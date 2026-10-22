using System.Data.Common;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using Kordra.Api;
using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class LocalAccountAdministrationTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("untrusted")]
    [InlineData("client-only")]
    [InlineData("no-eku")]
    [InlineData("cn-only")]
    [InlineData("wrong-san")]
    [InlineData("expired")]
    [InlineData("no-private-key")]
    public void Installed_tls_enforces_trust_server_usage_and_actual_localhost_SAN(string scenario)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Kordra synthetic test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        var now = DateTimeOffset.UtcNow;
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-2), now.AddDays(2));
        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        if (scenario != "cn-only")
        {
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(scenario == "wrong-san" ? "foreign.example" : "localhost");
            request.CertificateExtensions.Add(san.Build());
        }
        if (scenario != "no-eku")
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new(scenario == "client-only" ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        using var publicLeaf = request.Create(root, now.AddDays(-1), scenario == "expired" ? now.AddMinutes(-1) : now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var leaf = publicLeaf.CopyWithPrivateKey(leafKey);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        if (scenario != "untrusted") chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        var method = typeof(LocalPilotHostOptions).GetMethod("ValidateInstalledCertificate", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (scenario == "valid") method.Invoke(null, [leaf, chain]);
        else
        {
            var failure = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [scenario == "no-private-key" ? publicLeaf : leaf, chain]));
            Assert.IsType<InvalidOperationException>(failure.InnerException);
        }
        // The real startup path must reject this same synthetic root under native OS trust.
        var machineFailure = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [leaf, null]));
        Assert.IsType<InvalidOperationException>(machineFailure.InnerException);
    }

    [Theory]
    [InlineData("operator_1", "OPERATOR_1")]
    [InlineData("A.B-c", "A.B-C")]
    [InlineData("abc", "ABC")]
    [InlineData("a1234567890123456789012345678901234567890123456789012345678901", "A1234567890123456789012345678901234567890123456789012345678901")]
    public void Username_policy_accepts_only_ascii_identifier_characters_and_normalizes_case(string input, string expected)
    {
        Assert.True(LocalAuthenticationService.TryNormalizeUserName(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("đangvy")]
    [InlineData("name@host")]
    [InlineData("name/")]
    public void Username_policy_rejects_short_long_non_ascii_or_unlisted_characters(string input) =>
        Assert.False(LocalAuthenticationService.TryNormalizeUserName(input, out _));

    [Fact]
    public void Username_policy_rejects_identifiers_over_64_ascii_characters() =>
        Assert.False(LocalAuthenticationService.TryNormalizeUserName(new string('a', 65), out _));

    [Fact]
    public void Password_policy_honors_scalar_length_and_exact_pinned_denylist_entries()
    {
        using var artifact = CreatePolicy(["Known-Common-Password-123"]);

        Assert.True(artifact.Policy.IsAcceptable(new string('a', 15)));
        Assert.False(artifact.Policy.IsAcceptable(new string('a', 14)));
        Assert.True(artifact.Policy.IsAcceptable(string.Concat(Enumerable.Repeat("😀", 128))));
        Assert.False(artifact.Policy.IsAcceptable(string.Concat(Enumerable.Repeat("😀", 129))));
        Assert.False(artifact.Policy.IsAcceptable("Known-Common-Password-123"));
        Assert.True(artifact.Policy.IsAcceptable("known-common-password-123"));
        Assert.False(artifact.Policy.IsAcceptable("bad\ud800password-long"));
        Assert.Equal(artifact.ExpectedHash, artifact.Policy.Sha256);
    }

    [Fact]
    public void Password_policy_rejects_tampered_or_empty_artifacts_without_fallback()
    {
        using var artifact = CreatePolicy(["Synthetic-Common-Password"]);
        File.WriteAllText(artifact.Path, "[]", Encoding.UTF8);

        Assert.Throws<InvalidOperationException>(() => LocalPasswordPolicy.Load(
            artifact.Path, "fixture-v1", "synthetic test fixture", artifact.ExpectedHash));
    }

    [Fact]
    public async Task Offline_enrollment_rejects_bad_password_and_identifier_before_opening_storage()
    {
        using var artifact = CreatePolicy(["Synthetic-Common-Password"]);
        var service = CreateService(artifact.Policy);

        var shortPassword = await service.EnrollOfflineAsync("valid-user", "short");
        var deniedPassword = await service.EnrollOfflineAsync("valid-user", "Synthetic-Common-Password");
        var invalidUserName = await service.EnrollOfflineAsync("invalid/name", new string('x', 16));

        Assert.Equal(LocalAuthenticationStatus.InvalidRequest, shortPassword.Status);
        Assert.Equal(LocalAuthenticationStatus.InvalidRequest, deniedPassword.Status);
        Assert.Equal(LocalAuthenticationStatus.InvalidRequest, invalidUserName.Status);
    }

    [Fact]
    public void Idempotency_keys_are_bounded_ascii_and_never_accept_password_like_free_text()
    {
        Assert.True(LocalAuthenticationService.TryHashIdempotencyKey("request-key-0123456789", out var hash));
        Assert.Equal(32, hash.Length);
        CryptographicOperations.ZeroMemory(hash);

        Assert.False(LocalAuthenticationService.TryHashIdempotencyKey("short", out _));
        Assert.False(LocalAuthenticationService.TryHashIdempotencyKey("request key with spaces", out _));
        Assert.False(LocalAuthenticationService.TryHashIdempotencyKey(new string('a', 129), out _));
    }

    private static LocalAuthenticationService CreateService(LocalPasswordPolicy policy)
    {
        var config = new LocalAuthenticationConfiguration(Guid.NewGuid(), Guid.NewGuid());
        return new LocalAuthenticationService(
            new UnavailableContextFactory(),
            new UnavailableAuditFactory(),
            new PasswordHasher<KordraIdentityUser>(),
            config,
            new FairAsyncReaderWriterAdmissionGate(),
            passwordPolicy: policy);
    }

    private static PolicyArtifact CreatePolicy(string[] entries)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kordra-password-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var path = Path.Combine(directory, "denylist.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entries);
        File.WriteAllBytes(path, bytes);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new PolicyArtifact(directory, path, hash,
            LocalPasswordPolicy.Load(path, "fixture-v1", "synthetic test fixture", hash));
    }

    private sealed class PolicyArtifact(string directory, string path, string expectedHash, LocalPasswordPolicy policy) : IDisposable
    {
        public string Path { get; } = path;
        public string ExpectedHash { get; } = expectedHash;
        public LocalPasswordPolicy Policy { get; } = policy;

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    private sealed class UnavailableContextFactory : IDbContextFactory<KordraIdentityDbContext>
    {
        public KordraIdentityDbContext CreateDbContext() => throw new InvalidOperationException("Storage must not be opened for invalid input.");
        public Task<KordraIdentityDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<KordraIdentityDbContext>(new InvalidOperationException("Storage must not be opened for invalid input."));
    }

    private sealed class UnavailableAuditFactory : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IIdentityAuditTransaction>(new InvalidOperationException("Audit must not be opened for invalid input."));
    }
}
