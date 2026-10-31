namespace Kordra.Modules.Messaging.Contracts;

public enum MessageScopeKind { Tenant, System }

/// <summary>Trusted registration metadata, not a wire-supplied CLR type or routing destination.</summary>
public sealed record MessageTypeRegistration
{
    public string TypeId { get; }
    public int SchemaVersion { get; }
    public MessageScopeKind ScopeKind { get; }
    public bool RequiresResource { get; }
    public MessageTypeRegistration(string typeId, int schemaVersion, MessageScopeKind scopeKind, bool requiresResource)
    {
        MessageIdentity.ValidateTypeId(typeId);
        if (schemaVersion < 1 || !Enum.IsDefined(scopeKind)) throw new ArgumentException("Invalid registration metadata.");
        TypeId = typeId; SchemaVersion = schemaVersion; ScopeKind = scopeKind; RequiresResource = requiresResource;
    }
}

/// <summary>Immutable exact-version registry. Empty is the safe default for unactivated business ingress.</summary>
public sealed class MessageTypeRegistry
{
    private readonly Dictionary<(string, int), MessageTypeRegistration> _registrations = [];
    public static MessageTypeRegistry Empty { get; } = new([]);
    public MessageTypeRegistry(IEnumerable<MessageTypeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (_registrations.Count >= 256 || !_registrations.TryAdd((registration.TypeId, registration.SchemaVersion), registration))
                throw new ArgumentException("Duplicate or excessive message registrations.", nameof(registrations));
        }
    }

    public bool TryResolve(string typeId, int schemaVersion, MessageScope scope, bool hasResource,
        out MessageTypeRegistration? registration)
    {
        registration = null;
        if (scope is null || typeId is null || !_registrations.TryGetValue((typeId, schemaVersion), out var candidate)) return false;
        if (candidate.ScopeKind == MessageScopeKind.Tenant != scope.TenantId.HasValue || candidate.RequiresResource && !hasResource) return false;
        registration = candidate; return true;
    }
}
