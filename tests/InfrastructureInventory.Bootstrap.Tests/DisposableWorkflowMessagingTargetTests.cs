using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposableWorkflowMessagingTargetTests
{
    private static readonly Guid Run = Guid.NewGuid();
    private static Binding Profile() => (Run, new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(10));
    private static string Locator() => $"Host=127.0.0.1;Database=kordra_test_{Run:N};Username=kordra_test;Password=synthetic-do-not-echo;Pooling=false";

    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public void ExactWflCaseRevisionAndMessageInspectorNeedNoConnectionOpen(int revision)
    {
        using var connection = new NpgsqlConnection(Locator()); var caseId = Guid.NewGuid(); var profile = Profile();
        var target = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, profile);
        using var context = WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, revision, profile);
        Assert.Equal("t12_" + caseId.ToString("N"), target.SchemaName); Assert.Equal(target.SchemaName, context.Model.GetDefaultSchema());
        Assert.Same(connection, context.Database.GetDbConnection());
        Assert.Equal(revision >= 2, context.IncludesWakeOperations);
        Assert.Equal(revision >= 3, context.IncludesCancellationIntents);
        Assert.Equal(revision >= 4, context.IncludesStartReceipts);
        Assert.Equal(revision >= 5, context.IncludesOperatorReconciliation);
        Assert.NotNull(MessageReceiptInspector.CreateDisposableTestInspector(connection, caseId, profile));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Throws<InvalidOperationException>(() => WorkflowFixtureTarget.Validate(connection, caseId));
        Assert.Throws<InvalidOperationException>(() => MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId));
    }

    [Theory]
    [InlineData("Database=kordra_test")][InlineData("Database=kordra")][InlineData("Username=kordra_app")]
    [InlineData("Username=postgres")][InlineData("Host=localhost")][InlineData("Port=5433")]
    [InlineData("Options=-c search_path=app")][InlineData("Passfile=/tmp/synthetic-do-not-echo")]
    public void BothOwnersRejectWrongOrUnboundTargetsBeforeDatabaseAccess(string change)
    {
        var builder = new NpgsqlConnectionStringBuilder(Locator()); var pair = change.Split('=', 2); builder[pair[0]] = pair[1];
        using var connection = new NpgsqlConnection(builder.ConnectionString); var caseId = Guid.NewGuid(); var profile = Profile();
        Assert.Throws<InvalidOperationException>(() => WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, profile));
        var error = Assert.Throws<InvalidOperationException>(() => MessageReceiptInspector.CreateDisposableTestInspector(connection, caseId, profile));
        Assert.DoesNotContain("synthetic-do-not-echo", error.ToString());
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void EmptyCasesExpiredBindingsAndUnknownRevisionsDeny()
    {
        using var connection = new NpgsqlConnection(Locator()); var profile = Profile(); var caseId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => WorkflowFixtureTarget.ValidateDisposableTest(connection, Guid.Empty, profile));
        Assert.Throws<InvalidOperationException>(() => MessageReceiptInspector.CreateDisposableTestInspector(connection, Guid.Empty, profile));
        Assert.Throws<InvalidOperationException>(() => WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 0, profile));
        Assert.Throws<InvalidOperationException>(() => WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 6, profile));
        profile.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Throws<InvalidOperationException>(() => WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, profile));
        Assert.Throws<InvalidOperationException>(() => MessageReceiptInspector.CreateDisposableTestInspector(connection, caseId, profile));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }
}
