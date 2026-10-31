using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.Modules.WorkflowOrchestration.Domain;

public enum WorkflowEnvironment { Development = 1, Test }

/// <summary>Immutable business data only. Trusted owner resolution and current authority are separate.</summary>
public sealed class WorkflowSpecification
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    public const string TemplateId = "pilot.checkpoint.v1";
    public const int TemplateVersion = 1;
    private const string TemplateDescriptor = "{\"templateId\":\"pilot.checkpoint.v1\",\"version\":1,\"steps\":[\"Checkpoint\",\"WaitUntil\",\"Checkpoint\",\"Complete\"]}";
    public static string TemplateHash => Hash(Encoding.UTF8.GetBytes(TemplateDescriptor));
    private readonly Guid[] _ids;
    private static readonly string[] IdNames = ["installationId", "tenantId", "instanceId", "requestId", "projectId", "applicationId", "environmentId", "targetId", "releaseId"];
    private static readonly string[] RootFields = ["schemaVersion", ..IdNames, "releaseRevision", "releaseHash", "environmentClass", "templateId", "templateVersion", "templateHash", "createdAtUtc", "deadlineUtc", "steps"];
    public long ReleaseRevision { get; }
    public string ReleaseHash { get; }
    public WorkflowEnvironment Environment { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }
    public DateTimeOffset DueAtUtc { get; }
    public Guid InstallationId => _ids[0];
    public Guid TenantId => _ids[1];
    public Guid InstanceId => _ids[2];
    public Guid RequestId => _ids[3];
    public Guid ProjectId => _ids[4];
    public Guid ApplicationId => _ids[5];
    public Guid EnvironmentId => _ids[6];
    public Guid TargetId => _ids[7];
    public Guid ReleaseId => _ids[8];

    public WorkflowSpecification(Guid installationId, Guid tenantId, Guid instanceId, Guid requestId,
        Guid projectId, Guid applicationId, Guid environmentId, Guid targetId, Guid releaseId,
        long releaseRevision, string releaseHash, WorkflowEnvironment environment,
        DateTimeOffset createdAtUtc, DateTimeOffset deadlineUtc, DateTimeOffset dueAtUtc)
    {
        _ids = [installationId, tenantId, instanceId, requestId, projectId, applicationId, environmentId, targetId, releaseId];
        if (_ids.Any(id => id == Guid.Empty) || releaseRevision < 1 ||
            releaseHash is null || releaseHash.Length != 64 || releaseHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            !Enum.IsDefined(environment)) throw new ArgumentException("Invalid workflow business binding.");
        createdAtUtc = createdAtUtc.ToUniversalTime(); deadlineUtc = deadlineUtc.ToUniversalTime(); dueAtUtc = dueAtUtc.ToUniversalTime();
        if (createdAtUtc == default || deadlineUtc <= createdAtUtc || deadlineUtc - createdAtUtc > TimeSpan.FromHours(24) ||
            dueAtUtc < createdAtUtc || dueAtUtc >= deadlineUtc)
            throw new ArgumentException("Invalid bounded workflow schedule.");
        ReleaseRevision = releaseRevision; ReleaseHash = releaseHash; Environment = environment;
        CreatedAtUtc = createdAtUtc; DeadlineUtc = deadlineUtc; DueAtUtc = dueAtUtc;
    }

    public byte[] WriteCanonical()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
            for (var i = 0; i < IdNames.Length; i++) writer.WriteString(IdNames[i], _ids[i].ToString("D"));
            writer.WriteNumber("releaseRevision", ReleaseRevision); writer.WriteString("releaseHash", ReleaseHash);
            writer.WriteString("environmentClass", Environment.ToString()); writer.WriteString("templateId", TemplateId);
            writer.WriteNumber("templateVersion", TemplateVersion); writer.WriteString("templateHash", TemplateHash);
            writer.WriteString("createdAtUtc", Time(CreatedAtUtc)); writer.WriteString("deadlineUtc", Time(DeadlineUtc));
            writer.WriteStartArray("steps");
            string[] kinds = ["Checkpoint", "WaitUntil", "Checkpoint", "Complete"];
            for (var i = 0; i < kinds.Length; i++)
            {
                writer.WriteStartObject(); writer.WriteNumber("ordinal", i); writer.WriteString("kind", kinds[i]);
                if (i == 1) writer.WriteString("dueAtUtc", Time(DueAtUtc)); else writer.WriteNull("dueAtUtc");
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }
        if (stream.Length > 64 * 1024) throw new InvalidOperationException("Workflow specification exceeds byte limit.");
        return stream.ToArray();
    }

    public string SpecificationHash => Hash(WriteCanonical());

    /// <summary>Strict persisted-spec reader. Wire HTTP commands are a separate owner contract.</summary>
    public static WorkflowSpecification ReadCanonical(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > 64 * 1024) throw new ArgumentException("Invalid workflow specification.");
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement; Fields(root, RootFields);
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || Text(root, "templateId") != TemplateId ||
                root.GetProperty("templateVersion").GetInt32() != TemplateVersion || Text(root, "templateHash") != TemplateHash)
                throw new ArgumentException();
            var steps = root.GetProperty("steps");
            if (steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() != 4) throw new ArgumentException();
            string[] kinds = ["Checkpoint", "WaitUntil", "Checkpoint", "Complete"];
            for (var i = 0; i < 4; i++)
            {
                var step = steps[i]; Fields(step, ["ordinal", "kind", "dueAtUtc"]);
                if (step.GetProperty("ordinal").GetInt32() != i || Text(step, "kind") != kinds[i] ||
                    i != 1 && step.GetProperty("dueAtUtc").ValueKind != JsonValueKind.Null) throw new ArgumentException();
            }
            var ids = IdNames.Select(name => Id(root, name)).ToArray();
            var environment = Text(root, "environmentClass") switch
            { "Development" => WorkflowEnvironment.Development, "Test" => WorkflowEnvironment.Test, _ => throw new ArgumentException() };
            var result = new WorkflowSpecification(ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6], ids[7], ids[8],
                root.GetProperty("releaseRevision").GetInt64(), Text(root, "releaseHash"), environment,
                ParseTime(Text(root, "createdAtUtc")), ParseTime(Text(root, "deadlineUtc")), ParseTime(Text(steps[1], "dueAtUtc")));
            if (!bytes.Span.SequenceEqual(result.WriteCanonical())) throw new ArgumentException();
            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { throw new ArgumentException("Workflow specification rejected."); }
    }
    private static void Fields(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal)) throw new ArgumentException();
        if (seen.Count != expected.Length) throw new ArgumentException();
    }
    private static string Text(JsonElement value, string name)
    {
        var text = value.GetProperty(name).GetString() ?? throw new ArgumentException();
        if (Encoding.UTF8.GetByteCount(text) > 256) throw new ArgumentException();
        return text;
    }
    private static Guid Id(JsonElement root, string name)
    {
        var text = Text(root, name);
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text) throw new ArgumentException();
        return id;
    }
    private static string Time(DateTimeOffset time) => time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string time) => DateTimeOffset.ParseExact(time, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public override string ToString() => "WorkflowSpecification [private]";
}
