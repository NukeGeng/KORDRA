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

            if (args is ["registry-credential", "inspect", _, _] || args is ["registry-credential", "apply", _, _, _])
            {
                using var options = LocalPilotHostOptions.LoadInstalled();
                return await LocalRegistryCredentialCommand.RunAsync(args[1..], options);
            }

            if (args is ["approval-policy", "inspect", _, _] || args is ["approval-policy", "apply", _, _, _])
            {
                return await LocalApprovalPolicyCommand.RunAsync(args[1..]);
            }

            if (args is ["artifact-recovery", "inspect", _, _] || args is ["artifact-recovery", "apply", _, _, _])
            {
                return await LocalArtifactRecoveryCommand.RunAsync(args[1..]);
            }

            if (args is ["approval-recovery", "inspect", _, _] || args is ["approval-recovery", "apply", _, _, _])
            {
                return await LocalApprovalRecoveryCommand.RunAsync(args[1..]);
            }

            if (args is ["release-recovery", "inspect", _, _] || args is ["release-recovery", "apply", _, _, _])
            {
                return await LocalReleaseRecoveryCommand.RunAsync(args[1..]);
            }

            Console.Error.WriteLine("Usage: Kordra.Api [--profile local-pilot | identity enroll-user|recover-user|disable-user | policy inspect <private-manifest> <private-admin-locator> | policy apply <private-manifest> <private-admin-locator> <approved-sha256> | credential inspect <private-manifest> <private-admin-locator> | credential apply <private-manifest> <private-admin-locator> <approved-sha256> | registry-credential inspect <private-manifest> <private-admin-locator> | registry-credential apply <private-manifest> <private-admin-locator> <approved-sha256> | approval-policy inspect <private-manifest> <private-admin-locator> | approval-policy apply <private-manifest> <private-admin-locator> <approved-sha256> | artifact-recovery inspect <private-plan> <private-admin-locator> | artifact-recovery apply <private-plan> <private-admin-locator> <approved-sha256> | approval-recovery inspect <private-plan> <private-admin-locator> | approval-recovery apply <private-plan> <private-admin-locator> <approved-sha256> | release-recovery inspect <private-plan> <private-admin-locator> | release-recovery apply <private-plan> <private-admin-locator> <approved-sha256>]");
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
