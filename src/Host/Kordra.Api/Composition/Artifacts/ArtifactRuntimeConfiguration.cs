using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;

namespace Kordra.Api;

/// <summary>Private activation only. Omission preserves existing installations; partial input never widens access.</summary>
public sealed class ArtifactRuntimeConfiguration
{
    public const string ConnectionStringVariable = "KORDRA_ARTIFACT_CONNECTION_STRING";
    public const string ConfigurationFileVariable = "KORDRA_ARTIFACT_REGISTRY_CONFIGURATION_FILE";
    public const string ConfigurationHashVariable = "KORDRA_ARTIFACT_REGISTRY_CONFIGURATION_SHA256";
    private ArtifactRuntimeConfiguration(string? locator, RegistryTrustConfiguration? registries)
    { ConnectionString = locator; Registries = registries; }
    public string? ConnectionString { get; }
    public RegistryTrustConfiguration? Registries { get; }
    public override string ToString() => "ArtifactRuntimeConfiguration [private]";
    public static ArtifactRuntimeConfiguration LoadEnvironment(Guid installation) => Load(installation,
        Environment.GetEnvironmentVariable(ConnectionStringVariable), Environment.GetEnvironmentVariable(ConfigurationFileVariable),
        Environment.GetEnvironmentVariable(ConfigurationHashVariable));
    public static ArtifactRuntimeConfiguration Load(Guid installation, string? locator, string? path, string? hash)
    {
        if (installation == Guid.Empty || (path is null) != (hash is null) || path is not null && locator is null)
            throw new InvalidOperationException("Artifact activation is incomplete.");
        if (locator is not null) _ = ArtifactDbContextFactory.CreateRuntimeOptions(locator);
        return new(locator, path is null ? null : RegistryTrustConfiguration.Load(path, hash!, installation));
    }
}
