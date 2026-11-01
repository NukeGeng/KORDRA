using System.Data;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowReceiptInspectionTests
{
    [Fact]
    public async Task ClosedReaderIsUnavailableWithoutOpeningOrGrantingReplay()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        await using var inspector = WorkflowReceiptInspector.CreateIsolatedTestInspector(connection, Guid.NewGuid());
        var query = new WorkflowReceiptExpectation(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), new string('a', 64), 1);
        Assert.Equal(WorkflowReceiptInspectionStatus.Unavailable, await inspector.InspectAsync(query));
        Assert.Equal(ConnectionState.Closed, connection.State);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectAsync(query, new(true)));
        Assert.DoesNotContain(query.SpecificationHash, query.ToString());
    }

    [Fact]
    public void ReceiptKindRequiresExactClosedBinding()
    {
        var identity = new WorkflowIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); var request = Guid.NewGuid(); var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => new WorkflowReceiptExpectation(identity, request, hash, 2));
        Assert.Throws<ArgumentException>(() => new WorkflowReceiptExpectation(identity, request, hash, 1, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new WorkflowReceiptExpectation(identity, request, hash, 2, Guid.NewGuid(), null));
        Assert.Throws<ArgumentException>(() => new WorkflowReceiptExpectation(identity, Guid.Empty, hash, 1));
        Assert.Throws<ArgumentException>(() => new WorkflowReceiptExpectation(identity, request, new string('A', 64), 1));
    }
}
