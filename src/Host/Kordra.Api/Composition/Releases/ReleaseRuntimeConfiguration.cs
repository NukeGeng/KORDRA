using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;

namespace Kordra.Api;

/// <summary>Explicit paired runtime locators. Omission leaves existing installations unchanged.</summary>
public sealed class ReleaseRuntimeConfiguration
{
    public const string ReleaseVariable = "KORDRA_RELEASE_CONNECTION_STRING";
    public const string ApprovalVariable = "KORDRA_APPROVAL_CONNECTION_STRING";
    private ReleaseRuntimeConfiguration(string? release, string? approval) => (ReleaseLocator, ApprovalLocator) = (release, approval);
    public string? ReleaseLocator { get; }
    public string? ApprovalLocator { get; }
    public override string ToString() => "ReleaseRuntimeConfiguration [private]";
    public static ReleaseRuntimeConfiguration LoadEnvironment() => Load(Environment.GetEnvironmentVariable(ReleaseVariable), Environment.GetEnvironmentVariable(ApprovalVariable));
    public static ReleaseRuntimeConfiguration Load(string? release, string? approval)
    {
        if ((release is null) != (approval is null)) throw new InvalidOperationException("Release activation requires both private locators.");
        if (release is not null) { _ = ReleaseDbContextFactory.CreateRuntimeOptions(release); _ = ApprovalDbContextFactory.CreateRuntimeOptions(approval!); }
        return new(release, approval);
    }
}
