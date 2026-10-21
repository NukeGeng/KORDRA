namespace Kordra.Modules.AuditCompliance.Contracts;

/// <summary>Append and exact lookup are the only AUD operations exposed in Foundation T02.</summary>
public interface IAuditAppendPort
{
    ValueTask<AuditAppendOutcome> AppendAsync(AuditIntent intent, CancellationToken cancellationToken = default);

    ValueTask<AuditLookupOutcome> LookupAsync(Guid intentId, CancellationToken cancellationToken = default);
}
