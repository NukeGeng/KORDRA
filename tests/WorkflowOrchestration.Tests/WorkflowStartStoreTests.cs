using System.Data;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowStartStoreTests
{
    [Fact]
    public void RevisionFourOnlyAddsStartReceipts()
    {
        using var old = WorkflowDbContext.CreateOfflineCancellationModel();
        using var current = WorkflowDbContext.CreateOfflineStartModel();
        var baseline = old.Database.GenerateCreateScript().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var expanded = current.Database.GenerateCreateScript().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Empty(baseline.Except(expanded));
        var added = expanded.Except(baseline).ToArray();
        Assert.Equal(2, added.Length);
        Assert.Contains("CREATE TABLE wfl.start_receipts", added[0]);
        Assert.Contains("REFERENCES wfl.request_receipts", added[0]);
        Assert.Contains("CREATE UNIQUE INDEX", added[1]);
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var bytes = File.ReadAllBytes(Path.Combine(fixture, "wfl-v4-start.sql"));
        Assert.Equal(string.Join("\n\n", added) + "\n", System.Text.Encoding.UTF8.GetString(bytes));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "wfl-v4-start.manifest.json")));
        Assert.Equal(WorkflowDbContext.StartSchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(bytes),
            Convert.FromHexString(manifest.RootElement.GetProperty("sha256").GetString()!));
        Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task RequiresExpandedCallerRootBeforeAllocatingSpecification()
    {
        using var old = WorkflowDbContext.CreateOfflineCancellationModel();
        using var current = WorkflowDbContext.CreateOfflineStartModel();
        var spec = Specification();
        var command = Command(spec);
        foreach (var context in new[] { old, current })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new WorkflowStartStore(context).StageAsync(command, () => throw new Exception("Unexpected allocation."), spec.CreatedAtUtc));
            Assert.Contains("caller root", error.Message);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
        Assert.True(command.Matches(spec));
        Assert.False(command.Matches(Specification()));
    }

    private static WorkflowSpecification Specification()
    {
        var now = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
            WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
    }

    private static WorkflowStartCommand Command(WorkflowSpecification spec) =>
        new(spec.InstallationId, spec.TenantId, Guid.NewGuid(), spec.ProjectId, spec.EnvironmentId, spec.RequestId,
            spec.ReleaseId, spec.ReleaseRevision, spec.ReleaseHash, WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion, spec.DeadlineUtc, "fixture-key");
}
