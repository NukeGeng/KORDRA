using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.ConnectionManagement.Contracts;

namespace Kordra.Modules.ConnectionManagement.Commands;

public sealed record RegistryCredentialManifest(int SchemaVersion, Guid ManifestId, Guid InstallationId, Guid TenantId, Guid ProjectId, Guid RegistryId, string Repository,
    string ConsumerId, string Operation, string Action, Guid? ExpectedBindingId, long ExpectedRevision, Guid BindingId,
    Guid? ProfileId, Guid? ReferenceId, Guid? LocatorId, DateTimeOffset? NotBeforeUtc, DateTimeOffset? ExpiresAtUtc)
{
    public RegistryCredentialSelectionV1 Selection => new(InstallationId, TenantId, ProjectId, RegistryId, Repository, ConsumerId, Operation);
    private static readonly string[] Properties = ["schemaVersion", "manifestId", "installationId", "tenantId", "projectId", "registryId", "repository", "consumerId", "operation", "action",
        "expectedBindingId", "expectedRevision", "bindingId", "profileId", "referenceId", "locatorId", "notBeforeUtc", "expiresAtUtc"];
    public bool IsValid() => SchemaVersion == 1 && ManifestId != Guid.Empty && Selection.IsValid && BindingId != Guid.Empty &&
        Action is "create" or "rotate" or "revoke" && ExpectedRevision >= 0 &&
        (ExpectedRevision == 0 ? Action == "create" && ExpectedBindingId is null : ExpectedBindingId is not null && ExpectedBindingId != Guid.Empty) &&
        (Action == "revoke" ? ProfileId is null && ReferenceId is null && LocatorId is null && NotBeforeUtc is null && ExpiresAtUtc is null :
            ProfileId is not null && ProfileId != Guid.Empty && ReferenceId is not null && ReferenceId != Guid.Empty && LocatorId is not null && LocatorId != Guid.Empty &&
            NotBeforeUtc is not null && ExpiresAtUtc is not null && ValidTime(NotBeforeUtc.Value) && ValidTime(ExpiresAtUtc.Value) && NotBeforeUtc < ExpiresAtUtc) &&
        (Action == "create" || ExpectedBindingId == BindingId);
    public bool IsActiveAt(DateTimeOffset now) => Action == "revoke" || NotBeforeUtc <= now && ExpiresAtUtc > now && ExpiresAtUtc <= now.AddDays(30);
    private static bool ValidTime(DateTimeOffset t) => t != default && t.Offset == TimeSpan.Zero && t.Ticks % 10 == 0;
    public byte[] CanonicalBytes()
    {
        if (!IsValid()) throw new InvalidOperationException("Invalid credential manifest.");
        using var output = new MemoryStream();
        using (var w = new Utf8JsonWriter(output))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", SchemaVersion); w.WriteString("manifestId", ManifestId.ToString("D"));
            w.WriteString("installationId", InstallationId.ToString("D")); w.WriteString("tenantId", TenantId.ToString("D")); w.WriteString("projectId", ProjectId.ToString("D")); w.WriteString("registryId", RegistryId.ToString("D")); w.WriteString("repository", Repository);
            w.WriteString("consumerId", ConsumerId); w.WriteString("operation", Operation); w.WriteString("action", Action);
            Write(w, "expectedBindingId", ExpectedBindingId); w.WriteNumber("expectedRevision", ExpectedRevision); w.WriteString("bindingId", BindingId.ToString("D"));
            Write(w, "profileId", ProfileId); Write(w, "referenceId", ReferenceId); Write(w, "locatorId", LocatorId);
            WriteTime(w, "notBeforeUtc", NotBeforeUtc); WriteTime(w, "expiresAtUtc", ExpiresAtUtc); w.WriteEndObject();
        }
        return output.ToArray();
    }
    public string Hash() => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes()));
    private static void Write(Utf8JsonWriter w, string k, Guid? id) { if (id is null) w.WriteNull(k); else w.WriteString(k, id.Value.ToString("D")); }
    private static void WriteTime(Utf8JsonWriter w, string k, DateTimeOffset? time) { if (time is null) w.WriteNull(k); else w.WriteString(k, time.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)); }
    public static RegistryCredentialManifest? Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > 16384) return null;
        try
        {
            using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 2 }); var x = doc.RootElement;
            if (x.ValueKind != JsonValueKind.Object) return null;
            var names = x.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != Properties.Length || names.Distinct(StringComparer.Ordinal).Count() != Properties.Length || names.Except(Properties).Any()) return null;
            Guid Id(string name) { var s = x.GetProperty(name).GetString(); if (!Guid.TryParseExact(s, "D", out var g) || g == Guid.Empty || s != g.ToString("D")) throw new FormatException(); return g; }
            Guid? Maybe(string name) => x.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Id(name);
            DateTimeOffset? Time(string name)
            {
                if (x.GetProperty(name).ValueKind == JsonValueKind.Null) return null;
                var text = x.GetProperty(name).GetString();
                if (!DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) throw new FormatException();
                return new DateTimeOffset(t);
            }
            var result = new RegistryCredentialManifest(x.GetProperty("schemaVersion").GetInt32(), Id("manifestId"), Id("installationId"), Id("tenantId"), Id("projectId"), Id("registryId"), x.GetProperty("repository").GetString()!,
                x.GetProperty("consumerId").GetString()!, x.GetProperty("operation").GetString()!, x.GetProperty("action").GetString()!, Maybe("expectedBindingId"),
                x.GetProperty("expectedRevision").GetInt64(), Id("bindingId"), Maybe("profileId"), Maybe("referenceId"), Maybe("locatorId"), Time("notBeforeUtc"), Time("expiresAtUtc"));
            return result.IsValid() ? result : null;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException) { return null; }
    }
}
