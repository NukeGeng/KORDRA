namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>Immutable IAM policy revision; contains no tenant/resource authority or credential.</summary>
public sealed class KordraRoleDefinition
{
    public Guid DefinitionId { get; set; }
    public long Revision { get; set; }
    public Guid ManifestId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RulesJson { get; set; } = "[]";
    public bool IsRetired { get; set; }
}
