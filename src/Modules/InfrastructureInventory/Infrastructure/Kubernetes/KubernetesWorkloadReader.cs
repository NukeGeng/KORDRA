using System.Security.Cryptography;
using Kordra.Modules.InfrastructureInventory.Contracts;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;

/// <summary>Registered post-C consumer only; every operation remains inside the caller's provider budget.</summary>
public sealed class KubernetesWorkloadReader(KubernetesTargetTrust trust, Func<DateTimeOffset> trustedUtcNow)
{
    public async Task<KubernetesWorkloadsV1> ReadAsync(Guid invocation, Guid target, ReadOnlyMemory<byte> credential, CancellationToken ct)
    {
        if (invocation == Guid.Empty || target != trust.TargetId) throw KubernetesReadOnlyTransport.Unavailable();
        using var transport = new KubernetesReadOnlyTransport(trust, trustedUtcNow);
        var ns = Uri.EscapeDataString(trust.NamespaceName); var identity = $"/api/v1/namespaces/{ns}";
        await VerifyIdentity(); var captured = Now();
        var sets = new List<KubernetesResourceSetV1>();
        foreach (var (kind, group, resource) in new[] { ("Pod", "/api/v1", "pods"), ("Deployment", "/apis/apps/v1", "deployments"),
                     ("StatefulSet", "/apis/apps/v1", "statefulsets"), ("DaemonSet", "/apis/apps/v1", "daemonsets") })
        {
            var r = await transport.GetAsync($"{group}/namespaces/{ns}/{resource}?limit=25&timeoutSeconds=1", credential, ct);
            if (r.Status is 403 or 404 or 429 or >= 500 and <= 599)
                sets.Add(new(kind, "unavailable", "unavailable", null, Array.Empty<KubernetesWorkloadItemV1>()));
            else
            { if (r.Status != 200) throw KubernetesReadOnlyTransport.Unavailable(); try { sets.Add(KubernetesWorkloadProjection.Project(kind, r.Body, trust.NamespaceName)); }
                finally { CryptographicOperations.ZeroMemory(r.Body); } }
        }
        await VerifyIdentity(); var completed = Now(); var expiry = captured.AddSeconds(15);
        var ids = sets.SelectMany(s => s.Items).Select(i => i.Uid).ToArray();
        if (completed < captured || completed >= expiry || !sets.Any(s => s.Status == "observed") || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw KubernetesReadOnlyTransport.Unavailable();
        ct.ThrowIfCancellationRequested();
        return new(1, invocation, target, trust.NamespaceName, captured, completed, expiry,
            sets.All(s => s.Coverage == "complete") ? "complete" : "partial", sets.AsReadOnly());
        async Task VerifyIdentity()
        { var r = await transport.GetAsync(identity, credential, ct); if (r.Status != 200) throw KubernetesReadOnlyTransport.Unavailable(); try { KubernetesWorkloadProjection.Identity(r.Body, trust); } finally { CryptographicOperations.ZeroMemory(r.Body); } }
    }
    private DateTimeOffset Now()
    { var n = trustedUtcNow(); if (n.Offset != TimeSpan.Zero || n == default || n >= DateTimeOffset.MaxValue.AddSeconds(-15)) throw KubernetesReadOnlyTransport.Unavailable(); return n; }
}
