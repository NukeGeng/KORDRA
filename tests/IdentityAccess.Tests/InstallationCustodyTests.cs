using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kordra.Api;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class InstallationCustodyTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void One_canonical_lock_cannot_be_reused_across_runtime_and_offline_modes_or_after_disposal(bool runtimeFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), "kordra-t06-custody-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var denylist = Path.Combine(root, "denylist.json"); var bytes = "[\"SyntheticDeniedPassword\"]"u8.ToArray(); File.WriteAllBytes(denylist, bytes);
            var policy = LocalPasswordPolicy.Load(denylist, "test-v1", "synthetic fixture", Convert.ToHexStringLower(SHA256.HashData(bytes)));
            using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var type = typeof(LocalPilotHostOptions);
            var create = type.GetMethod("CreateForTests", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var options = (LocalPilotHostOptions)create.Invoke(null, ["Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app", certificate,
                new LocalAuthenticationConfiguration(Guid.NewGuid(), Guid.NewGuid()), policy, Path.Combine(root, "process.lock")])!;
            var migration = typeof(Kordra.DbMigrator.MigrationInstallationCustody).GetMethod("AcquireFile", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => migration.Invoke(null, [Path.Combine(root, "process.lock")])).InnerException);
            var runtime = type.GetMethod("RequireRuntimeCustody", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var offline = type.GetMethod("ClaimOfflineCustody", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (runtimeFirst)
            {
                runtime.Invoke(options, null);
                Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => offline.Invoke(options, null)).InnerException);
            }
            else
            {
                var lease = (IOfflineProvisioningLease)offline.Invoke(options, null)!; lease.RequireHeld();
                Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => runtime.Invoke(options, null)).InnerException);
                options.Dispose(); Assert.Throws<InvalidOperationException>(() => lease.RequireHeld());
                using var migrationLease = (IDisposable)migration.Invoke(null, [Path.Combine(root, "process.lock")])!;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
