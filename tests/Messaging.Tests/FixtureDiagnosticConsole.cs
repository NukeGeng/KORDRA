using Microsoft.Extensions.Logging;
namespace Kordra.Messaging.Tests;
/// <summary>Sink used only behind the MSG closed provider in explicitly selected private compatibility runs.</summary>
internal sealed class FixtureDiagnosticConsole : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Sink();
    public void Dispose() { }
    private sealed class Sink : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (error is not null) throw new InvalidOperationException("Closed diagnostic sink received an exception object.");
            Console.WriteLine(formatter(state, null));
        }
    }
}
