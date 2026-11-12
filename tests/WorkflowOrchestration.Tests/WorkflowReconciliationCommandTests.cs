using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowReconciliationCommandTests
{
    [Fact]
    public void CommandIdentityPinsActorOutcomeSelectionVersionAndEvidence()
    {
        var installation = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Guid.NewGuid();
        var request = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        WorkflowReconciliationCommand Make(Guid? who = null, long version = 5,
            WorkflowReconciliationKind kind = WorkflowReconciliationKind.ConfirmKnownRollback, string? evidence = null) =>
            new(installation, tenant, instance, request, who ?? actor, operation, version, kind,
                evidence ?? new string('a', 64), new string('b', 64));
        var original = Make();
        Assert.Equal(original.CommandHash, Make().CommandHash);
        Assert.NotEqual(original.CommandHash, Make(who: Guid.NewGuid()).CommandHash);
        Assert.NotEqual(original.CommandHash, Make(version: 6).CommandHash);
        Assert.NotEqual(original.CommandHash, Make(kind: WorkflowReconciliationKind.BindMatchingCommittedReceipt).CommandHash);
        Assert.NotEqual(original.CommandHash, Make(evidence: new string('c', 64)).CommandHash);
        Assert.Throws<ArgumentException>(() => Make(version: long.MaxValue));
        Assert.Throws<ArgumentException>(() => Make(kind: (WorkflowReconciliationKind)3));
        Assert.Throws<ArgumentException>(() => Make(evidence: "untrusted URL"));
    }
}
