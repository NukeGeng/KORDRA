using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowReconciliationTransactionTests
{
    [Theory]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("resource")]
    [InlineData("environment")]
    [InlineData("request")]
    [InlineData("hash")]
    [InlineData("stage")]
    [InlineData("permission")]
    [InlineData("policy")]
    [InlineData("authority")]
    [InlineData("time")]
    public async Task AuditSubstitutionFailsBeforeCustodyOrDatabaseAccess(string changed)
    {
        var caseId = Guid.NewGuid();
        var environment = Guid.NewGuid();
        var command = Command(caseId);
        var mutation = Mutation(command, environment);
        mutation = changed switch
        {
            "actor" => mutation with { SubjectRef = Guid.NewGuid() },
            "tenant" => mutation with { TenantRef = Guid.NewGuid() },
            "resource" => mutation with { ResourceRef = Guid.NewGuid() },
            "environment" => mutation with { ScopeId = Guid.NewGuid() },
            "request" => mutation with { CorrelationId = Guid.NewGuid() },
            "hash" => mutation with { SafeReason = "workflow-reconcile-" + new string('c', 64) },
            "stage" => mutation with { Stage = AuditIntentStage.Use },
            "permission" => mutation with { Permission = "workflow.cancel" },
            "policy" => mutation with { PolicyRevision = null },
            "authority" => mutation with { AuthorityRevision = 0 },
            "time" => mutation with { OccurredAtUtc = DateTimeOffset.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        await using var connection = Connection();
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowReconciliationTransaction.ExecuteIsolatedAsync(connection, null!, caseId, command,
                Guid.NewGuid(), environment, mutation, gate, null!, Never, Never));
        Assert.Contains("exact trusted scope and mutation audit", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(gate.IsClosed);
    }

    [Fact]
    public async Task ExactAuditStillRequiresCustodyBeforeOpeningDatabase()
    {
        var caseId = Guid.NewGuid();
        var command = Command(caseId);
        var environment = Guid.NewGuid();
        await using var connection = Connection();
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowReconciliationTransaction.ExecuteIsolatedAsync(connection, null!, caseId, command,
                Guid.NewGuid(), environment, Mutation(command, environment), gate, null!, Never, Never));
        Assert.Contains("exact installation custody", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(gate.IsClosed);
    }

    private static NpgsqlConnection Connection() => new("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
    private static Task<bool> Never(CancellationToken _) => throw new InvalidOperationException("Unexpected callback.");
    private static WorkflowReconciliationCommand Command(Guid installation) => new(installation, Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4,
        WorkflowReconciliationKind.ConfirmKnownRollback, new string('a', 64), new string('b', 64));
    private static AuditIntent Mutation(WorkflowReconciliationCommand command, Guid environment) =>
        new(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, command.ActorId,
            command.TenantId, command.InstanceId, "workflow.reconcile", "environment", environment,
            1, 1, "workflow-reconcile-" + command.CommandHash, DateTimeOffset.UtcNow, command.RequestId);
}
