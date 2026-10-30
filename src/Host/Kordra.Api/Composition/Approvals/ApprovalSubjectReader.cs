using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Current subject candidate only; it grants neither approval nor execution authority.</summary>
public sealed record ApprovalSubjectResult(string Status, ReleaseRevisionSnapshot? Revision = null, ReleaseApprovalPolicy? Policy = null);

/// <summary>For request/grant only. Terminal/history operations must not depend on an old permitting policy.</summary>
public sealed class ApprovalSubjectReader(IDbContextFactory<ReleaseDbContext> releases, ReleaseSelectionReader selections)
{
    public async Task<ApprovalSubjectResult> ReadEligibleUnderAdmissionAsync(Guid installation, Guid tenant, Guid project,
        Guid environment, Guid revisionId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (installation == Guid.Empty || tenant == Guid.Empty || project == Guid.Empty || environment == Guid.Empty ||
            revisionId == Guid.Empty || now == default || now == DateTimeOffset.MaxValue || now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid approval subject scope.");
        await using var db = await releases.CreateDbContextAsync(ct);
        var revision = await new ReleaseQueryStore(db).ReadRevisionUnderAdmissionAsync(installation, tenant, project, revisionId, ct);
        if (revision is null || revision.EnvironmentId != environment) return new("absent");
        if (revision.State != "Frozen") return new("not_frozen");
        var selection = ReleaseSelection.Create(revision.ApplicationId, revision.EnvironmentId, revision.TargetId, revision.ArtifactId);
        var current = await selections.ReadUnderAdmissionAsync(installation, tenant, project, selection, now, ct);
        if (current.Status != "eligible") return new(current.Status);
        if (current.Intent is null || current.Policy is null) throw new InvalidDataException("Eligible approval subject evidence is incomplete.");
        if (current.Intent.SpecHash != revision.SpecHash) return new("subject_changed");
        return new("eligible", revision, current.Policy);
    }
}
