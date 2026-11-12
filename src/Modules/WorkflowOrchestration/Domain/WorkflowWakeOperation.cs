using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>Immutable recovery/publication data only. Never current authority or replay eligibility.</summary>
public sealed class WorkflowWakeOperation
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly string[] Fields = ["schemaVersion", "installationId", "tenantId", "instanceId", "requestId", "stepId",
        "timerId", "generation", "schedulerAttempt", "custodyEpoch", "schedulerOperationId", "wakeOperationId", "messageId",
        "expectedVersion", "stepVersion", "specHash", "templateHash", "occurredAtUtc", "deadlineUtc"];

    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid InstanceId { get; }
    public Guid RequestId { get; }
    public Guid StepId { get; }
    public Guid TimerId { get; }
    public long Generation { get; }
    public int SchedulerAttempt { get; }
    public long CustodyEpoch { get; }
    public Guid SchedulerOperationId { get; }
    public Guid WakeOperationId { get; }
    public Guid MessageId { get; }
    public long ExpectedWorkflowVersion { get; }
    public long StepVersion { get; }
    public string SpecificationHash { get; }
    public string TemplateHash { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }

    public WorkflowWakeOperation(WorkflowSpecification specification, Guid stepId, Guid timerId, long generation,
        int schedulerAttempt, long custodyEpoch, Guid schedulerOperationId, Guid wakeOperationId, Guid messageId,
        long expectedWorkflowVersion, long stepVersion, DateTimeOffset occurredAtUtc, DateTimeOffset deadlineUtc)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (stepId == Guid.Empty || timerId == Guid.Empty || schedulerOperationId == Guid.Empty ||
            wakeOperationId == Guid.Empty || messageId == Guid.Empty || schedulerOperationId == wakeOperationId ||
            generation < 1 || custodyEpoch < 1 || schedulerAttempt is < 1 or > WorkflowTimingRules.MaximumSchedulerAttempts ||
            expectedWorkflowVersion < 1 || stepVersion < 1)
            throw new ArgumentException("Invalid immutable workflow wake binding.");
        occurredAtUtc = occurredAtUtc.ToUniversalTime(); deadlineUtc = deadlineUtc.ToUniversalTime();
        var allowed = WorkflowTimingRules.DueWakeWindow(specification, occurredAtUtc);
        if (deadlineUtc <= occurredAtUtc || deadlineUtc > allowed.DeadlineUtc)
            throw new ArgumentException("Workflow wake must preserve the bounded original deadline.");
        InstallationId = specification.InstallationId; TenantId = specification.TenantId; InstanceId = specification.InstanceId;
        RequestId = specification.RequestId; CustodyEpoch = custodyEpoch;
        StepId = stepId; TimerId = timerId; Generation = generation; SchedulerAttempt = schedulerAttempt;
        SchedulerOperationId = schedulerOperationId; WakeOperationId = wakeOperationId; MessageId = messageId;
        ExpectedWorkflowVersion = expectedWorkflowVersion; StepVersion = stepVersion;
        SpecificationHash = specification.SpecificationHash; TemplateHash = WorkflowSpecification.TemplateHash;
        OccurredAtUtc = occurredAtUtc; DeadlineUtc = deadlineUtc;
    }

    public byte[] WriteCanonical()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("installationId", InstallationId.ToString("D")); writer.WriteString("tenantId", TenantId.ToString("D"));
            writer.WriteString("instanceId", InstanceId.ToString("D")); writer.WriteString("requestId", RequestId.ToString("D")); writer.WriteString("stepId", StepId.ToString("D"));
            writer.WriteString("timerId", TimerId.ToString("D")); writer.WriteNumber("generation", Generation);
            writer.WriteNumber("schedulerAttempt", SchedulerAttempt); writer.WriteNumber("custodyEpoch", CustodyEpoch); writer.WriteString("schedulerOperationId", SchedulerOperationId.ToString("D"));
            writer.WriteString("wakeOperationId", WakeOperationId.ToString("D")); writer.WriteString("messageId", MessageId.ToString("D"));
            writer.WriteNumber("expectedVersion", ExpectedWorkflowVersion); writer.WriteNumber("stepVersion", StepVersion);
            writer.WriteString("specHash", SpecificationHash); writer.WriteString("templateHash", TemplateHash);
            writer.WriteString("occurredAtUtc", Time(OccurredAtUtc)); writer.WriteString("deadlineUtc", Time(DeadlineUtc));
            writer.WriteEndObject(); writer.Flush();
        }
        return stream.ToArray();
    }

    public string DescriptorHash => Convert.ToHexStringLower(SHA256.HashData(WriteCanonical()));

    /// <summary>Original occurrence/deadline is checked against the immutable spec, never refreshed to today's clock.</summary>
    public static WorkflowWakeOperation ReadCanonical(ReadOnlyMemory<byte> bytes, WorkflowSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (bytes.Length is < 2 or > 16 * 1024) throw Rejected();
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Rejected();
            var names = root.EnumerateObject().Select(x => x.Name).ToArray();
            if (names.Length != Fields.Length || names.Distinct(StringComparer.Ordinal).Count() != Fields.Length ||
                names.Any(x => !Fields.Contains(x, StringComparer.Ordinal)) || root.GetProperty("schemaVersion").GetInt32() != 1 ||
                Id(root, "installationId") != specification.InstallationId || Id(root, "tenantId") != specification.TenantId ||
                Id(root, "instanceId") != specification.InstanceId || Id(root, "requestId") != specification.RequestId || root.GetProperty("specHash").GetString() != specification.SpecificationHash ||
                root.GetProperty("templateHash").GetString() != WorkflowSpecification.TemplateHash)
                throw Rejected();
            var value = new WorkflowWakeOperation(specification, Id(root, "stepId"), Id(root, "timerId"), root.GetProperty("generation").GetInt64(),
                root.GetProperty("schedulerAttempt").GetInt32(), root.GetProperty("custodyEpoch").GetInt64(), Id(root, "schedulerOperationId"), Id(root, "wakeOperationId"), Id(root, "messageId"),
                root.GetProperty("expectedVersion").GetInt64(), root.GetProperty("stepVersion").GetInt64(),
                ParseTime(root.GetProperty("occurredAtUtc").GetString()!), ParseTime(root.GetProperty("deadlineUtc").GetString()!));
            if (!bytes.Span.SequenceEqual(value.WriteCanonical())) throw Rejected();
            return value;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or FormatException or OverflowException)
        { throw Rejected(); }
    }

    private static Guid Id(JsonElement root, string field)
    {
        var text = root.GetProperty(field).GetString();
        if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty || value.ToString("D") != text) throw Rejected();
        return value;
    }
    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.ParseExact(value, TimeFormat,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    private static ArgumentException Rejected() => new("Workflow wake descriptor rejected.");
    public override string ToString() => "WorkflowWakeOperation [private binding, not authority]";
}
