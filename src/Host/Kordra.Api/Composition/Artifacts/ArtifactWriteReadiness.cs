using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Exact owner capability checks before new registrations; no migration, repair or permission inference.</summary>
public sealed class ArtifactWriteReadiness(IDbContextFactory<ArtifactDbContext> artifacts,
    IDbContextFactory<CredentialDbContext> credentials)
{
    private readonly DisposableTestBinding? disposableTest;

    private ArtifactWriteReadiness(IDbContextFactory<ArtifactDbContext> artifacts,
        IDbContextFactory<CredentialDbContext> credentials, DisposableTestBinding profile) : this(artifacts, credentials)
        => disposableTest = profile;

    public static ArtifactWriteReadiness CreateDisposableTestReadiness(IDbContextFactory<ArtifactDbContext> artifacts,
        IDbContextFactory<CredentialDbContext> credentials, DisposableTestBinding profile) => new(artifacts, credentials, profile);

    public async Task<bool> CheckUnderAdmissionAsync(CancellationToken ct)
    {
        await using var art = await artifacts.CreateDbContextAsync(ct);
        await using var sec = await credentials.CreateDbContextAsync(ct);
        if (disposableTest is { } profile)
        {
            ArtifactDbContextFactory.ValidateDisposableTestTarget(art.Database.GetDbConnection().ConnectionString, profile);
            CredentialDbContextFactory.ValidateDisposableTestTarget(sec.Database.GetDbConnection().ConnectionString, profile);
        }
        else
        {
            ArtifactDbContextFactory.ValidateEnlistedTarget(art.Database.GetDbConnection());
            ArtifactDbContextFactory.ValidateEnlistedTarget(sec.Database.GetDbConnection());
        }
        return await new ArtifactQueryStore(art).HasWriteSchemaV1Async(ct) &&
            await new RegistryCredentialBindingStore(sec).HasRegistrySchemaV1Async(ct);
    }
}
