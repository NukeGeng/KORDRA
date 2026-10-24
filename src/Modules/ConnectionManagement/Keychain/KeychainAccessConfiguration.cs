using System.Collections.Frozen;
using System.Security.Cryptography;

namespace Kordra.Modules.ConnectionManagement.Keychain;

/// <summary>Installation-owned immutable configuration. Not an HTTP DTO and contains no secret material.</summary>
public sealed class KeychainAccessConfiguration
{
    public Guid InstallationId { get; }
    internal string Executable { get; }
    private readonly string _executableDigest;
    internal FrozenDictionary<Guid, string> Locators { get; }
    public KeychainAccessConfiguration(Guid installationId, string executable, string approvedExecutableSha256, IReadOnlyDictionary<Guid, string> locators)
    {
        if (installationId == Guid.Empty || !Path.IsPathFullyQualified(executable) || Path.GetFileName(executable) != "Kordra.Api" ||
            approvedExecutableSha256.Length != 64 || approvedExecutableSha256.Any(c => !char.IsAsciiHexDigitLower(c)) || locators.Count > 32 ||
            locators.Any(x => x.Key == Guid.Empty || !Path.IsPathFullyQualified(x.Value) || Path.GetFullPath(x.Value) != x.Value || x.Value.Any(char.IsControl)))
            throw new InvalidOperationException("Invalid private credential configuration.");
        InstallationId = installationId; Executable = executable; _executableDigest = approvedExecutableSha256; Locators = locators.ToFrozenDictionary();
        RequireExecutable();
    }
    internal void RequireExecutable()
    {
        if (!File.Exists(Executable) || new FileInfo(Executable).LinkTarget is not null ||
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Executable))) != _executableDigest)
            throw new InvalidOperationException("The installed credential executable is unavailable.");
    }
    public override string ToString() => "KeychainAccessConfiguration [redacted]";
}
