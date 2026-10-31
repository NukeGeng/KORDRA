using Kordra.Modules.Messaging.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class MessageDiagnosticLoggingProviderTests
{
    [Fact]
    public void KeepsKnownIdentitySeverityAndFailureWithoutRenderingInputOrForwardingExceptionScopeOrCategory()
    {
        var output = new Sink(); var candidate = new MessageDiagnosticLoggingProvider(output);
        var logger = candidate.CreateLogger("synthetic-private-category");
        using var scope = logger.BeginScope(new NeverRender());
        var id = Guid.NewGuid();
        var fields = new List<KeyValuePair<string, object?>> { new("envelope", id), new("Message", new NeverRender()), new("token", "synthetic-private-token") };
        logger.Log(LogLevel.Error, new EventId(105, "synthetic-private-event"), fields,
            new InvalidOperationException("synthetic-private-exception", new Exception("synthetic-private-inner")),
            static (_, _) => throw new InvalidOperationException("Incoming formatter must not run."));
        var entry = Assert.Single(output.Entries);
        Assert.Equal(LogLevel.Error, entry.Level); Assert.Equal("Kordra.Messaging.Diagnostics", output.Category);
        Assert.Equal(new EventId(4001, "MessagingDiagnostic"), entry.Event);
        Assert.Equal(id, entry.Diagnostic.MessageId); Assert.Equal("Failed", entry.Diagnostic.Code); Assert.True(entry.Diagnostic.HasFailure);
        Assert.Null(entry.Exception); Assert.DoesNotContain("synthetic-private", entry.Text);
        Assert.Equal(0, output.ScopeCalls);
    }

    [Fact]
    public void ConflictingIdsUnknownEventsAndOpaqueStateDoNotBecomeFreeTextOrFalseCorrelation()
    {
        var output = new Sink(); var logger = new MessageDiagnosticLoggingProvider(output).CreateLogger("unused");
        logger.Log(LogLevel.Warning, new EventId(987654, "private"),
            new List<KeyValuePair<string, object?>> { new("Id", Guid.NewGuid()), new("envelope", Guid.NewGuid()) }, null,
            static (_, _) => throw new InvalidOperationException());
        logger.Log(LogLevel.Error, new EventId(0), new NeverRender(), new Exception("private"),
            static (_, _) => throw new InvalidOperationException());
        Assert.Equal(2, output.Entries.Count);
        Assert.All(output.Entries, entry => { Assert.Null(entry.Diagnostic.MessageId); Assert.Equal("NativeDiagnostic", entry.Diagnostic.Code); Assert.Null(entry.Exception); });
    }

    private sealed class NeverRender { public override string ToString() => throw new InvalidOperationException("Arbitrary rendering forbidden."); }
    private sealed class Sink : ILoggerProvider, ILogger
    {
        public string? Category { get; private set; }
        public int ScopeCalls { get; private set; }
        public List<(LogLevel Level, EventId Event, MessageDiagnosticLoggingProvider.ClosedDiagnostic Diagnostic, Exception? Exception, string Text)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) { Category = categoryName; return this; }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull { ScopeCalls++; throw new InvalidOperationException("Scope must not be forwarded."); }
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) =>
            Entries.Add((level, id, Assert.IsType<MessageDiagnosticLoggingProvider.ClosedDiagnostic>(state), error, formatter(state, error)));
    }
}
