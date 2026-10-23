using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.Modules.IdentityAccess.Contracts;

public sealed record ResourceRule(string Case, string Effect);
public sealed record ResourcePolicyPayload(int SchemaVersion, string Action, string ScopeKind, string Status,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset? ExpiresAtUtc, IReadOnlyList<ResourceRule> Rules);
public sealed record ResourcePolicySnapshot(Guid PolicyId, Guid TenantId, Guid ProjectId, long Revision,
    ResourcePolicyPayload Payload, string ContentHash);
public sealed record ResourcePolicySelection(Guid BindingId, long BindingRevision, Guid TenantId, Guid ProjectId,
    Guid? EnvironmentId, string ScopeKind, Guid PolicyId, long PolicyRevision, string? Classification);
public enum ResourceEligibility { Allow, Deny, Unavailable }
public interface ICurrentResourcePolicyReader
{
    Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId,
        Guid policyId, long exactRevision, CancellationToken ct = default);
}
public sealed record ResourcePolicyCommandRecord(string ContentHash, string ResultJson, Guid AuditIntentId, string AuditIntentHash);
public interface IPolicyTenantAuthority
{
    Task<bool> IsActiveTenantUnderAdmissionAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Fixed resource eligibility vocabulary; it never grants a TEN permission.</summary>
public static class ResourcePolicies
{
    public static bool IsClassification(string? value) => value is "Development" or "Test" or "Staging" or "Production";
    public static bool IsPreciseUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero && value.Ticks % 10 == 0 &&
        value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
    public static bool IsValid(ResourcePolicyPayload? p) => p is not null && p.SchemaVersion == 1 && p.Action == "target.read" &&
        p.ScopeKind is "environment" or "project_without_environment" && p.Status is "active" or "inactive" &&
        IsPreciseUtc(p.EffectiveFromUtc) && (p.ExpiresAtUtc is null || IsPreciseUtc(p.ExpiresAtUtc.Value) && p.ExpiresAtUtc > p.EffectiveFromUtc) &&
        p.Rules is { Count: > 0 and <= 8 } && (p.ScopeKind != "project_without_environment" || p.Rules.Count <= 2) &&
        p.Rules.All(r => r is not null && r.Effect is "allow" or "deny" &&
            (p.ScopeKind == "environment" ? IsClassification(r.Case) : r.Case == "NoEnvironment")) &&
        p.Rules.Distinct().Count() == p.Rules.Count;

    public static byte[] CanonicalBytes(ResourcePolicyPayload p)
    {
        if (!IsValid(p)) throw new ArgumentException("Invalid resource policy.", nameof(p));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, p);
        return stream.ToArray();
    }
    public static string Hash(ResourcePolicyPayload p) => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(p)));
    public static string Timestamp(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    public static void Write(Utf8JsonWriter w, ResourcePolicyPayload p)
    {
        if (!IsValid(p)) throw new ArgumentException("Invalid resource policy.", nameof(p));
        w.WriteStartObject(); w.WriteNumber("schemaVersion", p.SchemaVersion); w.WriteString("action", p.Action);
        w.WriteString("scopeKind", p.ScopeKind); w.WriteString("status", p.Status);
        w.WriteString("effectiveFromUtc", Timestamp(p.EffectiveFromUtc));
        if (p.ExpiresAtUtc is { } expires) w.WriteString("expiresAtUtc", Timestamp(expires)); else w.WriteNull("expiresAtUtc");
        w.WriteStartArray("rules");
        foreach (var r in p.Rules.OrderBy(x => x.Case, StringComparer.Ordinal).ThenBy(x => x.Effect, StringComparer.Ordinal))
        { w.WriteStartObject(); w.WriteString("case", r.Case); w.WriteString("effect", r.Effect); w.WriteEndObject(); }
        w.WriteEndArray(); w.WriteEndObject(); w.Flush();
    }
    public static ResourcePolicyPayload? Parse(string json)
    {
        try
        {
            if (json is null || Encoding.UTF8.GetByteCount(json) > 128 * 1024) return null;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var e = document.RootElement;
            if (!Exact(e, "schemaVersion", "action", "scopeKind", "status", "effectiveFromUtc", "expiresAtUtc", "rules")) return null;
            var rules = e.GetProperty("rules");
            if (rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() is < 1 or > 8) return null;
            var parsed = new List<ResourceRule>();
            foreach (var r in rules.EnumerateArray())
            { if (!Exact(r, "case", "effect")) return null; parsed.Add(new(r.GetProperty("case").GetString()!, r.GetProperty("effect").GetString()!)); }
            if (!DateTimeOffset.TryParseExact(e.GetProperty("effectiveFromUtc").GetString(), "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var effective)) return null;
            DateTimeOffset? expiry = null;
            if (e.GetProperty("expiresAtUtc").ValueKind != JsonValueKind.Null)
            {
                if (!DateTimeOffset.TryParseExact(e.GetProperty("expiresAtUtc").GetString(), "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)) return null;
                expiry = value;
            }
            var p = new ResourcePolicyPayload(e.GetProperty("schemaVersion").GetInt32(), e.GetProperty("action").GetString()!,
                e.GetProperty("scopeKind").GetString()!, e.GetProperty("status").GetString()!, effective, expiry, parsed.AsReadOnly());
            return IsValid(p) ? p : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException) { return null; }
    }
    public static bool Exact(JsonElement e, params string[] names) => e.ValueKind == JsonValueKind.Object &&
        e.EnumerateObject().Count() == names.Length && e.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == names.Length &&
        e.EnumerateObject().All(x => names.Contains(x.Name, StringComparer.Ordinal));

    public static ResourceEligibility Evaluate(ResourcePolicySnapshot? policy, ResourcePolicySelection s, DateTimeOffset now)
    {
        if (policy is null || s is null || s.BindingId == Guid.Empty || s.BindingRevision <= 0 ||
            s.TenantId == Guid.Empty || s.ProjectId == Guid.Empty || s.PolicyId == Guid.Empty || s.PolicyRevision <= 0 ||
            policy.PolicyId != s.PolicyId || policy.Revision != s.PolicyRevision || policy.TenantId != s.TenantId || policy.ProjectId != s.ProjectId ||
            !IsValid(policy.Payload) || policy.ContentHash != Hash(policy.Payload)) return ResourceEligibility.Deny;
        var p = policy.Payload;
        if (p.Status != "active" || p.EffectiveFromUtc > now || p.ExpiresAtUtc <= now || p.ScopeKind != s.ScopeKind ||
            (s.EnvironmentId is null ? s.ScopeKind != "project_without_environment" || s.Classification is not null :
                s.EnvironmentId == Guid.Empty || s.ScopeKind != "environment" || !IsClassification(s.Classification))) return ResourceEligibility.Deny;
        var ruleCase = s.EnvironmentId is null ? "NoEnvironment" : s.Classification;
        return p.Rules.Any(x => x.Case == ruleCase && x.Effect == "allow") && !p.Rules.Any(x => x.Case == ruleCase && x.Effect == "deny")
            ? ResourceEligibility.Allow : ResourceEligibility.Deny;
    }
}
