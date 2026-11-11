using System.Data.Common;
using Kordra.Testing;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Npgsql;

namespace Kordra.Messaging.Tests;

/// <summary>Test-only target selection. Actual audit/receipt/journal/inspection and native lifecycle logic stay unchanged.</summary>
internal static class MessagingFixtureSelection
{
    private static DisposablePostgreSqlProfile? Profile(DbConnection connection)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        profile?.ValidateLocator(connection.ConnectionString, DateTimeOffset.UtcNow);
        return profile;
    }
    internal static void ValidateTestTarget(string locator)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is { } p) p.ValidateLocator(locator, DateTimeOffset.UtcNow);
        else AuditDbContextFactory.ValidateTestTarget(locator);
    }
    internal static AuditDbContext AuditContext(DbConnection connection, Guid caseId) => Profile(connection) is { } p
        ? AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, caseId, p.OwnerBinding)
        : AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
    internal static Task<AuditTransactionContext> AuditTransaction(DbConnection connection, DbTransaction root, Guid caseId,
        CancellationToken ct = default) => Profile(connection) is { } p
        ? AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, p.OwnerBinding, ct)
        : AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
    internal static CallerOwnedReceiptStore ReceiptStore(NpgsqlConnection connection, DbTransaction root, Guid caseId) => Profile(connection) is { } p
        ? CallerOwnedReceiptStore.CreateDisposableTestStore(connection, root, caseId, p.OwnerBinding)
        : CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, root, caseId);
    internal static CallerOwnedMessageJournal Journal(NpgsqlConnection connection, DbTransaction root, Guid caseId) => Profile(connection) is { } p
        ? CallerOwnedMessageJournal.CreateDisposableTestJournal(connection, root, caseId, p.OwnerBinding)
        : CallerOwnedMessageJournal.CreateIsolatedTestJournal(connection, root, caseId);
    internal static PublicationCapacityGuard Capacity(NpgsqlConnection connection, DbTransaction root, Guid caseId) => Profile(connection) is { } p
        ? PublicationCapacityGuard.CreateDisposableTestGuard(connection, root, caseId, p.OwnerBinding)
        : PublicationCapacityGuard.CreateIsolatedTestGuard(connection, root, caseId);
    internal static MessageReceiptInspector MessageReceipt(DbConnection connection, Guid caseId) => Profile(connection) is { } p
        ? MessageReceiptInspector.CreateDisposableTestInspector(connection, caseId, p.OwnerBinding)
        : MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
}
