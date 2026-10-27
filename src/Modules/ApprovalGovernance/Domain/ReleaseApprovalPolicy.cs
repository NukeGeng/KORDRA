using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.ApprovalGovernance.Domain;

// Validates an offline business policy payload only. This type does not authorize any action.
public sealed class ReleaseApprovalPolicy
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("release-approval-policy-v1\n");
    private readonly byte[] _canonical;

    private ReleaseApprovalPolicy(string status, DateTimeOffset effectiveFrom, DateTimeOffset? expiresAt,
        IReadOnlyList<ReleaseApprovalRule> rules, byte[] canonical)
    {
        Status = status;
        EffectiveFromUtc = effectiveFrom;
        ExpiresAtUtc = expiresAt;
        Rules = rules;
        _canonical = canonical;
        var input = new byte[Domain.Length + canonical.Length];
        Domain.CopyTo(input, 0);
        canonical.CopyTo(input, Domain.Length);
        PayloadHash = Convert.ToHexStringLower(SHA256.HashData(input));
    }

    public string Status { get; }
    public DateTimeOffset EffectiveFromUtc { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    public IReadOnlyList<ReleaseApprovalRule> Rules { get; }
    public string PayloadHash { get; }
    public byte[] GetCanonicalBytes() => (byte[])_canonical.Clone();

    // This is policy interpretation, not an authorization or artifact qualification decision.
    public ReleaseApprovalRule? FindApplicableAllowRule(string classification, DateTimeOffset nowUtc)
    {
        if (Status != "active" || nowUtc < EffectiveFromUtc || (ExpiresAtUtc.HasValue && nowUtc >= ExpiresAtUtc.Value))
            return null;
        if (Rules.Any(rule => rule.Classification == classification && rule.Effect == "deny")) return null;
        return Rules.SingleOrDefault(rule => rule.Classification == classification && rule.Effect == "allow");
    }

    public static ReleaseApprovalPolicy Parse(ReadOnlyMemory<byte> input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            var fields = Fields(root, "schemaVersion", "purpose", "status", "effectiveFromUtc", "expiresAtUtc", "rules");
            if (!fields["schemaVersion"].TryGetInt32(out var version) || version != 1 ||
                String(fields["purpose"]) != "release-intent-v1")
                throw Invalid();
            var status = String(fields["status"]);
            if (status is not ("active" or "inactive")) throw Invalid();
            var start = Time(fields["effectiveFromUtc"]);
            var expiry = fields["expiresAtUtc"].ValueKind == JsonValueKind.Null ? (DateTimeOffset?)null : Time(fields["expiresAtUtc"]);
            if (expiry <= start) throw Invalid();
            var rulesElement = fields["rules"];
            if (rulesElement.ValueKind != JsonValueKind.Array || rulesElement.GetArrayLength() is < 1 or > 8) throw Invalid();
            var rules = new List<ReleaseApprovalRule>();
            var seen = new HashSet<(string, string)>();
            foreach (var rule in rulesElement.EnumerateArray())
            {
                var parsed = ParseRule(rule);
                if (!seen.Add((parsed.Classification, parsed.Effect))) throw Invalid();
                rules.Add(parsed);
            }
            rules.Sort((a, b) =>
            {
                var comparison = StringComparer.Ordinal.Compare(a.Classification, b.Classification);
                return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(a.Effect, b.Effect);
            });
            var readOnlyRules = new ReadOnlyCollection<ReleaseApprovalRule>(rules);
            return new ReleaseApprovalPolicy(status, start, expiry, readOnlyRules, Canonical(status, start, expiry, rules));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new ArgumentException("Invalid release approval policy.", nameof(input), ex);
        }
    }

    private static ReleaseApprovalRule ParseRule(JsonElement element)
    {
        var fields = Fields(element, "classification", "effect", "separationOfDuties", "allowUnattestedProvenance",
            "pendingLifetimeSeconds", "grantLifetimeSeconds", "blockedWindowsUtc");
        var classification = String(fields["classification"]);
        var effect = String(fields["effect"]);
        var separation = String(fields["separationOfDuties"]);
        if (classification is not ("Development" or "Test" or "Staging" or "Production") ||
            effect is not ("allow" or "deny") || separation is not ("independent" or "self_allowed")) throw Invalid();
        var unattested = fields["allowUnattestedProvenance"].GetBoolean();
        if ((effect == "deny" && (separation != "independent" || unattested)) ||
            ((classification is "Staging" or "Production") && (separation == "self_allowed" || unattested))) throw Invalid();
        var pending = Lifetime(fields["pendingLifetimeSeconds"]);
        var grant = Lifetime(fields["grantLifetimeSeconds"]);
        var value = fields["blockedWindowsUtc"];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 16) throw Invalid();
        var windows = new List<ApprovalBlockedWindow>();
        foreach (var entry in value.EnumerateArray())
        {
            var f = Fields(entry, "startsAtUtc", "endsAtUtc");
            var start = Time(f["startsAtUtc"]);
            var end = Time(f["endsAtUtc"]);
            if (start >= end || (windows.Count > 0 && windows[^1].EndsAtUtc > start)) throw Invalid();
            windows.Add(new ApprovalBlockedWindow(start, end));
        }
        return new ReleaseApprovalRule(classification, effect, separation, unattested, pending, grant,
            new ReadOnlyCollection<ApprovalBlockedWindow>(windows));
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var allowed = new HashSet<string>(expected, StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !result.TryAdd(property.Name, property.Value)) throw Invalid();
        if (result.Count != allowed.Count) throw Invalid();
        return result;
    }

    private static string String(JsonElement element) => element.GetString() ?? throw Invalid();
    private static int Lifetime(JsonElement element) => element.TryGetInt32(out var value) && value is >= 60 and <= 86400
        ? value : throw Invalid();
    private static DateTimeOffset Time(JsonElement element)
    {
        var text = String(element);
        if (!DateTimeOffset.TryParseExact(text, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) ||
            result.ToString(TimestampFormat, CultureInfo.InvariantCulture) != text) throw Invalid();
        return result;
    }
    private static ArgumentException Invalid() => new("Invalid release approval policy.");
    private static string Format(DateTimeOffset time) => time.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static byte[] Canonical(string status, DateTimeOffset start, DateTimeOffset? expiry, List<ReleaseApprovalRule> rules)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("effectiveFromUtc", Format(start));
            if (expiry.HasValue) writer.WriteString("expiresAtUtc", Format(expiry.Value));
            else writer.WriteNull("expiresAtUtc");
            writer.WriteString("purpose", "release-intent-v1");
            writer.WriteStartArray("rules");
            foreach (var rule in rules)
            {
                writer.WriteStartObject();
                writer.WriteBoolean("allowUnattestedProvenance", rule.AllowUnattestedProvenance);
                writer.WriteStartArray("blockedWindowsUtc");
                foreach (var window in rule.BlockedWindowsUtc)
                {
                    writer.WriteStartObject();
                    writer.WriteString("endsAtUtc", Format(window.EndsAtUtc));
                    writer.WriteString("startsAtUtc", Format(window.StartsAtUtc));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteString("classification", rule.Classification);
                writer.WriteString("effect", rule.Effect);
                writer.WriteNumber("grantLifetimeSeconds", rule.GrantLifetimeSeconds);
                writer.WriteNumber("pendingLifetimeSeconds", rule.PendingLifetimeSeconds);
                writer.WriteString("separationOfDuties", rule.SeparationOfDuties);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("status", status);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}

public sealed record ApprovalBlockedWindow(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
public sealed record ReleaseApprovalRule(string Classification, string Effect, string SeparationOfDuties,
    bool AllowUnattestedProvenance, int PendingLifetimeSeconds, int GrantLifetimeSeconds,
    IReadOnlyList<ApprovalBlockedWindow> BlockedWindowsUtc);
