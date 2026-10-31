namespace Kordra.Modules.Messaging.Contracts;

/// <summary>Claimed message scope, never an authorization decision or transferable permit.</summary>
public sealed record MessageScope
{
    public Guid? TenantId { get; }
    public string Kind => TenantId.HasValue ? "tenant" : "system";
    private MessageScope(Guid? tenantId) => TenantId = tenantId;
    public static MessageScope Tenant(Guid tenantId) => tenantId != Guid.Empty
        ? new(tenantId) : throw new ArgumentException("Nonempty tenant identity required.", nameof(tenantId));
    public static MessageScope System() => new((Guid?)null);
}

/// <summary>Logical identity deliberately excludes delivery IDs, traces, retry counts and payload hashes.</summary>
public sealed record MessageIdentity
{
    public Guid InstallationId { get; }
    public MessageScope Scope { get; }
    public Guid OperationId { get; }
    public Guid StepId { get; }
    public long StepVersion { get; }
    public string TypeId { get; }

    public MessageIdentity(Guid installationId, MessageScope scope, Guid operationId, Guid stepId, long stepVersion, string typeId)
    {
        if (installationId == Guid.Empty || operationId == Guid.Empty || stepId == Guid.Empty || stepVersion <= 0)
            throw new ArgumentException("Nonempty message identity and positive step version required.");
        ArgumentNullException.ThrowIfNull(scope);
        ValidateTypeId(typeId);
        InstallationId = installationId; Scope = scope; OperationId = operationId; StepId = stepId;
        StepVersion = stepVersion; TypeId = typeId;
    }

    internal static void ValidateTypeId(string typeId)
    {
        if (string.IsNullOrEmpty(typeId) || typeId.Length > 256 || typeId[0] is < 'a' or > 'z' ||
            typeId.EndsWith('.') || typeId.Contains("..", StringComparison.Ordinal) ||
            typeId.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            throw new ArgumentException("Invalid registered message type identifier.", nameof(typeId));
    }

    public override string ToString() => "MessageIdentity [scope-bound]";
}
