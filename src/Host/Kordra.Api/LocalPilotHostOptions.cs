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
            if (!certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow ||
                certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
                !string.Equals(certificate.GetNameInfo(X509NameType.DnsName, false), "localhost", StringComparison.OrdinalIgnoreCase))
            {
                certificate.Dispose();
                throw new InvalidOperationException("The local-pilot TLS certificate is expired, incomplete, or not valid for localhost.");
            }

            var denylistPath = RequirePrivateFile(config.PasswordDenylistFile);
            var denylist = LocalPasswordPolicy.Load(
                denylistPath,
                config.PasswordDenylistVersion,
                config.PasswordDenylistProvenance,
                config.PasswordDenylistSha256);
            return new LocalPilotHostOptions(
                connectionString,
                certificate,
                new LocalAuthenticationConfiguration(installationId, Guid.NewGuid()),
                denylist,
                processLock);
        }
        catch
        {
            processLock.Dispose();
            throw;
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

    private static string RequirePrivateFile(string path)
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
        string PasswordDenylistSha256);
}

internal sealed class LocalInstallationProcessLock : IDisposable
{
    private readonly FileStream _stream;

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

    public void Dispose() => _stream.Dispose();
}
