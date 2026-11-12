using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowOperatorRequestParserTests
{
    [Theory]
    [InlineData("BindMatchingCommittedReceipt")]
    [InlineData("ConfirmKnownRollback")]
    public void SelectionsBindOnlyToServerOwnerAndActor(string kind)
    {
        var spec = Specification(); var actor = Guid.NewGuid(); var request = Guid.NewGuid(); var operation = Guid.NewGuid();
        var cancel = WorkflowOperatorRequestParser.ParseCancellation(spec.InstanceId.ToString("D"),
            JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, requestId = request, reasonCode = "OperatorRequested" }), 3);
        var intent = cancel.BindToOwner(spec, actor);
        Assert.Equal(spec.TenantId, intent.TenantId); Assert.Equal(actor, intent.ActorId);
        Assert.Equal(spec.SpecificationHash, intent.SpecificationHash); Assert.Equal(3, intent.ExpectedVersion);
        var reconcile = WorkflowOperatorRequestParser.ParseReconciliation(spec.InstanceId.ToString("D"),
            JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, requestId = request, operationId = operation,
                expectedVersion = 3, kind, evidenceHash = new string('a', 64) }));
        var command = reconcile.BindToOwner(spec, actor);
        Assert.Equal(operation, command.OperationId); Assert.Equal(actor, command.ActorId);
        Assert.Equal(spec.TenantId, command.TenantId); Assert.Equal(spec.SpecificationHash, command.SpecificationHash);
        Assert.Throws<InvalidOperationException>(() => cancel.BindToOwner(Specification(), actor));
        Assert.Throws<InvalidOperationException>(() => reconcile.BindToOwner(Specification(), actor));
        Assert.Throws<ArgumentException>(() => reconcile.BindToOwner(spec, Guid.Empty));
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("actorId")]
    [InlineData("specificationHash")]
    [InlineData("path")]
    [InlineData("replay")]
    public void ExtraAuthorityOrOutcomeFieldsAreRejected(string field)
    {
        var instance = Guid.NewGuid().ToString("D");
        var cancel = Cancel().TrimEnd('}') + ",\"" + field + "\":\"untrusted\"}";
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseCancellation(instance, Bytes(cancel), 1));
        var reconcile = Reconcile().TrimEnd('}') + ",\"" + field + "\":\"untrusted\"}";
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseReconciliation(instance, Bytes(reconcile)));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("request")]
    [InlineData("reason")]
    [InlineData("reason-type")]
    public void CancellationRejectsAmbiguousOrInvalidBodies(string fault)
    {
        var body = Cancel();
        body = fault switch
        {
            "duplicate" => body.TrimEnd('}') + ",\"schemaVersion\":1}",
            "missing" => "{}",
            "schema" => body.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            "request" => body.Replace("\"requestId\":\"" + Request + "\"", "\"requestId\":\"not-a-guid\""),
            "reason" => body.Replace("OperatorRequested", "ForceSuccess"),
            "reason-type" => body.Replace("\"OperatorRequested\"", "null"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseCancellation(Guid.NewGuid().ToString("D"), Bytes(body), 1));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("kind")]
    [InlineData("kind-number")]
    [InlineData("hash")]
    [InlineData("version-zero")]
    [InlineData("version-max")]
    [InlineData("version-string")]
    public void ReconciliationRejectsArbitraryOutcomeAndInvalidIdentity(string fault)
    {
        var body = Reconcile();
        body = fault switch
        {
            "duplicate" => body.TrimEnd('}') + ",\"schemaVersion\":1}",
            "missing" => "{}",
            "kind" => body.Replace("ConfirmKnownRollback", "Succeeded"),
            "kind-number" => body.Replace("\"ConfirmKnownRollback\"", "2"),
            "hash" => body.Replace(new string('a', 64), new string('A', 64)),
            "version-zero" => body.Replace("\"expectedVersion\":1", "\"expectedVersion\":0"),
            "version-max" => body.Replace("\"expectedVersion\":1", "\"expectedVersion\":9223372036854775807"),
            "version-string" => body.Replace("\"expectedVersion\":1", "\"expectedVersion\":\"1\""),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseReconciliation(Guid.NewGuid().ToString("D"), Bytes(body)));
    }

    [Fact]
    public void RouteAndPreconditionAreBoundedWithoutChoosingAnHttpEtagFormat()
    {
        var route = Guid.NewGuid().ToString("D");
        foreach (var version in new[] { 0L, -1L, long.MaxValue })
            Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseCancellation(route, Bytes(Cancel()), version));
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseCancellation("invalid", Bytes(Cancel()), 1));
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseReconciliation("invalid", Bytes(Reconcile())));
        var excessive = new byte[ReleaseRequestParser.MaximumBodyBytes + 1];
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseCancellation(route, excessive, 1));
        Assert.Throws<FormatException>(() => WorkflowOperatorRequestParser.ParseReconciliation(route, excessive));
    }

    private static readonly Guid Request = Guid.Parse("264273f5-a8eb-4a65-b224-5d71f01e168a");
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static string Cancel() => JsonSerializer.Serialize(new { schemaVersion = 1, requestId = Request, reasonCode = "OperatorRequested" });
    private static string Reconcile() => JsonSerializer.Serialize(new { schemaVersion = 1, requestId = Request,
        operationId = Guid.Parse("06a70504-1eac-4bf3-ad3c-16db3f081a66"), expectedVersion = 1,
        kind = "ConfirmKnownRollback", evidenceHash = new string('a', 64) });
    private static WorkflowSpecification Specification()
    {
        var now = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            now, now.AddHours(1), now.AddMinutes(3));
    }
}
