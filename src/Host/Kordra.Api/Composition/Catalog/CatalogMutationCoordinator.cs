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
    IAuthorityClock clock, ICatalogAuditTransactionFactory audits, ICatalogClockFloorWriter? clockFloor = null)
{
    private sealed record Prepared(string Status, object? Resource = null, Guid Id = default, long Revision = 0, bool Replay = false, Guid IntentId = default);

    public async Task<EnvironmentCreationV2Result> ExecuteEnvironmentV2Async(EnvironmentCreateV2Command command, CancellationToken ct = default)
    {
        if (command is null) return new("invalid");
        if (clockFloor is null) return new("unavailable");
        var result = await RunAsync(command.Key, command.TenantId, command.ProjectId, null, "project.manage", command,
            async (projects, _, key, hash, token) =>
            {
                var prepared = await projects.PrepareEnvironmentV2Async(command, key, hash, token);
                return new(prepared.Status, prepared, prepared.Environment?.Id ?? Guid.Empty, prepared.Environment?.Revision ?? 0,
                    false, prepared.MutationId);
            }, ct);
        return result.Status == "success" && result.Resource is EnvironmentCreationV2Result created ? created : new(result.Status);
    }

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
        var independentSubject = authority as IAuthenticatedCatalogSubjectReader;
        var principal = independentSubject is null ? await authority.ResolvePrincipalAsync(tenant, project, environment, permission, ct)
            : await independentSubject.CurrentPrincipalUnderAdmissionAsync(ct);
        if (principal is null || principal == Guid.Empty) return new("denied");
        var contentHash = Hash(JsonSerializer.Serialize(new { principal, commandType = command.GetType().Name, command }));
        await using var prjContext = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await prjContext.Database.BeginTransactionAsync(ct);
        await using var invContext = new InventoryDbContext(InventoryDbContextFactory.CreateEnlistedOptions(prjContext.Database.GetDbConnection()));
        await invContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var projects = new ProjectCatalogStore(prjContext, policies);
        var inventory = new TargetRegistryStore(invContext, projects);
        var keyHash = Hash(key);
        var replay = command is TargetCommand ? await inventory.ReadCommandReplayUnderAdmissionAsync(keyHash, contentHash, ct)
            : await projects.ReadCommandReplayUnderAdmissionAsync(keyHash, contentHash, ct);
        if (replay is { } stored)
        {
            if (stored.Status != "success") return new("conflict");
            if (command is EnvironmentCreateV2Command)
            {
                var saved = JsonSerializer.Deserialize<EnvironmentCreationV2Result>(stored.ResultJson);
                return saved is null ? new("unavailable") : new("success", saved, saved.Environment?.Id ?? Guid.Empty, saved.Environment?.Revision ?? 0, true, saved.MutationId);
            }
            if (command is TargetCommand)
            {
                var saved = JsonSerializer.Deserialize<TargetMetadata>(stored.ResultJson);
                return saved is null ? new("unavailable") : new("success", saved, saved.TargetId, saved.Revision, true);
            }
            var fact = JsonSerializer.Deserialize<CatalogFact>(stored.ResultJson);
            return fact is null ? new("unavailable") : new("success", fact, fact.Id, fact.Revision, true);
        }
        if (independentSubject is not null && await authority.ResolvePrincipalAsync(tenant, project, environment, permission, ct) != principal) return new("denied");
        var prepared = await prepare(projects, inventory, keyHash, contentHash, ct);
        if (prepared.Status != "success" || prepared.Replay) return prepared;
        var scopeKind = environment is not null ? "environment" : project == Guid.Empty ? "organization" : "project";
        var scopeId = environment ?? (project == Guid.Empty ? tenant : project);
        var intentId = prepared.IntentId == Guid.Empty ? Guid.NewGuid() : prepared.IntentId;
        var intent = new AuditIntent(1, intentId, intentId, AuditIntentStage.Mutation, principal, tenant, prepared.Id,
            permission, scopeKind, scopeId, command is EnvironmentCreateV2Command explicitPolicy ? explicitPolicy.ResourcePolicy.Revision : null,
            prepared.Revision, command is EnvironmentCreateV2Command ? "catalog-" + Hash(JsonSerializer.Serialize(new { contentHash, prepared.Resource })) : "catalog-owner-mutation", clock.GetTrustedUtcNow(), command is EnvironmentCreateV2Command correlated ? correlated.CorrelationId : intentId);
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
                if (command is EnvironmentCreateV2Command v2 && (policies is not IExactEnvironmentPolicyReader exact ||
                    !await exact.IsApplicableUnderAdmissionAsync(tenant, project, v2.Classification, v2.ResourcePolicy, token))) return false;
                if (command is TargetCommand targetCommand &&
                    await projects.ResolveOwnershipAsync(tenant, project, targetCommand.EnvironmentId, token) is null) return false;
                await projects.PersistAsync(token);
                await inventory.PersistAsync(token);
                if (clockFloor is not null) await clockFloor.StageAsync(prjContext.Database.GetDbConnection(), transaction.GetDbTransaction(), token);
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
