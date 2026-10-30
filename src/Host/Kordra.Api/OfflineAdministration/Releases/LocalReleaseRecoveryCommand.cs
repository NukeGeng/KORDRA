using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kordra.Api;

/// <summary>Explicit stopped-custody recovery; no application state is mutated.</summary>
public static class LocalReleaseRecoveryCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!(args is ["inspect", _, _] || args is ["apply", _, _, _])) return 2;
        using var custody = OfflineArtifactInstallation.Acquire();
        var planPath = LocalPilotHostOptions.RequirePrivateFile(args[1]);
        var locatorPath = LocalPilotHostOptions.RequirePrivateFile(args[2]);
        if (new FileInfo(planPath).Length > 16384 || new FileInfo(locatorPath).Length > 16384) return 2;
        var json = await File.ReadAllBytesAsync(planPath); var locator = (await File.ReadAllTextAsync(locatorPath, Encoding.UTF8)).Trim();
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(locator));
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var configuration = new LocalAuthenticationConfiguration(custody.InstallationId, Guid.NewGuid());
        var hasher = new PasswordHasher<KordraIdentityUser>(Options.Create(new PasswordHasherOptions
        { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = configuration.PasswordHashIterations }));
        var authentication = new LocalAuthenticationService(identity, new IdentityAccessAuditTransactionAdapter(), hasher, configuration, gate);
        await authentication.InitializeOfflineClockAsync(custody);
        var coordinator = new OfflineReleaseRecoveryCoordinator(custody.InstallationId, identity, new ArtifactProcessQuiescence());
        if (args[0] == "inspect")
        {
            var result = await coordinator.InspectAsync(custody, json);
            if (result.BoundPlan is null) { Console.WriteLine(JsonSerializer.Serialize(new { status = result.Status })); return 1; }
            Console.WriteLine(JsonSerializer.Serialize(new { status = result.Status,
                plan = JsonSerializer.Deserialize<JsonElement>(result.BoundPlan.CanonicalBytes()), approvedSha256 = result.BoundPlan.Hash() }));
            return 0;
        }
        var applied = await coordinator.ApplyAsync(custody, json, args[3]);
        Console.WriteLine(JsonSerializer.Serialize(new { status = applied.Status, commandId = applied.CommandId, intentId = applied.IntentId }));
        return applied.Status is "confirmed" or "no_commit" ? 0 : applied.Status is "invalid" or "denied" or "conflict" ? 2 : 1;
    }
}
