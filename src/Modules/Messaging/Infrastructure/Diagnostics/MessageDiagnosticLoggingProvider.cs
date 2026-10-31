using Microsoft.Extensions.Logging;

namespace Kordra.Modules.Messaging.Infrastructure.Diagnostics;

/// <summary>Explicit dedicated MSG logging boundary. Does not register a host/provider or sanitize tracker/telemetry automatically.</summary>
public sealed class MessageDiagnosticLoggingProvider(ILoggerProvider destination) : ILoggerProvider
{
    private readonly ILoggerProvider _destination = destination ?? throw new ArgumentNullException(nameof(destination));
    public ILogger CreateLogger(string categoryName) => new Boundary(_destination.CreateLogger("Kordra.Messaging.Diagnostics"));
    public void Dispose() { } // Composition owns the downstream provider.
    private sealed class Boundary(ILogger sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => Enum.IsDefined(level) && level != LogLevel.None && sink.IsEnabled(level);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            // Do not call the incoming formatter, arbitrary ToString, exception rendering or scope formatter.
            Guid? messageId = null;
            if (state is IReadOnlyList<KeyValuePair<string, object?>> fields && fields.Count <= 32)
                for (var index = 0; index < fields.Count; index++)
                {
                    var field = fields[index];
                    if (field.Key is "envelope" or "Id" && field.Value is Guid value && value != Guid.Empty)
                    {
                        if (messageId is not null && messageId != value) { messageId = null; break; }
                        messageId = value;
                    }
                }
            var code = id.Id switch { 102 => "ExecutionStarted", 103 => "ExecutionFinished", 104 => "Succeeded", 105 => "Failed", _ => "NativeDiagnostic" };
            var diagnostic = new ClosedDiagnostic(code, messageId, error is not null);
            sink.Log(level, new EventId(4001, "MessagingDiagnostic"), diagnostic, null, static (entry, _) =>
                $"MSG {entry.Code}; messageId={entry.MessageId?.ToString("D") ?? "unavailable"}; failure={entry.HasFailure}");
        }
    }
    public sealed record ClosedDiagnostic(string Code, Guid? MessageId, bool HasFailure);
}
