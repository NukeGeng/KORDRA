using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.ApprovalGovernance.Domain;

/// <summary>Closed offline policy command content; it conveys neither custody nor authority.</summary>
public sealed class ApprovalPolicyManifest
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("release-approval-policy-command-v1\n");
    private readonly byte[] _canonical;

    private ApprovalPolicyManifest(Guid manifestId, Guid installationId, string expectedDatabase, Guid tenantId,
        Guid projectId, Guid policyId, long expectedPolicyRevision, ReleaseApprovalPolicy policy,
        IReadOnlyList<ApprovalBindingChange> bindings, byte[] canonical)
    {
        ManifestId = manifestId;
        InstallationId = installationId;
        ExpectedDatabase = expectedDatabase;
        TenantId = tenantId;
        ProjectId = projectId;
        PolicyId = policyId;
        ExpectedPolicyRevision = expectedPolicyRevision;
        Policy = policy;
        BindingChanges = bindings;
        _canonical = canonical;
        var data = new byte[Domain.Length + canonical.Length];
        Domain.CopyTo(data, 0);
        canonical.CopyTo(data, Domain.Length);
        CommandHash = Convert.ToHexStringLower(SHA256.HashData(data));
    }

    public Guid ManifestId { get; }
    public Guid InstallationId { get; }
    public string ExpectedDatabase { get; }
    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid PolicyId { get; }
    public long ExpectedPolicyRevision { get; }
    public ReleaseApprovalPolicy Policy { get; }
    public IReadOnlyList<ApprovalBindingChange> BindingChanges { get; }
    public string CommandHash { get; }
    public byte[] GetCanonicalBytes() => (byte[])_canonical.Clone();

    public static ApprovalPolicyManifest Parse(ReadOnlyMemory<byte> input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 12 });
            var f = Fields(document.RootElement, "schemaVersion", "manifestId", "installationId", "expectedDatabase",
                "expectedRole", "tenantId", "projectId", "policyId", "expectedPolicyRevision", "payload", "bindingChanges");
            if (!f["schemaVersion"].TryGetInt32(out var schema) || schema != 1 ||
                f["expectedRole"].GetString() != "kordra_admin") throw Invalid();
            var manifest = Id(f["manifestId"]); var installation = Id(f["installationId"]);
            var tenant = Id(f["tenantId"]); var project = Id(f["projectId"]); var policyId = Id(f["policyId"]);
            var database = f["expectedDatabase"].GetString();
            if (string.IsNullOrWhiteSpace(database) || database.Any(char.IsControl)) throw Invalid();
            var revision = Revision(f["expectedPolicyRevision"], allowZero: true);
            var policy = ReleaseApprovalPolicy.Parse(Encoding.UTF8.GetBytes(f["payload"].GetRawText()));
            var changes = f["bindingChanges"];
            if (changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() > 32) throw Invalid();
            var bindings = new List<ApprovalBindingChange>();
            string? previous = null;
            foreach (var item in changes.EnumerateArray())
            {
                var b = Fields(item, "environmentId", "expectedEnvironmentRevision", "expectedBindingId", "expectedBindingRevision");
                var environment = Id(b["environmentId"]);
                var canonicalId = environment.ToString("D");
                if (previous is not null && StringComparer.Ordinal.Compare(previous, canonicalId) >= 0) throw Invalid();
                previous = canonicalId;
                var environmentRevision = Revision(b["expectedEnvironmentRevision"], allowZero: false, allowMax: true);
                var bindingId = b["expectedBindingId"].ValueKind == JsonValueKind.Null ? (Guid?)null : Id(b["expectedBindingId"]);
                var bindingRevision = Revision(b["expectedBindingRevision"], allowZero: true);
                if (bindingId.HasValue != (bindingRevision > 0)) throw Invalid();
                bindings.Add(new(environment, environmentRevision, bindingId, bindingRevision));
            }
            var canonical = Canonical(manifest, installation, database, tenant, project, policyId, revision, policy, bindings);
            return new ApprovalPolicyManifest(manifest, installation, database, tenant, project, policyId, revision,
                policy, new ReadOnlyCollection<ApprovalBindingChange>(bindings), canonical);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new ArgumentException("Invalid approval policy manifest.", nameof(input), ex);
        }
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
    private static Guid Id(JsonElement element)
    {
        var value = element.GetString();
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != value) throw Invalid();
        return parsed;
    }
    private static long Revision(JsonElement element, bool allowZero, bool allowMax = false)
    {
        if (!element.TryGetInt64(out var value) || (!allowMax && value == long.MaxValue) || value < (allowZero ? 0 : 1)) throw Invalid();
        return value;
    }
    private static ArgumentException Invalid() => new("Invalid approval policy manifest.");

    private static string QuoteDatabase(string database)
    {
        var result = new StringBuilder().Append('"');
        for (var i = 0; i < database.Length; i++)
        {
            var value = database[i];
            if (char.IsHighSurrogate(value))
            {
                if (i + 1 >= database.Length || !char.IsLowSurrogate(database[i + 1])) throw Invalid();
                result.Append(value).Append(database[++i]);
            }
            else if (char.IsLowSurrogate(value)) throw Invalid();
            else if (value is '"' or '\\') result.Append('\\').Append(value);
            else result.Append(value);
        }
        return result.Append('"').ToString();
    }

    private static byte[] Canonical(Guid manifest, Guid installation, string database, Guid tenant, Guid project,
        Guid policyId, long revision, ReleaseApprovalPolicy policy, List<ApprovalBindingChange> bindings)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("bindingChanges");
            foreach (var change in bindings)
            {
                writer.WriteStartObject();
                writer.WriteString("environmentId", change.EnvironmentId.ToString("D"));
                if (change.ExpectedBindingId.HasValue) writer.WriteString("expectedBindingId", change.ExpectedBindingId.Value.ToString("D"));
                else writer.WriteNull("expectedBindingId");
                writer.WriteNumber("expectedBindingRevision", change.ExpectedBindingRevision);
                writer.WriteNumber("expectedEnvironmentRevision", change.ExpectedEnvironmentRevision);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("expectedDatabase");
            writer.WriteRawValue(Encoding.UTF8.GetBytes(QuoteDatabase(database)), skipInputValidation: false);
            writer.WriteNumber("expectedPolicyRevision", revision);
            writer.WriteString("expectedRole", "kordra_admin");
            writer.WriteString("installationId", installation.ToString("D"));
            writer.WriteString("manifestId", manifest.ToString("D"));
            writer.WritePropertyName("payload"); writer.WriteRawValue(policy.GetCanonicalBytes(), skipInputValidation: false);
            writer.WriteString("policyId", policyId.ToString("D"));
            writer.WriteString("projectId", project.ToString("D"));
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("tenantId", tenant.ToString("D"));
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}

public sealed record ApprovalBindingChange(Guid EnvironmentId, long ExpectedEnvironmentRevision, Guid? ExpectedBindingId, long ExpectedBindingRevision);
