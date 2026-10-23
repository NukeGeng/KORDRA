namespace Kordra.DbMigrator;

/// <summary>Uses the same canonical OS lock as API/offline provisioning; no root override or database mutation.</summary>
public sealed class MigrationInstallationCustody : IDisposable
{
    private readonly FileStream _lock;
    private MigrationInstallationCustody(FileStream held) => _lock = held;
    public static MigrationInstallationCustody AcquireInstalled()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kordra");
        RequirePrivate(root, directory: true);
        var identityFile = Path.Combine(root, "installation.id"); RequirePrivate(identityFile, directory: false);
        if (!Guid.TryParseExact(File.ReadAllText(identityFile).Trim(), "D", out var installation) || installation == Guid.Empty)
            throw new InvalidOperationException("A canonical installation identity is required for offline migration.");
        var data = Path.Combine(root, installation.ToString("D")); RequirePrivate(data, directory: true);
        return AcquireFile(Path.Combine(data, "process.lock"));
    }
    // Fixture seam is internal; the executable never accepts a lock path or installation override.
    internal static MigrationInstallationCustody AcquireFile(string path)
    {
        if (new FileInfo(path).LinkTarget is not null) throw new InvalidOperationException("Migration lock cannot be a symlink.");
        FileStream held;
        try { held = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough); }
        catch (IOException) { throw new InvalidOperationException("API or another offline operation holds installation custody."); }
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return new(held);
        }
        catch { held.Dispose(); throw; }
    }
    private static void RequirePrivate(string path, bool directory)
    {
        FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null) throw new InvalidOperationException("Private canonical installation files are required.");
        for (DirectoryInfo? parent = directory ? (DirectoryInfo)info : ((FileInfo)info).Directory; parent is not null; parent = parent.Parent)
            if (parent.LinkTarget is not null) throw new InvalidOperationException("Canonical installation cannot use symlink aliases.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
            (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("Installation files must be private to the local operator.");
    }
    public void Dispose() => _lock.Dispose();
}
