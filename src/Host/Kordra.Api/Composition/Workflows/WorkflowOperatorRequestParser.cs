using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Domain;

namespace Kordra.Api;

/// <summary>Wire selection only. Actor, tenant, project and specification are resolved by trusted owners.</summary>
public sealed record ParsedWorkflowCancellation(Guid InstanceId, Guid RequestId, long ExpectedVersion)
{
    public WorkflowCancellationIntent BindToOwner(WorkflowSpecification specification, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (InstanceId != specification.InstanceId) throw new InvalidOperationException("Cancellation owner selection conflict.");
        return new(specification.InstallationId, specification.TenantId, InstanceId, RequestId, actorId,
            ExpectedVersion, specification.SpecificationHash);
    }
    public override string ToString() => "ParsedWorkflowCancellation [selection only]";
}

public sealed record ParsedWorkflowReconciliation(Guid InstanceId, WorkflowReconciliationSelection Selection)
{
    public WorkflowReconciliationCommand BindToOwner(WorkflowSpecification specification, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (InstanceId != specification.InstanceId) throw new InvalidOperationException("Reconciliation owner selection conflict.");
        return Selection.BindToOwner(specification, actorId);
    }
    public override string ToString() => "ParsedWorkflowReconciliation [selection only]";
}

/// <summary>Inactive DTO decoding. Cancellation receives an already-decoded If-Match version, never chooses its HTTP format.</summary>
public static class WorkflowOperatorRequestParser
{
    public static ParsedWorkflowCancellation ParseCancellation(string instance, ReadOnlyMemory<byte> body, long expectedVersion)
    {
        if (!ReleaseRequestParser.GuidValue(instance, out var instanceId) || expectedVersion is < 1 or long.MaxValue)
            throw Invalid();
        using var document = Read(body, ["schemaVersion", "requestId", "reasonCode"]);
        var root = document.RootElement;
        if (!ReleaseRequestParser.GuidValue(Text(root, "requestId"), out var requestId) ||
            Text(root, "reasonCode") != WorkflowCancellationIntent.ReasonCode) throw Invalid();
        return new(instanceId, requestId, expectedVersion);
    }

    public static ParsedWorkflowReconciliation ParseReconciliation(string instance, ReadOnlyMemory<byte> body)
    {
        if (!ReleaseRequestParser.GuidValue(instance, out var instanceId)) throw Invalid();
        using var document = Read(body, ["schemaVersion", "requestId", "operationId", "expectedVersion", "kind", "evidenceHash"]);
        var root = document.RootElement;
        if (!ReleaseRequestParser.GuidValue(Text(root, "requestId"), out var requestId) ||
            !ReleaseRequestParser.GuidValue(Text(root, "operationId"), out var operationId) ||
            root.GetProperty("expectedVersion").ValueKind != JsonValueKind.Number ||
            !root.GetProperty("expectedVersion").TryGetInt64(out var version) || version is < 1 or long.MaxValue)
            throw Invalid();
        var kind = Text(root, "kind") switch
        {
            nameof(WorkflowReconciliationKind.BindMatchingCommittedReceipt) => WorkflowReconciliationKind.BindMatchingCommittedReceipt,
            nameof(WorkflowReconciliationKind.ConfirmKnownRollback) => WorkflowReconciliationKind.ConfirmKnownRollback,
            _ => throw Invalid()
        };
        var selection = new WorkflowReconciliationSelection(requestId, operationId, version, kind, Text(root, "evidenceHash"));
        if (!selection.IsValid) throw Invalid();
        return new(instanceId, selection);
    }

    private static JsonDocument Read(ReadOnlyMemory<byte> body, string[] fields)
    {
        if (body.Length > ReleaseRequestParser.MaximumBodyBytes) throw Invalid();
        var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 2 });
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
            if (seen.Count != fields.Length || root.GetProperty("schemaVersion").GetRawText() != "1") throw Invalid();
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    private static string Text(JsonElement root, string name) =>
        root.GetProperty(name).ValueKind == JsonValueKind.String ? root.GetProperty(name).GetString()! : throw Invalid();
    private static FormatException Invalid() => new("Invalid workflow operator request.");
}
