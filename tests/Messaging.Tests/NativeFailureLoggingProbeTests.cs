using Kordra.Modules.Messaging.Infrastructure.Diagnostics;
using System.Collections.Concurrent;
using System.Diagnostics;
using Wolverine.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.ErrorHandling;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed record SyntheticLoggingProbe(string SyntheticValue);
public sealed class SyntheticLoggingProbeHandler
{
    public static void Handle(SyntheticLoggingProbe message) => throw new InvalidOperationException(
        "Synthetic outer failure", new ArgumentException(message.SyntheticValue));
}

/// <summary>Characterizes the unprotected native default, not a passing confidentiality acceptance gate.</summary>
public sealed class NativeFailureLoggingProbeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeInvocationContrastsUnprotectedAndClosedLoggingBoundary(bool protect)
    {
        var sentinel = "synthetic-msg-log-" + Guid.NewGuid().ToString("N");
        var logs = new MemoryLogs();
        using var host = Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders().AddProvider(protect ? new MessageDiagnosticLoggingProvider(logs) : logs))
            .UseWolverine(options =>
            {
                options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType<SyntheticLoggingProbeHandler>();
            }).Build();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await host.StartAsync(budget.Token);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Services.GetRequiredService<IMessageBus>()
                .InvokeAsync(new SyntheticLoggingProbe(sentinel), budget.Token));
            Assert.Equal(sentinel, Assert.IsType<ArgumentException>(failure.InnerException).Message);
            Assert.NotEmpty(logs.Lines);
            Assert.Equal(!protect, logs.Lines.Any(line => line.Contains(sentinel, StringComparison.Ordinal)));
            if (protect) Assert.Contains(logs.Lines, line => line.Contains("failure=True", StringComparison.Ordinal));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task ProtectedLoggerDoesNotSanitizeExplicitNativeTrackerExceptionCapture()
    {
        var sentinel = "synthetic-tracker-token-" + Guid.NewGuid().ToString("N");
        var logs = new MemoryLogs();
        using var host = ProbeHost(logs);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await host.StartAsync(budget.Token);
            var session = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(5))
                .DoNotAssertOnExceptionsDetected().ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async context =>
                {
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        context.InvokeAsync(new SyntheticLoggingProbe(sentinel), budget.Token));
                    Assert.Equal(sentinel, Assert.IsType<ArgumentException>(error.InnerException).Message);
                    // Public native tracker capture is a separate boundary from ILogger. Inline
                    // ExecutionFinished records metrics, not AllExceptions; exercise LogException explicitly.
                    host.Services.GetRequiredService<Wolverine.Runtime.IWolverineRuntime>().MessageTracking.LogException(error);
                }));
            Assert.Contains(session.AllExceptions(), error => error.ToString().Contains(sentinel, StringComparison.Ordinal));
            Assert.DoesNotContain(logs.Lines, line => line.Contains(sentinel, StringComparison.Ordinal));
            var projection = MessageTrackerDiagnosticExport.Project(session);
            Assert.Equal(1, projection.ExceptionCount); Assert.True(projection.RecordCount > 0);
            Assert.NotEmpty(projection.MessageIds);
            Assert.DoesNotContain(sentinel, System.Text.Json.JsonSerializer.Serialize(projection));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task FullySampledNativeFailureSpanIsObservedWithoutSyntheticInnerExceptionText()
    {
        var sentinel = "synthetic-telemetry-token-" + Guid.NewGuid().ToString("N");
        var observations = new ConcurrentQueue<(ActivityStatusCode Status, string Text)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Wolverine",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observations.Enqueue((activity.Status,
                activity.DisplayName + activity.StatusDescription + activity.TraceStateString + string.Join("|", activity.Baggage.Select(item => item.Key + "=" + item.Value)) + string.Join("|", activity.TagObjects.Select(tag => tag.Key + "=" + SafeValue(tag.Value))) +
                string.Join("|", activity.Events.Select(e => e.Name + string.Join("|", e.Tags.Select(tag => tag.Key + "=" + SafeValue(tag.Value)))))))
        };
        ActivitySource.AddActivityListener(listener);
        var logs = new MemoryLogs(); using var host = ProbeHost(logs);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await host.StartAsync(budget.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.Services.GetRequiredService<IMessageBus>()
                .InvokeAsync(new SyntheticLoggingProbe(sentinel), budget.Token));
            Assert.Contains(observations, observation => observation.Status == ActivityStatusCode.Error);
            Assert.DoesNotContain(observations, observation => observation.Text.Contains(sentinel, StringComparison.Ordinal));
        }
        finally { await host.StopAsync(CancellationToken.None); }
        static string SafeValue(object? value) => value switch
        {
            null => "", string text => text,
            Guid id => id.ToString("D"),
            bool or byte or short or int or long or float or double or decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            string[] values when values.Length <= 32 => string.Join("|", values),
            _ => throw new InvalidOperationException("Unexamined native telemetry value type; do not claim coverage.")
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InMemoryQueuedRetryKeepsTwoAttemptsAndSuccessWhileProtectingOutput(bool protect)
    {
        var sentinel = "synthetic-queued-token-" + Guid.NewGuid().ToString("N");
        var state = new SyntheticQueuedLoggingState(); var logs = new MemoryLogs();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton(state))
            .ConfigureLogging(logging => logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(protect ? new MessageDiagnosticLoggingProvider(logs) : logs))
            .UseWolverine(options =>
            {
                options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
                options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<SyntheticQueuedLoggingHandler>();
                options.PublishMessage<SyntheticQueuedLoggingProbe>().ToLocalQueue("synthetic_logging_probe");
                options.LocalQueue("synthetic_logging_probe").Sequential();
                options.OnException<SyntheticLoggingTransientFailure>().RetryTimes(1);
            }).Build();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var id = Guid.NewGuid();
        try
        {
            await host.StartAsync(budget.Token);
            await host.Services.GetRequiredService<IMessageBus>().SendAsync(new SyntheticQueuedLoggingProbe(id, sentinel));
            Assert.Equal(id, await state.Completed.Task.WaitAsync(budget.Token));
            await Task.Delay(100, budget.Token);
            Assert.Equal(2, state.Attempts[id]);
            Assert.Equal(!protect, logs.Lines.Any(line => line.Contains(sentinel, StringComparison.Ordinal)));
            if (protect)
            {
                Assert.Contains(logs.Lines, line => line.Contains("failure=True", StringComparison.Ordinal));
                Assert.Contains(logs.Lines, line => line.Contains("MSG Succeeded", StringComparison.Ordinal) && !line.Contains("messageId=unavailable", StringComparison.Ordinal));
            }
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static IHost ProbeHost(MemoryLogs logs) => Host.CreateDefaultBuilder()
        .ConfigureLogging(logging => logging.ClearProviders().AddProvider(new MessageDiagnosticLoggingProvider(logs)))
        .UseWolverine(options =>
        {
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            options.InvokeTracing = InvokeTracingMode.Full;
            options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<SyntheticLoggingProbeHandler>();
        }).Build();

    private sealed class MemoryLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(MemoryLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
            {
                if (owner.Lines.Count > 2000) throw new InvalidOperationException("Synthetic logging budget exceeded.");
                owner.Lines.Enqueue(formatter(state, error) + (error is null ? "" : error.ToString()));
            }
        }
    }
}
