using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>Owner-side comparison of immutable AUD evidence. No authorization or owner state is inferred.</summary>
public sealed class AuditMutationEvidenceReader(IDbContextFactory<AuditDbContext> contexts) : IAuditMutationEvidenceReader
{
    public async Task<AuditEvidenceStatus> MatchUnderAdmissionAsync(AuditMutationExpectation expected, CancellationToken ct = default)
    {
        if (expected is null || expected.IntentId == Guid.Empty || expected.SubjectId == Guid.Empty || expected.TenantId == Guid.Empty ||
            expected.ResourceId == Guid.Empty || expected.InvocationId == Guid.Empty || expected.ScopeId == Guid.Empty || expected.IntentHash is not { Length: 64 } ||
            !expected.IntentHash.All(char.IsAsciiHexDigitLower) || string.IsNullOrEmpty(expected.Permission) || string.IsNullOrEmpty(expected.ScopeKind) || string.IsNullOrEmpty(expected.SafeReason))
            return AuditEvidenceStatus.Mismatch;
        AuditEntry? row;
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            row = await db.Entries.AsNoTracking().SingleOrDefaultAsync(x => x.IntentId == expected.IntentId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return AuditEvidenceStatus.Unavailable; }
        if (row is null) return AuditEvidenceStatus.Missing;
        try
        {
            if (row.SchemaVersion != 1 || row.Stage != "mutation" || row.SubjectRef != expected.SubjectId || row.TenantRef != expected.TenantId ||
                row.ResourceRef != expected.ResourceId || row.InvocationId != expected.InvocationId || row.Permission != expected.Permission || row.ScopeKind != expected.ScopeKind ||
                row.ScopeId != expected.ScopeId || row.SafeReason != expected.SafeReason || row.CanonicalIntentHash.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(row.CanonicalIntentHash, Convert.FromHexString(expected.IntentHash)) ||
                !CryptographicOperations.FixedTimeEquals(row.CanonicalIntentHash, SHA256.HashData(Encoding.UTF8.GetBytes(row.CanonicalIntent))))
                return AuditEvidenceStatus.Mismatch;
            using var json = JsonDocument.Parse(row.CanonicalIntent);
            var root = json.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("intentId").GetGuid() != expected.IntentId ||
                root.GetProperty("invocationId").GetGuid() != expected.InvocationId || root.GetProperty("stage").GetString() != "mutation" || root.GetProperty("subjectRef").GetGuid() != expected.SubjectId ||
                root.GetProperty("tenantRef").GetGuid() != expected.TenantId || root.GetProperty("resourceRef").GetGuid() != expected.ResourceId ||
                root.GetProperty("permission").GetString() != expected.Permission || root.GetProperty("scopeKind").GetString() != expected.ScopeKind ||
                root.GetProperty("scopeId").GetGuid() != expected.ScopeId || root.GetProperty("safeReason").GetString() != expected.SafeReason ||
                root.GetProperty("actionOutcome").ValueKind != JsonValueKind.Null) return AuditEvidenceStatus.Mismatch;
            return AuditEvidenceStatus.Matched;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { return AuditEvidenceStatus.Mismatch; }
    }
}
