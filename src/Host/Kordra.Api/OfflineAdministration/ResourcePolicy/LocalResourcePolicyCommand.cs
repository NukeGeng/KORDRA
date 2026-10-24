using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kordra.Api;

public static class LocalResourcePolicyCommand
{
    public static async Task<int> RunAsync(string[] args, LocalPilotHostOptions options)
    {
        if (!(args is ["inspect", _, _] || args is ["apply", _, _, _])) return 2;
        var custody = options.ClaimOfflineCustody();
        var manifestPath = LocalPilotHostOptions.RequirePrivateFile(args[1]);
        var locatorPath = LocalPilotHostOptions.RequirePrivateFile(args[2]);
        if (new FileInfo(manifestPath).Length > 128 * 1024 || new FileInfo(locatorPath).Length > 16 * 1024) return 2;
        var json = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8);
        var locator = (await File.ReadAllTextAsync(locatorPath, Encoding.UTF8)).Trim();
        var manifest = ResourcePolicyManifest.Parse(json);
        if (manifest is null || manifest.InstallationId != options.Authentication.InstallationId) return 2;
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(locator));
        var tenants = new PooledDbContextFactory<TenantDbContext>(TenantDbContextFactory.CreateRuntimeOptions(options.RuntimeConnectionString));
        var lookups = new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateOfflineMigrationOptions(locator)));
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IIdentityAuditTransactionFactory, IdentityAccessAuditTransactionAdapter>();
        services.AddLocalAuthenticationServices(options.RuntimeConnectionString, options.Authentication, options.PasswordPolicy);
        await using var provider = services.BuildServiceProvider();
        var authentication = provider.GetRequiredService<LocalAuthenticationService>();
        await authentication.InitializeOfflineClockAsync(custody);
        var gate = provider.GetRequiredService<FairAsyncReaderWriterAdmissionGate>();
        var coordinator = new OfflineResourcePolicyCoordinator(options.Authentication.InstallationId, identity,
            new TenantOrganizationAuthority(tenants), lookups, gate, authentication);
        if (args[0] == "inspect")
        {
            var impact = await coordinator.InspectAsync(custody, json);
            if (impact is null) return 2;
            Console.WriteLine(JsonSerializer.Serialize(impact)); return 0;
        }
        var result = await coordinator.ExecuteAsync(custody, json, args[3]);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Status == "success" ? 0 : result.Status is "invalid" or "denied" or "conflict" ? 2 : 1;
    }
}
