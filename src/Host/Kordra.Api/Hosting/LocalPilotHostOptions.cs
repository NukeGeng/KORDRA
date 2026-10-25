using Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;

namespace Kordra.Api;

/// <summary>Private installed configuration for the explicit local-pilot profile.</summary>
public sealed class LocalPilotHostOptions : IDisposable
{
    private readonly LocalInstallationProcessLock _processLock;
    private int _custodyMode;

    internal void RequireRuntimeCustody()
    {
        _processLock.RequireHeld();
        if (Interlocked.CompareExchange(ref _custodyMode, 1, 0) != 0)
            throw new InvalidOperationException("Installation custody cannot be reused for another runtime or offline operation.");
    }
    internal Kordra.Modules.IdentityAccess.Contracts.IOfflineProvisioningLease ClaimOfflineCustody()
    {
        _processLock.RequireHeld();
        var previous = Interlocked.CompareExchange(ref _custodyMode, 2, 0);
        if (previous is not (0 or 2)) throw new InvalidOperationException("Runtime installation custody cannot authorize offline work.");
        return new OfflineCustody(this);
    }
    private sealed class OfflineCustody(LocalPilotHostOptions options) : Kordra.Modules.IdentityAccess.Contracts.IOfflineProvisioningLease
    {
        public void RequireHeld()
        {
            options._processLock.RequireHeld();
            if (Volatile.Read(ref options._custodyMode) != 2) throw new InvalidOperationException("Offline installation custody was released.");
        }
    }

    internal LocalPilotHostOptions(
        string runtimeConnectionString,
        X509Certificate2 certificate,
        LocalAuthenticationConfiguration authentication,
        LocalPasswordPolicy passwordPolicy,
        LocalInstallationProcessLock processLock)
    {
        RuntimeConnectionString = runtimeConnectionString;
        Certificate = certificate;
        Authentication = authentication;
        PasswordPolicy = passwordPolicy;
        _processLock = processLock;
    }

    public KubernetesTrustConfiguration? KubernetesTrust { get; private init; }
    public NomadTrustConfiguration? NomadTrust { get; private init; }
    public KeychainAccessConfiguration? CredentialAccess { get; private init; }

    public string RuntimeConnectionString { get; }
    public X509Certificate2 Certificate { get; }
    public LocalAuthenticationConfiguration Authentication { get; }
    public LocalPasswordPolicy PasswordPolicy { get; }

    public static LocalPilotHostOptions LoadInstalled()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kordra");
        RequirePrivateDirectory(root);
        var identityPath = Path.Combine(root, "installation.id");
        RequirePrivateFile(identityPath);
        var installationText = File.ReadAllText(identityPath).Trim();
        if (!Guid.TryParseExact(installationText, "D", out var installationId) || installationId == Guid.Empty)
        {
            throw new InvalidOperationException("The installed Kordra identity is missing or invalid.");
        }

        var dataDirectory = Path.Combine(root, installationId.ToString("D"));
        RequirePrivateDirectory(dataDirectory);
        var processLock = LocalInstallationProcessLock.Acquire(Path.Combine(dataDirectory, "process.lock"));
        try
        {
            var configPath = Path.Combine(dataDirectory, "local-pilot.json");
            RequirePrivateFile(configPath);
            var config = JsonSerializer.Deserialize<InstalledLocalPilotConfiguration>(
                File.ReadAllBytes(configPath),
                new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidOperationException("Installed local-pilot configuration is invalid.");

            var runtimeLocator = RequirePrivateFile(config.RuntimeConnectionStringFile);
            var connectionString = File.ReadAllText(runtimeLocator).Trim();
            IdentityAccessDbContextFactory.ValidateRuntimeTarget(connectionString);

            var certificatePath = RequirePrivateFile(config.TlsCertificatePemFile);
            var privateKeyPath = RequirePrivateFile(config.TlsPrivateKeyPemFile);
            var certificate = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
            try
            {
                ValidateInstalledCertificate(certificate);
            }
            catch
            {
                certificate.Dispose();
                throw;
            }

            try
            {
                var denylistPath = RequirePrivateFile(config.PasswordDenylistFile);
                var denylist = LocalPasswordPolicy.Load(
                    denylistPath,
                    config.PasswordDenylistVersion,
                    config.PasswordDenylistProvenance,
                    config.PasswordDenylistSha256);
                NomadTrustConfiguration? nomad = null;
                KubernetesTrustConfiguration? kubernetes = null;
                KeychainAccessConfiguration? access = null;
                if (config.NomadTrustConfigurationFile is not null || config.KubernetesTrustConfigurationFile is not null)
                {
                    if ((config.NomadTrustConfigurationFile is null) != (config.NomadTrustConfigurationSha256 is null) ||
                        (config.KubernetesTrustConfigurationFile is null) != (config.KubernetesTrustConfigurationSha256 is null) || config.CredentialExecutableFile is null ||
                        config.CredentialExecutableSha256 is null || config.CredentialKeychainLocators is null)
                        throw new InvalidOperationException("Installed provider configuration is incomplete.");
                    if (config.NomadTrustConfigurationFile is not null)
                        nomad = NomadTrustConfiguration.Load(RequirePrivateFile(config.NomadTrustConfigurationFile), config.NomadTrustConfigurationSha256!, installationId);
                    if (config.KubernetesTrustConfigurationFile is not null)
                        kubernetes = KubernetesTrustConfiguration.Load(RequirePrivateFile(config.KubernetesTrustConfigurationFile), config.KubernetesTrustConfigurationSha256!, installationId);
                    access = new(installationId, config.CredentialExecutableFile, config.CredentialExecutableSha256, config.CredentialKeychainLocators);
                }
                else if (config.KubernetesTrustConfigurationSha256 is not null || config.NomadTrustConfigurationSha256 is not null || config.CredentialExecutableFile is not null ||
                    config.CredentialExecutableSha256 is not null || config.CredentialKeychainLocators is not null)
                    throw new InvalidOperationException("Credential access requires explicit provider trust configuration.");
                return new LocalPilotHostOptions(
                    connectionString,
                    certificate,
                    new LocalAuthenticationConfiguration(installationId, Guid.NewGuid()),
                    denylist,
                    processLock) { NomadTrust = nomad, KubernetesTrust = kubernetes, CredentialAccess = access };
            }
            catch { certificate.Dispose(); throw; }
        }
        catch
        {
            processLock.Dispose();
            throw;
        }
    }

