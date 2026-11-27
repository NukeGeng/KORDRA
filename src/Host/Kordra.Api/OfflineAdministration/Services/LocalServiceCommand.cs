using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kordra.Api;

/// <summary>
/// 12.3d (D6): <c>service inspect|apply &lt;manifest&gt; &lt;admin-locator&gt; [approved-sha256]</c> with the API stopped, and
/// <c>service spki &lt;certificate.pem&gt;</c>, which prints the pin of a public certificate and never reads a private key.
/// </summary>
public static class LocalServiceCommand
{
    public static int Spki(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length > 16 * 1024) return 2;
        var pin = ServiceCertificatePin.FromPem(File.ReadAllText(file.FullName, Encoding.UTF8));
        if (pin is null) return 2;
        Console.WriteLine(pin);
        return 0;
    }

    public static async Task<int> RunAsync(string[] args, LocalPilotHostOptions options)
    {
        if (!(args is ["inspect", _, _] || args is ["apply", _, _, _])) return 2;
        var custody = options.ClaimOfflineCustody();
        var manifestPath = LocalPilotHostOptions.RequirePrivateFile(args[1]);
        var locatorPath = LocalPilotHostOptions.RequirePrivateFile(args[2]);
        if (new FileInfo(manifestPath).Length > 16 * 1024 || new FileInfo(locatorPath).Length > 16 * 1024) return 2;
        var json = await File.ReadAllBytesAsync(manifestPath);
        var locator = (await File.ReadAllTextAsync(locatorPath, Encoding.UTF8)).Trim();
        var manifest = ServiceManifestDocument.Parse(json);
        if (manifest is null || manifest.InstallationId != options.Authentication.InstallationId) return 2;
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(locator));
        var lookups = new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateOfflineMigrationOptions(locator)));
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IIdentityAuditTransactionFactory, IdentityAccessAuditTransactionAdapter>();
        services.AddLocalAuthenticationServices(options.RuntimeConnectionString, options.Authentication, options.PasswordPolicy);
        await using var provider = services.BuildServiceProvider();
        var authentication = provider.GetRequiredService<LocalAuthenticationService>();
        await authentication.InitializeOfflineClockAsync(custody);
        var coordinator = new OfflineServiceCoordinator(options.Authentication.InstallationId, identity, lookups,
            provider.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), authentication);
        if (args[0] == "inspect")
        {
            var impact = await coordinator.InspectAsync(custody, json);
            if (impact is null) return 2;
            Console.WriteLine(JsonSerializer.Serialize(impact)); return 0;
        }
        var result = await coordinator.ApplyAsync(custody, json, args[3]);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Status == "success" ? 0 : result.Status is "invalid" or "denied" or "conflict" ? 2 : 1;
    }
}
