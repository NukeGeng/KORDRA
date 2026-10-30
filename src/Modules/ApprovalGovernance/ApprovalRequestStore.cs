using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ApprovalGovernance;

public sealed class ApprovalRequestConflictException(string message) : InvalidOperationException(message);

/// <summary>Stages owner rows only; caller must hold write admission, verify all authority and owners,
/// enlist AUD and clock floor, save and commit the same transaction. This store grants no authority.</summary>
public sealed class ApprovalRequestStore(ApprovalDbContext db)
{
    public void StageRequest(ApprovalRequestRecord request)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(request);
        if (request.Id == Guid.Empty || request.InstallationId == Guid.Empty || request.TenantId == Guid.Empty ||
            request.ProjectId == Guid.Empty || request.EnvironmentId == Guid.Empty || request.ReleaseRevisionId == Guid.Empty ||
            request.RequesterPrincipalId == Guid.Empty || request.Version != 1 || request.State != "Pending" ||
            request.SpecHash is not { Length: 32 } || request.RequesterEvidence is not { Length: > 0 and <= 65536 } ||
            !Utc(request.CreatedAtUtc) || !Utc(request.PendingExpiresAtUtc) || request.PendingExpiresAtUtc <= request.CreatedAtUtc ||
            request.GrantedAtUtc is not null || request.GrantExpiresAtUtc is not null || request.ApproverPrincipalId is not null || request.ApproverEvidence is not null)
            throw new ArgumentException("Invalid approval request.", nameof(request));
        request.SpecHash = (byte[])request.SpecHash.Clone(); request.RequesterEvidence = (byte[])request.RequesterEvidence.Clone();
        db.Requests.Add(request);
    }

    public async Task StageDecisionAsync(Guid installation, Guid tenant, Guid project, Guid environment, Guid requestId,
        long expectedVersion, Guid decisionId, Guid actorId, ApprovalDecisionAction action, string reason,
        DateTimeOffset now, DateTimeOffset? grantExpiry = null, byte[]? approverEvidence = null, CancellationToken ct = default)
    {
        RequireTransaction();
        if (installation == Guid.Empty || tenant == Guid.Empty || project == Guid.Empty || environment == Guid.Empty || requestId == Guid.Empty ||
            decisionId == Guid.Empty || actorId == Guid.Empty || expectedVersion <= 0 || expectedVersion == long.MaxValue || !Utc(now))
            throw new ArgumentException("Invalid approval decision identity.");
        var row = await db.Requests.SingleOrDefaultAsync(x => x.Id == requestId && x.InstallationId == installation &&
            x.TenantId == tenant && x.ProjectId == project && x.EnvironmentId == environment, ct);
        if (row is null || row.Version != expectedVersion || !Enum.TryParse<ApprovalRecordedState>(row.State, out var state) ||
            !Enum.IsDefined(state) || now < row.CreatedAtUtc) throw new ApprovalRequestConflictException("Approval scope, version or clock mismatch.");
        ApprovalRecordedState next;
        try { next = ApprovalDecisionTransition.Next(state, action, reason); }
        catch (InvalidOperationException) { throw new ApprovalRequestConflictException("Approval recorded state conflicts with this decision."); }
        if (action == ApprovalDecisionAction.Grant)
        {
            if (grantExpiry is null || !Utc(grantExpiry.Value) || grantExpiry <= now || grantExpiry > row.PendingExpiresAtUtc ||
                approverEvidence is not { Length: > 0 and <= 65536 }) throw new ArgumentException("Invalid grant evidence or expiry.");
            row.GrantedAtUtc = now; row.GrantExpiresAtUtc = grantExpiry; row.ApproverPrincipalId = actorId;
            row.ApproverEvidence = (byte[])approverEvidence.Clone();
        }
        else if (grantExpiry is not null || approverEvidence is not null) throw new ArgumentException("Unexpected grant evidence.");
        row.State = next.ToString(); row.Version++;
        db.Decisions.Add(new() { Id = decisionId, InstallationId = installation, TenantId = tenant, RequestId = requestId,
            RequestVersion = row.Version, ActorPrincipalId = actorId, Action = action.ToString().ToLowerInvariant(), ReasonCode = reason, OccurredAtUtc = now });
    }
    private void RequireTransaction()
    { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("A shared owner transaction is required."); }
    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero && value != default && value != DateTimeOffset.MaxValue;
}