    // An injectable chain is internal and used only by deterministic certificate fixtures.
    // Installed startup always creates the native machine-trust chain here.
    internal static void ValidateInstalledCertificate(X509Certificate2 certificate, X509Chain? testChain = null)
    {
        using var machineChain = testChain is null ? new X509Chain() : null;
        var chain = testChain ?? machineChain!;
        var serverAuthentication = new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1");
        chain.ChainPolicy.ApplicationPolicy.Add(serverAuthentication);
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        if (!certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow ||
            certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
            !certificate.MatchesHostname("localhost", allowWildcards: false, allowCommonName: false) ||
            eku is null || !eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                .Any(oid => oid.Value == serverAuthentication.Value) || !chain.Build(certificate))
        {
            throw new InvalidOperationException("The local-pilot TLS certificate must be trusted and valid for localhost server authentication.");
        }
    }

    internal static LocalPilotHostOptions CreateForTests(
        string runtimeConnectionString,
        X509Certificate2 certificate,
        LocalAuthenticationConfiguration authentication,
        LocalPasswordPolicy passwordPolicy,
        string lockFilePath) =>
        new(runtimeConnectionString, certificate, authentication, passwordPolicy,
            LocalInstallationProcessLock.Acquire(lockFilePath));

    public void Dispose()
    {
        Certificate.Dispose();
        _processLock.Dispose();
    }

    internal static string RequirePrivateFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.LinkTarget is not null)
        {
            throw new InvalidOperationException("A required private local-pilot file is missing or a symlink.");
        }

        EnsurePrivateUnixMode(fullPath, directory: false);
        for (var parent = info.Directory; parent is not null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("A local-pilot file path traverses a symlink.");
            }
        }

        return fullPath;
    }

    private static void RequirePrivateDirectory(string path)
    {
        var info = new DirectoryInfo(Path.GetFullPath(path));
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("A required private local-pilot directory is missing or a symlink.");
        }

        EnsurePrivateUnixMode(info.FullName, directory: true);
    }

    private static void EnsurePrivateUnixMode(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        var forbidden = directory ? UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute
            : UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & forbidden) != 0)
        {
            throw new InvalidOperationException("Local-pilot files and directories must be private to the current user.");
        }
    }

    private sealed record InstalledLocalPilotConfiguration(
        string RuntimeConnectionStringFile,
        string TlsCertificatePemFile,
        string TlsPrivateKeyPemFile,
        string PasswordDenylistFile,
        string PasswordDenylistVersion,
        string PasswordDenylistProvenance,
        string PasswordDenylistSha256,
        string? NomadTrustConfigurationFile = null,
        string? NomadTrustConfigurationSha256 = null,
        string? CredentialExecutableFile = null,
        string? CredentialExecutableSha256 = null,
        Dictionary<Guid, string>? CredentialKeychainLocators = null,
        string? KubernetesTrustConfigurationFile = null,
        string? KubernetesTrustConfigurationSha256 = null);
}

internal sealed class LocalInstallationProcessLock : IDisposable
{
    private readonly FileStream _stream;
    private int _disposed;

    private LocalInstallationProcessLock(FileStream stream) => _stream = stream;

    public static LocalInstallationProcessLock Acquire(string lockFilePath)
    {
        var fullPath = Path.GetFullPath(lockFilePath);
        var info = new FileInfo(fullPath);
        if (info.LinkTarget is not null)
        {
            throw new InvalidOperationException("The Kordra process lock cannot be a symlink.");
        }

        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
        }
        catch (IOException)
        {
            throw new InvalidOperationException("Another Kordra local-pilot or offline account process already holds the installation lock.");
        }

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return new LocalInstallationProcessLock(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal void RequireHeld()
    { if (Volatile.Read(ref _disposed) != 0 || _stream.SafeFileHandle.IsClosed) throw new InvalidOperationException("The installation process lock was released."); }
    public void Dispose()
    { if (Interlocked.Exchange(ref _disposed, 1) == 0) _stream.Dispose(); }
}
