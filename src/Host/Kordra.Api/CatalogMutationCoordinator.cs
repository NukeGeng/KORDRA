using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

/// <summary>Technical transaction composition only. Owners prepare and persist their own records. No HTTP registration.</summary>
public sealed class CatalogMutationCoordinator(IDbContextFactory<ProjectDbContext> contexts,
    ICatalogAuthority authority, IEnvironmentPolicyReader policies, FairAsyncReaderWriterAdmissionGate gate,
    IAuthorityClock clock, ICatalogAuditTransactionFactory audits)
{
    private sealed record Prepared(string Status, object? Resource = null, Guid Id = default, long Revision = 0, bool Replay = false);

    public async Task<CatalogMutationResult> ExecuteAsync(CatalogCommand command, CancellationToken ct = default)
    {
        if (command is null) return new("invalid");
        var result = await RunAsync(command.Key, command.TenantId, command.Kind == "project" && command.Operation == "create" ? Guid.Empty : command.ProjectId,
            null, "project.manage", command, async (prj, _, key, hash, token) =>
            {
                var p = await prj.PrepareAsync(command, key, hash, token);
                return new(p.Status, p.Resource, p.Resource?.Id ?? Guid.Empty, p.Resource?.Revision ?? 0, p.IsReplay);
            }, ct);
        return new(result.Status, result.Resource as CatalogFact);
    }

    public async Task<TargetMutationResult> ExecuteAsync(TargetCommand command, CancellationToken ct = default)
    {
        if (command is null) return new("invalid");
        var result = await RunAsync(command.Key, command.TenantId, command.ProjectId, command.EnvironmentId, "target.manage", command,
            async (prj, inv, key, hash, token) =>
            {
                var p = await inv.PrepareAsync(command, key, hash, token);
                if (p.Status == "success" && !p.IsReplay && command.Operation == "register" &&
                    !await prj.BindTargetAsync(p.Resource!.TargetId, command.TenantId, command.ProjectId, command.EnvironmentId, token)) return new("denied");
                return new(p.Status, p.Resource, p.Resource?.TargetId ?? Guid.Empty, p.Resource?.Revision ?? 0, p.IsReplay);
            }, ct);
        return new(result.Status, result.Resource as TargetMetadata);
    }

    private async Task<Prepared> RunAsync(string key, Guid tenant, Guid project, Guid? environment, string permission,
        object command, Func<ProjectCatalogStore, TargetRegistryStore, string, string, CancellationToken, Task<Prepared>> prepare, CancellationToken ct)
    {
        if (tenant == Guid.Empty || key is null || key.Length is < 16 or > 128 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-'))) return new("invalid");
        await using var admission = await gate.AcquireWriteAsync(ct);
        var principal = await authority.ResolvePrincipalAsync(tenant, project, environment, permission, ct);
        if (principal is null || principal == Guid.Empty) return new("denied");
        var contentHash = Hash(JsonSerializer.Serialize(new { principal, commandType = command.GetType().Name, command }));
        await using var prjContext = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await prjContext.Database.BeginTransactionAsync(ct);
        await using var invContext = new InventoryDbContext(InventoryDbContextFactory.CreateEnlistedOptions(prjContext.Database.GetDbConnection()));
        await invContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var projects = new ProjectCatalogStore(prjContext, policies);
        var inventory = new TargetRegistryStore(invContext, projects);
        var prepared = await prepare(projects, inventory, Hash(key), contentHash, ct);
        if (prepared.Status != "success" || prepared.Replay) return prepared;
        var scopeKind = environment is not null ? "environment" : project == Guid.Empty ? "organization" : "project";
        var scopeId = environment ?? (project == Guid.Empty ? tenant : project);
        var intentId = Guid.NewGuid();
        var intent = new AuditIntent(1, intentId, intentId, AuditIntentStage.Mutation, principal, tenant, prepared.Id,
            permission, scopeKind, scopeId, null, prepared.Revision, "catalog-owner-mutation", clock.GetTrustedUtcNow(), intentId);
        // A setup failure has not attempted a commit and must not poison process admission.
        await using var audit = await audits.EnlistAsync(prjContext.Database.GetDbConnection(), transaction.GetDbTransaction(), ct);
        AuditAppendOutcome outcome;
        try
        {
            outcome = await audit.CommitAsync(intent, async token =>
            {
                if (await authority.ResolvePrincipalAsync(tenant, project, environment, permission, token) != principal) return false;
                // Owner policy may have expired/revoked during audit staging; never rely on preparation alone.
                if (command is CatalogCommand { Kind: "environment", Operation: "create" } environmentCommand &&
                    !await policies.IsApplicableAsync(tenant, project, environmentCommand.Classification!, environmentCommand.PolicyRevision!.Value, token)) return false;
                if (command is TargetCommand targetCommand &&
                    await projects.ResolveOwnershipAsync(tenant, project, targetCommand.EnvironmentId, token) is null) return false;
                await projects.PersistAsync(token);
                await inventory.PersistAsync(token);
                return true;
            }, ct);
        }
        catch { gate.Close(); throw; }
        if (outcome.Status == AuditAppendStatus.Unknown) gate.Close();
        return outcome.Status == AuditAppendStatus.Durable ? prepared : new("unavailable");
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public interface ICatalogAuditTransactionFactory
{
    ValueTask<ICatalogAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default);
}
public interface ICatalogAuditTransaction : IAsyncDisposable
{
    Task<AuditAppendOutcome> CommitAsync(AuditIntent intent, Func<CancellationToken, Task<bool>> saveOwners, CancellationToken ct = default);
}

public sealed class CatalogAuditTransactionFactory : ICatalogAuditTransactionFactory
{
    public async ValueTask<ICatalogAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default) =>
        new Enlisted(await AuditTransactionContext.CreateAsync(connection, transaction, ct));
    private sealed class Enlisted(AuditTransactionContext audit) : ICatalogAuditTransaction
    {
        public async Task<AuditAppendOutcome> CommitAsync(AuditIntent intent, Func<CancellationToken, Task<bool>> saveOwners, CancellationToken ct = default)
        {
            if ((await audit.StageAsync(intent, ct)).Status != AuditTransactionStageStatus.Staged)
            { await audit.RollbackAsync(CancellationToken.None); return AuditAppendOutcome.Unavailable(); }
            try
            {
                if (!await saveOwners(ct)) { await audit.RollbackAsync(CancellationToken.None); return AuditAppendOutcome.Rejected("authority_changed"); }
            }
            catch
            {
                await audit.RollbackAsync(CancellationToken.None); return AuditAppendOutcome.Unavailable();
            }
            return await audit.CommitAsync(ct);
        }
        public ValueTask DisposeAsync() => audit.DisposeAsync();
    }
}
