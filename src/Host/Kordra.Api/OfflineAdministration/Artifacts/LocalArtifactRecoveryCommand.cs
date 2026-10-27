using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kordra.Api;

public static class LocalArtifactRecoveryCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!(args is ["inspect", _, _] || args is ["apply", _, _, _])) return 2;
        using var custody = OfflineArtifactInstallation.Acquire();
        var planPath = LocalPilotHostOptions.RequirePrivateFile(args[1]);
        var locatorPath = LocalPilotHostOptions.RequirePrivateFile(args[2]);
        if (new FileInfo(planPath).Length > 16384 || new FileInfo(locatorPath).Length > 16384) return 2;
        var json = await File.ReadAllBytesAsync(planPath);
        var locator = (await File.ReadAllTextAsync(locatorPath, Encoding.UTF8)).Trim();
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(locator));
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var configuration = new LocalAuthenticationConfiguration(custody.InstallationId, Guid.NewGuid());
        var hasher = new PasswordHasher<KordraIdentityUser>(Options.Create(new PasswordHasherOptions
        { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = configuration.PasswordHashIterations }));
        var authentication = new LocalAuthenticationService(identity, new IdentityAccessAuditTransactionAdapter(), hasher, configuration, gate);
        await authentication.InitializeOfflineClockAsync(custody);
        var coordinator = new OfflineArtifactRecoveryCoordinator(custody.InstallationId, identity, gate, authentication, new ArtifactProcessQuiescence());
        if (args[0] == "inspect")
        {
            var bound = await coordinator.InspectAsync(custody, json);
            if (bound is null) return 2;
            Console.WriteLine(JsonSerializer.Serialize(new { plan = JsonSerializer.Deserialize<JsonElement>(bound.CanonicalBytes()), approvedSha256 = bound.Hash() }));
            return 0;
        }
        var result = await coordinator.ExecuteAsync(custody, json, args[3]); Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Status is "success" or "unchanged" ? 0 : result.Status is "invalid" or "denied" or "conflict" ? 2 : 1;
    }
}

/// <summary>Same private installation identity and OS lock as the API, with no runtime configuration or credential loading.</summary>
internal sealed class OfflineArtifactInstallation(Guid installationId, LocalInstallationProcessLock processLock) : IOfflineProvisioningLease, IDisposable
{
    public Guid InstallationId { get; } = installationId;
    public static OfflineArtifactInstallation Acquire()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kordra");
        return AcquireFromRoot(root);
    }
    private static OfflineArtifactInstallation AcquireFromRoot(string root)
    {
        LocalPilotHostOptions.RequirePrivateDirectory(root);
        var identityPath = LocalPilotHostOptions.RequirePrivateFile(Path.Combine(root, "installation.id"));
        var text = File.ReadAllText(identityPath).Trim();
        if (!Guid.TryParseExact(text, "D", out var installation) || installation == Guid.Empty)
            throw new InvalidOperationException("The installed Kordra identity is missing or invalid.");
        var directory = Path.Combine(root, installation.ToString("D"));
        LocalPilotHostOptions.RequirePrivateDirectory(directory);
        return new(installation, LocalInstallationProcessLock.Acquire(Path.Combine(directory, "process.lock")));
    }
    public void RequireHeld() => processLock.RequireHeld();
    public void Dispose() => processLock.Dispose();
}
