namespace Kordra.Modules.Messaging.Contracts;

public sealed record MessageResource
{
    public string Kind { get; }
    public Guid Id { get; }
    public MessageResource(string kind, Guid id)
    {
        MessageIdentity.ValidateTypeId(kind);
        if (id == Guid.Empty) throw new ArgumentException("Nonempty resource identity required.", nameof(id));
        Kind = kind; Id = id;
    }
}

/// <summary>Original immutable business binding. Delivery and tracing metadata are intentionally absent.</summary>
public sealed record MessageBinding
{
    public MessageIdentity Identity { get; }
    public int TypeSchemaVersion { get; }
    public MessageResource? Resource { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }
    public MessageBinding(MessageIdentity identity, int typeSchemaVersion, MessageResource? resource,
        DateTimeOffset occurredAtUtc, DateTimeOffset deadlineUtc)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (typeSchemaVersion < 1 || occurredAtUtc == default || deadlineUtc <= occurredAtUtc || deadlineUtc - occurredAtUtc > TimeSpan.FromSeconds(120))
            throw new ArgumentException("Invalid message schema or original deadline.");
        Identity = identity; TypeSchemaVersion = typeSchemaVersion; Resource = resource;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime(); DeadlineUtc = deadlineUtc.ToUniversalTime();
    }
    public override string ToString() => "MessageBinding [private metadata]";
}
