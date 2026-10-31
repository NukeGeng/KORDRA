using System.Reflection;
using System.Text;
using System.Text.Json;
using Npgsql;
using Wolverine;
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class ExhaustedRetryContinuationTests
{
    private static readonly Uri Destination = new("postgresql://nativecompatibility");
    private const string Secret = "synthetic-terminal-sensitive-token";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task KnownFinalPrecommitFailureSanitizesBeforeMoveAndCompletesOnlyAfterSuccessfulMove(bool moveFails, bool hasClrMessage)
    {
        var envelope = new Envelope { Id = Guid.NewGuid(), Attempts = 5, Destination = Destination,
            Data = Encoding.UTF8.GetBytes(Secret), Message = hasClrMessage ? new { Token = Secret } : null, Failure = new Exception(Secret),
            TenantId = Secret, CorrelationId = Secret, ReplyUri = new Uri("https://example.invalid/" + Secret) };
        envelope.Headers[Secret] = Secret;
        var installation = Guid.NewGuid();
        var continuation = new ExhaustedRetryFixtureContinuation(installation, Destination).Build(Failure(), envelope);
        Assert.NotNull(continuation);
        var lifecycle = DispatchProxy.Create<IEnvelopeLifecycle, LifecycleProbe>();
        var probe = (LifecycleProbe)(object)lifecycle; probe.Current = envelope; probe.FailMove = moveFails;
        var id = envelope.Id;
        if (moveFails)
            await Assert.ThrowsAsync<InvalidOperationException>(() => continuation.ExecuteAsync(lifecycle, null!, DateTimeOffset.UtcNow, null).AsTask());
        else
            await continuation.ExecuteAsync(lifecycle, null!, DateTimeOffset.UtcNow, null);
        Assert.Equal(moveFails ? ["move"] : new[] { "move", "complete" }, probe.Calls);
        Assert.Equal(id, envelope.Id); Assert.Equal(Destination, envelope.Destination);
        Assert.DoesNotContain(Secret, probe.PersistedWire!);
        Assert.DoesNotContain(Secret, probe.StoredException!.ToString()); Assert.Null(probe.StoredException.InnerException);
        using var diagnostic = JsonDocument.Parse(envelope.Data!);
        Assert.Equal("RetryExhausted", diagnostic.RootElement.GetProperty("reason").GetString());
        Assert.Equal(installation.ToString("D"), diagnostic.RootElement.GetProperty("installationId").GetString());
        Assert.Equal(!hasClrMessage, diagnostic.RootElement.GetProperty("bodyWithinLimit").GetBoolean());
    }

    [Fact]
    public void UnknownCommitOtherFailuresAttemptsAndDestinationsAreNeverClaimed()
    {
        var source = new ExhaustedRetryFixtureContinuation(Guid.NewGuid(), Destination);
        var envelope = new Envelope { Destination = Destination, Attempts = 5 };
        Assert.Null(source.Build(new InvalidOperationException("unknown commit"), envelope));
        Assert.Null(source.Build(new PostgresException(Secret, "ERROR", "ERROR", "08006"), envelope));
        foreach (var attempt in new[] { 0, 1, 4, 6, int.MaxValue })
        { envelope.Attempts = attempt; Assert.Null(source.Build(Failure(), envelope)); }
        envelope.Attempts = 5; envelope.Destination = new Uri("postgresql://elsewhere");
        Assert.Null(source.Build(Failure(), envelope));
        envelope.Destination = Destination; envelope.Id = Guid.Empty;
        Assert.Null(source.Build(Failure(), envelope));
    }

    [Fact]
    public async Task ReplacedLifecycleEnvelopeCannotSettleAnotherDelivery()
    {
        var original = new Envelope { Destination = Destination, Attempts = 5 };
        var continuation = new ExhaustedRetryFixtureContinuation(Guid.NewGuid(), Destination).Build(Failure(), original)!;
        var lifecycle = DispatchProxy.Create<IEnvelopeLifecycle, LifecycleProbe>();
        var probe = (LifecycleProbe)(object)lifecycle; probe.Current = new Envelope { Destination = Destination };
        await Assert.ThrowsAsync<InvalidOperationException>(() => continuation.ExecuteAsync(lifecycle, null!, DateTimeOffset.UtcNow, null).AsTask());
        Assert.Empty(probe.Calls);
    }

    private static PostgresException Failure() => new(Secret, "ERROR", "ERROR", "40001");

    public class LifecycleProbe : DispatchProxy
    {
        public Envelope Current { get; set; } = null!;
        public bool FailMove { get; set; }
        public List<string> Calls { get; } = [];
        public string? PersistedWire { get; private set; }
        public Exception? StoredException { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "get_Envelope") return Current;
            if (method?.Name == "MoveToDeadLetterQueueAsync")
            {
                Calls.Add("move"); StoredException = Assert.IsAssignableFrom<Exception>(args![0]);
                PersistedWire = Encoding.UTF8.GetString(EnvelopeSerializer.Serialize(Current));
                return FailMove ? Task.FromException(new InvalidOperationException("Synthetic DLQ unavailable.")) : Task.CompletedTask;
            }
            if (method?.Name == "CompleteAsync") { Calls.Add("complete"); return ValueTask.CompletedTask; }
            throw new InvalidOperationException("Unexpected fixture action: " + method?.Name);
        }
    }
}
