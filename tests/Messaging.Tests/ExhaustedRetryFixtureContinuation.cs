using System.Diagnostics;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;

namespace Kordra.Messaging.Tests;

/// <summary>
/// Synthetic conformance policy only. Not registered in the Product kernel or an active host.
/// The fixture's SQLSTATE40001 trigger aborts before commit; UnknownCommit is never routed here.
/// Actual native DLQ persistence/settlement still requires an approved database fixture.
/// </summary>
public sealed class ExhaustedRetryFixtureContinuation(Guid installationId, Uri destination) : IContinuationSource
{
    public string Description => "Synthetic known-precommit retry exhaustion";

    public IContinuation? Build(Exception ex, Envelope envelope)
    {
        if (installationId == Guid.Empty || ex is not PostgresException { SqlState: "40001" } ||
            envelope.Attempts != 5 || envelope.Id == Guid.Empty || envelope.Destination != destination ||
            destination != new Uri("postgresql://nativecompatibility")) return null;
        return new Terminal(envelope, installationId, destination);
    }

    private sealed class Terminal(Envelope expected, Guid installationId, Uri destination) : IContinuation
    {
        public async ValueTask ExecuteAsync(IEnvelopeLifecycle lifecycle, IWolverineRuntime runtime,
            DateTimeOffset now, Activity? activity)
        {
            if (!ReferenceEquals(lifecycle.Envelope, expected))
                throw new InvalidOperationException("Fixture settlement identity changed.");
            // Do not read Data when that could invoke a handler CLR object or an attached serializer.
            var observed = expected.Message is null && expected.Serializer is null ? expected.Data ?? [] : [];
            RejectedEnvelopeDiagnostic.ReplaceInPlace(expected, observed, installationId, destination,
                MessageQuarantineReason.RetryExhausted);
            // Never propagate the original exception, inner exception, free-form PostgreSQL detail or payload.
            await lifecycle.MoveToDeadLetterQueueAsync(new InvalidOperationException("Synthetic precommit retry attempts exhausted."));
            // A failed move must not settle the only remaining delivery.
            await lifecycle.CompleteAsync();
        }
    }
}
