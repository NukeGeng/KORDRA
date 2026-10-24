namespace Kordra.Api;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--internal-keychain-read"])
        {
            if (!Console.IsInputRedirected || !Console.IsOutputRedirected || !Console.IsErrorRedirected) return 2;
            return await Kordra.Modules.ConnectionManagement.Keychain.KeychainChild.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
        }
        if (args.Length == 0)
        {
            await using var healthOnlyApp = LocalApiHost.Create();
            await healthOnlyApp.RunAsync();
            return 0;
        }

        try
        {
            if (args is ["--profile", "local-pilot"])
            {
                using var options = LocalPilotHostOptions.LoadInstalled();
                await using var app = LocalApiHost.CreateLocalPilot(options);
                await app.Services.GetRequiredService<Modules.IdentityAccess.Authentication.LocalAuthenticationService>()
                    .InitializeBootAsync();
                await app.RunAsync();
                return 0;
            }

            if (args.Length == 2 && args[0] == "identity")
            {
                using var options = LocalPilotHostOptions.LoadInstalled();
                return await LocalIdentityAdminCommand.RunAsync(args[1], options);
            }

            if (args is ["policy", "inspect", _, _] || args is ["policy", "apply", _, _, _])
            {
                using var options = LocalPilotHostOptions.LoadInstalled();
                return await LocalResourcePolicyCommand.RunAsync(args[1..], options);
            }

            if (args is ["credential", "inspect", _, _] || args is ["credential", "apply", _, _, _])
            {
                using var options = LocalPilotHostOptions.LoadInstalled();
                return await LocalCredentialCommand.RunAsync(args[1..], options);
            }

            Console.Error.WriteLine("Usage: Kordra.Api [--profile local-pilot | identity enroll-user|recover-user|disable-user | policy inspect <private-manifest> <private-admin-locator> | policy apply <private-manifest> <private-admin-locator> <approved-sha256> | credential inspect <private-manifest> <private-admin-locator> | credential apply <private-manifest> <private-admin-locator> <approved-sha256>]");
            return 2;
        }
        catch
        {
            // Connection locators, certificate paths and provider exceptions are private.
            Console.Error.WriteLine("Kordra local operation failed; no private configuration was displayed.");
            return 1;
        }
    }
}
