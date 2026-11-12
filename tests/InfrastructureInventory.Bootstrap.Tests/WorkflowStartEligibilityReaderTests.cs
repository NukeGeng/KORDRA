using Kordra.Api;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowStartEligibilityReaderTests
{
    [Theory]
    [InlineData("tenant")]
    [InlineData("environment")]
    [InlineData("revision-id")]
    [InlineData("revision-number")]
    [InlineData("hash")]
    [InlineData("template")]
    [InlineData("template-version")]
    public async Task InvalidStartSelectionIsRejectedBeforeReadingOwners(string fault)
    {
        var selection = new WorkflowStartSelection(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1, new string('a', 64), WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion);
        selection = fault switch
        {
            "tenant" => selection with { TenantId = Guid.Empty },
            "environment" => selection with { EnvironmentId = Guid.Empty },
            "revision-id" => selection with { ReleaseRevisionId = Guid.Empty },
            "revision-number" => selection with { ReleaseRevision = 0 },
            "hash" => selection with { ReleaseHash = new string('A', 64) },
            "template" => selection with { TemplateId = "deploy" },
            "template-version" => selection with { TemplateVersion = 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var clock = new ProbeClock();
        var reader = new WorkflowStartEligibilityReader(Guid.NewGuid(), null!, null!, null!, null!, null!, clock);
        var result = await reader.ReadUnderAdmissionAsync(new(Guid.NewGuid(), "synthetic", Guid.NewGuid()),
            selection, Guid.NewGuid());
        Assert.Null(result);
        Assert.Equal(0, clock.Reads);
    }

    private sealed class ProbeClock : IAuthorityClock
    {
        public int Reads { get; private set; }

        public DateTimeOffset GetTrustedUtcNow()
        {
            Reads++;
            return DateTimeOffset.UtcNow;
        }
    }
}
