using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kordra.Modules.ConnectionManagement.Keychain;

internal interface ISecretLookup
{
    Task<SecretLookup> ReadAsync(KeychainRequest request, CancellationToken ct);
}
internal interface IKeychainProcess : IDisposable
{
    Stream Input { get; } Stream Output { get; } Stream Errors { get; }
    bool HasExited { get; } int ExitCode { get; }
    void Kill(); Task WaitForExitAsync(CancellationToken ct = default);
}
internal interface IKeychainProcessFactory { IKeychainProcess? Start(ProcessStartInfo start); }
internal sealed class OwnedKeychainProcess(Process process) : IKeychainProcess
{
    public Stream Input => process.StandardInput.BaseStream; public Stream Output => process.StandardOutput.BaseStream;
    public Stream Errors => process.StandardError.BaseStream; public bool HasExited => process.HasExited; public int ExitCode => process.ExitCode;
    public void Kill() => process.Kill(entireProcessTree: true);
    public Task WaitForExitAsync(CancellationToken ct = default) => process.WaitForExitAsync(ct);
    public void Dispose() => process.Dispose();
}
internal sealed class OwnedKeychainProcessFactory : IKeychainProcessFactory
{
    public IKeychainProcess? Start(ProcessStartInfo start) { var p = Process.Start(start); return p is null ? null : new OwnedKeychainProcess(p); }
}
internal sealed class NativeSecretLookup(KeychainAccessConfiguration configuration, IKeychainProcessFactory? factory = null) : ISecretLookup
{
    public Task<SecretLookup> ReadAsync(KeychainRequest request, CancellationToken ct) => OperatingSystem.IsMacOS() ? ReadCoreAsync(request, ct) : Task.FromResult(new SecretLookup(null));
    internal async Task<SecretLookup> ReadCoreAsync(KeychainRequest request, CancellationToken ct)
    {
        if (!request.IsValid || request.InstallationId != configuration.InstallationId ||
            !configuration.Locators.Values.Contains(request.KeychainPath, StringComparer.Ordinal)) return new(null);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(2));
        await Task.Yield(); // Process.Start never runs synchronously under the authority fence.
        IKeychainProcess? process = null; Task<IKeychainProcess?>? launch = null; SecretLookup? result = null; Task? stderr = null;
        try
        {
            budget.Token.ThrowIfCancellationRequested(); configuration.RequireExecutable();
            var start = new ProcessStartInfo(configuration.Executable) { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(configuration.Executable)! };
            start.ArgumentList.Add("--internal-keychain-read"); start.Environment.Clear();
            start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
            start.Environment["DOTNET_EnableDiagnostics"] = "0"; start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
            launch = Task.Run(() => (factory ?? new OwnedKeychainProcessFactory()).Start(start));
            process = await launch.WaitAsync(budget.Token); if (process is null) return new(null);
            stderr = DrainErrors(process.Errors, budget);
            await KeychainProtocol.WriteRequestAsync(process.Input, request, budget.Token);
            process.Input.Close();
            result = await KeychainProtocol.ReadResultAsync(process.Output, request.RequestId, budget.Token);
            await process.WaitForExitAsync(budget.Token); await stderr;
            if (process.ExitCode != 0) { result.Dispose(); return new(null); }
            return result;
        }
        catch
        {
            result?.Dispose();
            if (process is null && launch is not null)
            {
                if (launch.IsCompletedSuccessfully) process = launch.Result;
                else { _ = ReapLateLaunch(launch); return new(null, quiescent: false); }
            }
            if (process is null) return new(null);
            try
            {
                if (!process.HasExited) process.Kill();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await process.WaitForExitAsync(cleanup.Token); return new(null);
            }
            catch
            {
                // Keep ownership of this exact process handle until it eventually exits. No replacement is launched.
                var retained = process; process = null; _ = ReapEventually(retained); return new(null, quiescent: false);
            }
        }
        finally { process?.Dispose(); if (stderr is not null) _ = ObserveErrors(stderr); }
    }
    private static async Task DrainErrors(Stream stream, CancellationTokenSource budget)
    {
        var buffer = new byte[256]; var count = 0;
        try
        {
            while (true)
            {
                var n = await stream.ReadAsync(buffer, budget.Token); if (n == 0) return;
                count += n; if (count > 1024) { budget.Cancel(); throw new InvalidDataException("Credential process unavailable."); }
            }
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer); }
    }
    private static async Task ObserveErrors(Task task) { try { await task; } catch { } }
    private static async Task ReapLateLaunch(Task<IKeychainProcess?> launch)
    {
        IKeychainProcess? process = null;
        try
        {
            process = await launch;
            if (process is null) return;
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
        }
        catch { if (process is not null) { var retained = process; process = null; _ = ReapEventually(retained); } }
        finally { process?.Dispose(); }
    }
    private static async Task ReapEventually(IKeychainProcess process)
    { try { await process.WaitForExitAsync(); } catch { } finally { process.Dispose(); } }
}
