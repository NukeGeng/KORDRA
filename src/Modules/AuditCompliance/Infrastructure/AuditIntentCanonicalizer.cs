using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Contracts;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>Canonicalizes the allowlisted, nonsecret audit v1 fields before hashing or persistence.</summary>
public static class AuditIntentCanonicalizer
{
    public static string ComputeHash(AuditIntent intent)
    {
        try
        {
            return Canonicalize(intent).HashHex;
        }
        catch (AuditIntentValidationException)
        {
            throw new ArgumentException("The audit intent contains invalid fields.", nameof(intent));
        }
    }

    internal static CanonicalizedAuditIntent Canonicalize(AuditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (intent.SchemaVersion != 1 || intent.IntentId == Guid.Empty || intent.InvocationId == Guid.Empty ||
            intent.CorrelationId == Guid.Empty || !Enum.IsDefined(intent.Stage) ||
            intent.OccurredAtUtc == default ||
            IsEmpty(intent.SubjectRef) || IsEmpty(intent.TenantRef) || IsEmpty(intent.ResourceRef) || IsEmpty(intent.ScopeId) ||
            intent.PolicyRevision is <= 0 || intent.AuthorityRevision is <= 0)
        {
            throw new AuditIntentValidationException();
        }

        var permission = NormalizeToken(intent.Permission, 128, allowDot: true, allowColon: true, allowHyphen: true, allowUnderscore: false, allowStar: true);
        var scopeKind = NormalizeToken(intent.ScopeKind, 32, allowDot: true, allowColon: false, allowHyphen: true, allowUnderscore: true, allowStar: false);
        var safeReason = NormalizeToken(intent.SafeReason, 128, allowDot: true, allowColon: true, allowHyphen: true, allowUnderscore: true, allowStar: false);
        var stage = StageCode(intent.Stage);
        var actionOutcome = ActionOutcomeCode(intent.Stage, intent.ActionOutcome);

        string? resultDigest = null;
        long? resultSizeBytes = null;
        if (intent.Stage == AuditIntentStage.Disclosure)
        {
            if (intent.ResultDigest is not { Length: 64 } digest ||
                !digest.All(Uri.IsHexDigit) || intent.ResultSizeBytes is null or < 0)
            {
                throw new AuditIntentValidationException();
            }

            resultDigest = digest.ToLowerInvariant();
            resultSizeBytes = intent.ResultSizeBytes;
        }
        else if (intent.ResultDigest is not null || intent.ResultSizeBytes is not null)
        {
            throw new AuditIntentValidationException();
        }

        var canonicalIntent = intent with
        {
            Permission = permission,
            ScopeKind = scopeKind,
            SafeReason = safeReason,
            OccurredAtUtc = intent.OccurredAtUtc.ToUniversalTime(),
            ResultDigest = resultDigest,
            ResultSizeBytes = resultSizeBytes
        };
        var canonicalJson = SerializeCanonical(canonicalIntent, stage, actionOutcome);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return new CanonicalizedAuditIntent(canonicalIntent, stage, canonicalJson, hash, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string SerializeCanonical(AuditIntent intent, string stage, string? actionOutcome)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", intent.SchemaVersion);
            writer.WriteString("intentId", intent.IntentId.ToString("D"));
            writer.WriteString("invocationId", intent.InvocationId.ToString("D"));
            writer.WriteString("stage", stage);
            if (actionOutcome is null) writer.WriteNull("actionOutcome");
            else writer.WriteString("actionOutcome", actionOutcome);
            WriteGuid(writer, "subjectRef", intent.SubjectRef);
            WriteGuid(writer, "tenantRef", intent.TenantRef);
            WriteGuid(writer, "resourceRef", intent.ResourceRef);
            writer.WriteString("permission", intent.Permission);
            writer.WriteString("scopeKind", intent.ScopeKind);
            WriteGuid(writer, "scopeId", intent.ScopeId);
            WriteNumber(writer, "policyRevision", intent.PolicyRevision);
            WriteNumber(writer, "authorityRevision", intent.AuthorityRevision);
            writer.WriteString("safeReason", intent.SafeReason);
            writer.WriteString("occurredAtUtc", intent.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("correlationId", intent.CorrelationId.ToString("D"));
            if (intent.ResultDigest is null)
            {
                writer.WriteNull("resultDigest");
                writer.WriteNull("resultSizeBytes");
            }
            else
            {
                writer.WriteString("resultDigest", intent.ResultDigest);
                writer.WriteNumber("resultSizeBytes", intent.ResultSizeBytes!.Value);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string NormalizeToken(
        string value,
        int maximumLength,
        bool allowDot,
        bool allowColon,
        bool allowHyphen,
        bool allowUnderscore,
        bool allowStar)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AuditIntentValidationException();
        }

        var normalized = value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (normalized.Length > maximumLength ||
            !normalized.All(character => char.IsAsciiLetterOrDigit(character) ||
                (allowDot && character == '.') ||
                (allowColon && character == ':') ||
                (allowHyphen && character == '-') ||
                (allowUnderscore && character == '_') ||
                (allowStar && character == '*')))
        {
            throw new AuditIntentValidationException();
        }

        return normalized;
    }

    private static string StageCode(AuditIntentStage stage) => stage switch
    {
        AuditIntentStage.Use => "U",
        AuditIntentStage.Disclosure => "D",
        AuditIntentStage.Mutation => "mutation",
        AuditIntentStage.Outcome => "outcome",
        _ => throw new AuditIntentValidationException()
    };

    private static string? ActionOutcomeCode(AuditIntentStage stage, AuditActionOutcome? outcome)
    {
        if (stage == AuditIntentStage.Outcome)
        {
            return outcome switch
            {
                AuditActionOutcome.Completed => "completed",
                AuditActionOutcome.Denied => "denied",
                AuditActionOutcome.Cancelled => "cancelled",
                AuditActionOutcome.Unknown => "unknown",
                _ => throw new AuditIntentValidationException()
            };
        }

        if (outcome is not null)
        {
            throw new AuditIntentValidationException();
        }

        return null;
    }

    private static void WriteGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value.Value.ToString("D"));
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteNumber(name, value.Value);
    }

    private static bool IsEmpty(Guid? value) => value.HasValue && value.Value == Guid.Empty;
}

internal sealed record CanonicalizedAuditIntent(
    AuditIntent Intent,
    string StageCode,
    string CanonicalJson,
    byte[] Hash,
    string HashHex);

internal sealed class AuditIntentValidationException : Exception { }
