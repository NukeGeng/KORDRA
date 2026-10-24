using Kordra.Modules.InfrastructureInventory.Contracts;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;

/// <summary>Called only by a registered post-C consumer. The caller supplies its trusted authority clock.</summary>
public sealed class NomadWorkloadReader(NomadTargetTrust trust, Func<DateTimeOffset> trustedUtcNow)
{
    public async Task<NomadWorkloadsV1> ReadAsync(Guid invocation, Guid target, ReadOnlyMemory<byte> credential, CancellationToken ct)
    {
        if (invocation == Guid.Empty || target != trust.TargetId) throw NomadReadOnlyTransport.Unavailable();
        using var transport = new NomadReadOnlyTransport(trust, trustedUtcNow);
        var ns = Uri.EscapeDataString(trust.NamespaceName); var region = Uri.EscapeDataString(trust.Region);
        var identity = $"/v1/namespace/{ns}?region={region}";
        await VerifyIdentity(); var captured = Now();
        var jobs = await Collection("Jobs", $"/v1/jobs?namespace={ns}&region={region}&per_page=25&meta=false");
        var allocations = await Collection("Allocations", $"/v1/allocations?namespace={ns}&region={region}&per_page=25&resources=false&task_states=false");
        await VerifyIdentity(); var completed = Now(); var expiry = captured.AddSeconds(15);
        if (completed < captured || completed >= expiry || jobs.Status != "observed" && allocations.Status != "observed") throw NomadReadOnlyTransport.Unavailable();
        ct.ThrowIfCancellationRequested();
        return new(1, invocation, target, "nomad", trust.NamespaceName, trust.Region, captured, completed, expiry,
            jobs.Coverage == "complete" && allocations.Coverage == "complete" ? "complete" : "partial", Array.AsReadOnly(new[] { jobs, allocations }));
        async Task VerifyIdentity()
        { var r = await transport.GetAsync(identity, credential, ct); if (r.Status != 200) throw NomadReadOnlyTransport.Unavailable(); NomadWorkloadProjection.Identity(r.Body, trust); }
        async Task<NomadResourceSetV1> Collection(string kind, string path)
        {
            var r = await transport.GetAsync(path, credential, ct);
            if (r.Status is 403 or 404 or 429 or >= 500 and <= 599 || r.Status == 200 && !r.KnownLeader)
                return new(kind, "unavailable", "unavailable", null, Array.Empty<NomadWorkloadItemV1>());
            if (r.Status != 200 || !NomadWorkloadProjection.ValidIndex(r.Index)) throw NomadReadOnlyTransport.Unavailable();
            return NomadWorkloadProjection.Project(kind, r.Body, trust.NamespaceName, r.Index!, r.Partial);
        }
    }
    private DateTimeOffset Now()
    { var now = trustedUtcNow(); if (now.Offset != TimeSpan.Zero || now == default || now >= DateTimeOffset.MaxValue.AddSeconds(-15)) throw NomadReadOnlyTransport.Unavailable(); return now; }
}
