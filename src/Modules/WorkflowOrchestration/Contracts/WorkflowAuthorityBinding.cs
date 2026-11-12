namespace Kordra.Modules.WorkflowOrchestration.Contracts;

/// <summary>
/// IAM-issued delegation reference recorded as WFL authority history (12.2f). A locator, never a bearer secret,
/// grant or permit; current authority is always re-evaluated by its owner.
/// </summary>
public sealed record WorkflowDelegationReference(Guid DelegationId, DateTimeOffset ValidUntilUtc);

/// <summary>Staged authority-binding receipt. Duplicate means the original request result is returned.</summary>
public sealed record WorkflowAuthorityBindingResult(Guid BindingId, long Generation, long ResultingVersion, bool Duplicate);
