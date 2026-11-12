using System.Data;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using Xunit;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposableWorkflowMetadataTests
{
    private static readonly Guid Run = Guid.NewGuid();
    private const string Secret = "synthetic-not-for-diagnostics";
    private static Binding Profile() => (Run, new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(10));
    private static string Locator() => $"Host=127.0.0.1;Database=kordra_test_{Run:N};Username=kordra_test;Password={Secret};Pooling=false";
    private static Task<bool> Never(CancellationToken _) => throw new InvalidOperationException("Unexpected authority/evidence callback.");

    [Theory]
    [InlineData("cancel", false)][InlineData("cancel", true)]
    [InlineData("reconcile", false)][InlineData("reconcile", true)]
    public async Task WrongTargetIsRejectedBeforeAnyAuthorityOrEvidenceRead(string operation, bool admitted)
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Pooling=false");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(operation, admitted, connection, Profile()));
        Assert.Contains("Disposable WFL fixture target/profile invalid", error.Message);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("cancel", false)][InlineData("cancel", true)]
    [InlineData("reconcile", false)][InlineData("reconcile", true)]
    public async Task ExpiredBindingDoesNotFallBackOrEnterAuthority(string operation, bool admitted)
    {
        using var connection = new NpgsqlConnection(Locator());
        var profile = Profile(); profile.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(operation, admitted, connection, profile));
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("cancel")][InlineData("reconcile")]
    public async Task ProfileSelectionDoesNotPermitAuditSubstitution(string operation)
    {
        var caseId = Guid.NewGuid(); var environment = Guid.NewGuid();
        var cancel = Cancel(caseId); var reconcile = Reconcile(caseId);
        var audit = operation == "cancel" ? Mutation(cancel.ActorId, cancel.TenantId, cancel.InstanceId, cancel.RequestId,
            environment, operation, cancel.CommandHash) : Mutation(reconcile.ActorId, reconcile.TenantId, reconcile.InstanceId,
            reconcile.RequestId, environment, operation, reconcile.CommandHash);
        audit = audit with { TenantRef = Guid.NewGuid() };
        using var connection = new NpgsqlConnection(Locator()); var gate = new FairAsyncReaderWriterAdmissionGate();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "cancel")
                await WorkflowCancellationTransaction.ExecuteDisposableTestAsync(connection, null!, caseId, cancel,
                    Guid.NewGuid(), environment, audit, gate, Never, Profile());
            else
                await WorkflowReconciliationTransaction.ExecuteDisposableTestAsync(connection, null!, caseId, reconcile,
                    Guid.NewGuid(), environment, audit, gate, null!, Never, Never, Profile());
        });
        Assert.Contains("exact trusted scope and mutation audit", error.Message);
        Assert.Equal(ConnectionState.Closed, connection.State); Assert.False(gate.IsClosed);
    }

    [Fact]
    public async Task ReconciliationRequiresCustodyEvenWithValidProfileAndAudit()
    {
        var caseId = Guid.NewGuid(); var environment = Guid.NewGuid(); var command = Reconcile(caseId);
        using var connection = new NpgsqlConnection(Locator()); var gate = new FairAsyncReaderWriterAdmissionGate();
        var audit = Mutation(command.ActorId, command.TenantId, command.InstanceId, command.RequestId,
            environment, "reconcile", command.CommandHash);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowReconciliationTransaction.ExecuteDisposableTestAsync(connection, null!, caseId, command,
                Guid.NewGuid(), environment, audit, gate, null!, Never, Never, Profile()));
        Assert.Contains("exact installation custody", error.Message);
        Assert.Equal(ConnectionState.Closed, connection.State); Assert.False(gate.IsClosed);
    }

    private static async Task Invoke(string operation, bool admitted, NpgsqlConnection connection, Binding profile)
    {
        // Null dependencies make a target-guard failure before any authority callback observable.
        if (operation == "cancel" && admitted)
            await WorkflowCancellationTransaction.ExecuteAdmittedDisposableTestAsync(connection, null!, Guid.NewGuid(),
                null!, Guid.NewGuid(), Guid.NewGuid(), null!, null!, null!, profile);
        else if (operation == "cancel")
            await WorkflowCancellationTransaction.ExecuteDisposableTestAsync(connection, null!, Guid.NewGuid(), null!,
                Guid.NewGuid(), Guid.NewGuid(), null!, null!, Never, profile);
        else if (admitted)
            await WorkflowReconciliationTransaction.ExecuteAdmittedDisposableTestAsync(connection, null!, Guid.NewGuid(),
                null!, null!, null!, null!, null!, null!, null!, null!, profile);
        else
            await WorkflowReconciliationTransaction.ExecuteDisposableTestAsync(connection, null!, Guid.NewGuid(), null!,
                Guid.NewGuid(), Guid.NewGuid(), null!, null!, null!, Never, Never, profile);
    }
    private static WorkflowCancellationIntent Cancel(Guid installation) => new(installation, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), 4, new string('a', 64));
    private static WorkflowReconciliationCommand Reconcile(Guid installation) => new(installation, Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4,
        WorkflowReconciliationKind.ConfirmKnownRollback, new string('a', 64), new string('b', 64));
    private static AuditIntent Mutation(Guid actor, Guid tenant, Guid instance, Guid request, Guid environment,
        string operation, string hash) => new(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, actor, tenant,
            instance, "workflow." + operation, "environment", environment, 1, 1, "workflow-" + operation + "-" + hash,
            DateTimeOffset.UtcNow, request);
}
