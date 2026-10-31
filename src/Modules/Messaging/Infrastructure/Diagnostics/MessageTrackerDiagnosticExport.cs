using Wolverine.Tracking;

namespace Kordra.Modules.Messaging.Infrastructure.Diagnostics;

/// <summary>Bounded metadata-only output projection; never mutates native records or authorizes exporting a raw tracked session.</summary>
public static class MessageTrackerDiagnosticExport
{
    public sealed record Summary(int ExceptionCount, int RecordCount, Guid[] MessageIds);
    public static Summary Project(ITrackedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var records = session.AllRecordsInOrder();
        var exceptions = session.AllExceptions();
        if (records.Length > 1024 || exceptions.Count > 1024)
            throw new InvalidOperationException("Tracker export exceeds bounded diagnostic profile.");
        // No original message object, exception, service name, body, route, ToString or arbitrary tracing field escapes.
        return new(exceptions.Count, records.Length, records.Select(record => record.Envelope?.Id ?? Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().Take(1024).ToArray());
    }
}
