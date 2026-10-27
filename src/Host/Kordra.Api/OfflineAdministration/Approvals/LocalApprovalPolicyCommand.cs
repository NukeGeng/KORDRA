using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kordra.Api;

public static class LocalApprovalPolicyCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!(args is ["inspect", _, _] || args is ["apply", _, _, _])) return 2;
        using var custody = OfflineArtifactInstallation.Acquire();
        var manifestPath = LocalPilotHostOptions.RequirePrivateFile(args[1]);
        var locatorPath = LocalPilotHostOptions.RequirePrivateFile(args[2]);
        if (new FileInfo(manifestPath).Length > 128 * 1024 || new FileInfo(locatorPath).Length > 16 * 1024) return 2;
        var manifest = await File.ReadAllBytesAsync(manifestPath);
        var locator = (await File.ReadAllTextAsync(locatorPath, Encoding.UTF8)).Trim();
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(locator));
        var audit = new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateOfflineMigrationOptions(locator)));
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var configuration = new LocalAuthenticationConfiguration(custody.InstallationId, Guid.NewGuid());
        var hasher = new PasswordHasher<KordraIdentityUser>(Options.Create(new PasswordHasherOptions
        { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = configuration.PasswordHashIterations }));
        var authentication = new LocalAuthenticationService(identity, new IdentityAccessAuditTransactionAdapter(), hasher, configuration, gate);
        await authentication.InitializeOfflineClockAsync(custody);
        var coordinator = new OfflineApprovalPolicyCoordinator(custody.InstallationId, identity, audit, gate, authentication);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (args[0] == "inspect")
        {
            var result = await coordinator.InspectAsync(custody, manifest);
            if (result is null) return 2;
            Console.WriteLine(JsonSerializer.Serialize(result, options));
            return 0;
        }
        var applied = await coordinator.ApplyAsync(custody, manifest, args[3]);
        if (applied.Status == "success")
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                applied.SchemaVersion,
                applied.ManifestId,
                applied.PolicyId,
                applied.PolicyRevision,
                applied.PayloadHash,
                applied.CommandId,
                applied.Replayed
            }, options));
            return 0;
        }
        Console.Error.WriteLine("Approval policy operation: " + applied.Status);
        return applied.Status is "invalid" or "denied" or "conflict" ? 2 : 1;
    }
}
