namespace Kordra.ApprovalGovernance.Domain;

/// <summary>Calculates exclusive original deadlines; it does not prove grants, owner facts or authorize actions.</summary>
public static class ApprovalDeadlines
{
    public static DateTimeOffset Pending(DateTimeOffset nowUtc, ReleaseApprovalPolicy policy, string classification,
        DateTimeOffset? requestAssignmentExpiry, DateTimeOffset? readAssignmentExpiry)
    {
        var rule = Applicable(policy, classification, nowUtc);
        var result = nowUtc.AddSeconds(rule.PendingLifetimeSeconds);
        result = Cap(result, policy.ExpiresAtUtc, nowUtc);
        result = Cap(result, requestAssignmentExpiry, nowUtc);
        return Cap(result, readAssignmentExpiry, nowUtc);
    }

    public static DateTimeOffset Grant(DateTimeOffset nowUtc, ReleaseApprovalPolicy policy, string classification,
        Guid requester, Guid approver, DateTimeOffset originalPendingExpiry,
        DateTimeOffset? requesterRequestExpiry, DateTimeOffset? requesterReadExpiry,
        DateTimeOffset? approverApproveExpiry, DateTimeOffset? approverReadExpiry)
    {
        var rule = Applicable(policy, classification, nowUtc);
        if (requester == Guid.Empty || approver == Guid.Empty ||
            (rule.SeparationOfDuties == "independent" && requester == approver))
            throw new InvalidOperationException("Approval separation of duties is not satisfied.");
        if (rule.BlockedWindowsUtc.Any(window => window.StartsAtUtc <= nowUtc && nowUtc < window.EndsAtUtc))
            throw new InvalidOperationException("Approval is blocked in the current window.");
        var result = Cap(nowUtc.AddSeconds(rule.GrantLifetimeSeconds), originalPendingExpiry, nowUtc);
        result = Cap(result, policy.ExpiresAtUtc, nowUtc);
        result = Cap(result, requesterRequestExpiry, nowUtc);
        result = Cap(result, requesterReadExpiry, nowUtc);
        result = Cap(result, approverApproveExpiry, nowUtc);
        result = Cap(result, approverReadExpiry, nowUtc);
        foreach (var window in rule.BlockedWindowsUtc)
            if (window.StartsAtUtc > nowUtc && window.StartsAtUtc < result) result = window.StartsAtUtc;
        return result;
    }

    private static ReleaseApprovalRule Applicable(ReleaseApprovalPolicy policy, string classification, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (nowUtc.Offset != TimeSpan.Zero || classification is not ("Development" or "Test"))
            throw new InvalidOperationException("Approval qualification is unavailable.");
        return policy.FindApplicableAllowRule(classification, nowUtc)
            ?? throw new InvalidOperationException("An active allowing policy is required.");
    }
    private static DateTimeOffset Cap(DateTimeOffset deadline, DateTimeOffset? cap, DateTimeOffset nowUtc)
    {
        if (cap is null) return deadline;
        if (cap.Value.Offset != TimeSpan.Zero || cap.Value <= nowUtc)
            throw new InvalidOperationException("Approval authority is expired or has an invalid deadline.");
        return cap.Value < deadline ? cap.Value : deadline;
    }
}
