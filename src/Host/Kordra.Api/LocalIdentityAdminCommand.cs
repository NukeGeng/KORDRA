using Kordra.Modules.IdentityAccess.Authentication;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace Kordra.Api;

/// <summary>Offline-only account operations protected by the installation process lock.</summary>
public static class LocalIdentityAdminCommand
{
    public static async Task<int> RunAsync(string operation, LocalPilotHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ClaimOfflineCustody().RequireHeld();
        if (operation is not ("enroll-user" or "recover-user" or "disable-user"))
        {
            Console.Error.WriteLine("Usage: Kordra.Api identity enroll-user|recover-user|disable-user");
            return 2;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Offline account operations require an interactive console for hidden password input.");
            return 2;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdentityAuditTransactionFactory, IdentityAccessAuditTransactionAdapter>();
        services.AddLocalAuthenticationServices(
            options.RuntimeConnectionString,
            options.Authentication,
            options.PasswordPolicy);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var authentication = provider.GetRequiredService<LocalAuthenticationService>();
        return operation switch
        {
            "enroll-user" => await EnrollAsync(authentication),
            "recover-user" => await RecoverAsync(authentication),
            "disable-user" => await DisableAsync(authentication),
            _ => 2
        };
    }

    private static async Task<int> EnrollAsync(LocalAuthenticationService authentication)
    {
        Console.Write("Username: ");
        var userName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.Error.WriteLine("A username is required.");
            return 2;
        }

        var password = ReadHiddenPassword("Password: ");
        var confirmation = ReadHiddenPassword("Confirm password: ");
        if (password is null || confirmation is null)
        {
            Console.Error.WriteLine("Password input exceeded the supported console limit.");
            return 2;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The password entries did not match.");
            return 2;
        }

        try
        {
            return Report(await authentication.EnrollOfflineAsync(userName, password), "Enrollment");
        }
        finally
        {
            password = null;
            confirmation = null;
        }
    }

    private static async Task<int> RecoverAsync(LocalAuthenticationService authentication)
    {
        if (!TryReadPrincipal(out var principalId))
        {
            return 2;
        }

        if (!Confirm($"Replace credentials for principal {principalId:D}? [y/N] "))
        {
            Console.WriteLine("Cancelled.");
            return 2;
        }

        var password = ReadHiddenPassword("New password: ");
        var confirmation = ReadHiddenPassword("Confirm new password: ");
        if (password is null || confirmation is null)
        {
            Console.Error.WriteLine("Password input exceeded the supported console limit.");
            return 2;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The password entries did not match.");
            return 2;
        }

        try
        {
            return Report(await authentication.RecoverOfflineAsync(principalId, password), "Recovery");
        }
        finally
        {
            password = null;
            confirmation = null;
        }
    }

    private static async Task<int> DisableAsync(LocalAuthenticationService authentication)
    {
        if (!TryReadPrincipal(out var principalId))
        {
            return 2;
        }

        if (!Confirm($"Disable principal {principalId:D} and revoke its sessions? [y/N] "))
        {
            Console.WriteLine("Cancelled.");
            return 2;
        }

        return Report(await authentication.DisableOfflineAsync(principalId), "Disable");
    }

    private static bool TryReadPrincipal(out Guid principalId)
    {
        principalId = Guid.Empty;
        Console.Write("Principal ID (D format): ");
        if (!Guid.TryParseExact(Console.ReadLine(), "D", out principalId) || principalId == Guid.Empty)
        {
            Console.Error.WriteLine("A valid principal ID is required.");
            return false;
        }

        return true;
    }

    private static bool Confirm(string prompt)
    {
        Console.Write(prompt);
        var answer = Console.ReadLine();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadHiddenPassword(string prompt)
    {
        Console.Write(prompt);
        var characters = new List<char>();
        var tooLong = false;
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                var password = tooLong ? null : new string(CollectionsMarshal.AsSpan(characters));
                CollectionsMarshal.AsSpan(characters).Clear();
                characters.Clear();
                return password;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                {
                    characters.RemoveAt(characters.Count - 1);
                }

                continue;
            }

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && !tooLong)
            {
                if (characters.Count == 512)
                {
                    tooLong = true;
                    CollectionsMarshal.AsSpan(characters).Clear();
                    characters.Clear();
                }
                else
                {
                    characters.Add(key.KeyChar);
                }
            }
        }
    }

    private static int Report(LocalCommandResult result, string operation)
    {
        if (result.Status == LocalAuthenticationStatus.Success && result.PrincipalId is { } principalId)
        {
            Console.WriteLine($"{operation} completed. Principal: {principalId:D}; correlation: {result.CorrelationId:D}.");
            return 0;
        }

        Console.Error.WriteLine($"{operation} was not confirmed ({result.Status}). No credential value was displayed.");
        return 1;
    }
}
