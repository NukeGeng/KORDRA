using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowOperatorDecisionStoreTests
{
    [Fact]
    public void RevisionFivePreservesPriorSchemaAndPinsAdditiveDecisionConstraints()
    {
        using var old = WorkflowDbContext.CreateOfflineStartModel();
        using var current = WorkflowDbContext.CreateOfflineReconciliationModel();
        var baseline = old.Database.GenerateCreateScript().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var expanded = current.Database.GenerateCreateScript().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Empty(baseline.Except(expanded));
        var added = expanded.Except(baseline).ToArray();
        Assert.Equal(2, added.Length);
        Assert.Contains("CREATE TABLE wfl.operator_decisions", added[0]);
        Assert.Contains("REFERENCES wfl.instances", added[0]);
        Assert.Contains("kind IN (1,2)", added[0]);
        Assert.Contains("resulting_version = expected_version + 1", added[0]);
        Assert.Contains("PRIMARY KEY (installation_id, tenant_id, request_id)", added[0]);
        Assert.Contains("CREATE UNIQUE INDEX", added[1]);
        Assert.Contains("(installation_id, tenant_id, instance_id, operation_id, kind)", added[1]);
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var bytes = File.ReadAllBytes(Path.Combine(directory, "wfl-v5-operator-decisions.sql"));
        Assert.Equal(string.Join("\n\n", added) + "\n", Encoding.UTF8.GetString(bytes));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "wfl-v5-operator-decisions.manifest.json")));
        Assert.Equal(WorkflowDbContext.ReconciliationSchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(SHA256.HashData(bytes), Convert.FromHexString(manifest.RootElement.GetProperty("sha256").GetString()!));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "wfl-v4-start.sql"))),
            Convert.FromHexString(manifest.RootElement.GetProperty("baselineSha256").GetString()!));
        Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task MissingCallerRootNeverOpensDatabaseOrEvaluatesEvidence()
    {
        using var old = WorkflowDbContext.CreateOfflineStartModel();
        using var current = WorkflowDbContext.CreateOfflineReconciliationModel();
        var command = Command();
        foreach (var context in new[] { old, current })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new WorkflowOperatorDecisionStore(context).StageAsync(command, Guid.NewGuid(), Guid.NewGuid(),
                    _ => throw new Exception("Evidence must not be evaluated."), DateTimeOffset.UtcNow));
            Assert.Contains("caller root", error.Message);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.Empty(context.ChangeTracker.Entries());
        }
    }

    [Fact]
    public async Task CancellationNeverStartsOwnerWork()
    {
        using var context = WorkflowDbContext.CreateOfflineReconciliationModel();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WorkflowOperatorDecisionStore(context).StageAsync(Command(), Guid.NewGuid(), Guid.NewGuid(),
                _ => throw new Exception("Unexpected evidence probe."), DateTimeOffset.UtcNow, cancellation.Token));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static WorkflowReconciliationCommand Command() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, WorkflowReconciliationKind.ConfirmKnownRollback,
        new string('a', 64), new string('b', 64));
}
