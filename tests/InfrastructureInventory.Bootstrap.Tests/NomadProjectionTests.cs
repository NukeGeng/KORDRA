using System.Text;
using System.Text.Json;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;
using Xunit;

namespace Kordra.Modules.InfrastructureInventory.Bootstrap.Tests;

public sealed class NomadProjectionTests
{
    [Fact]
    public void Projection_sorts_and_excludes_every_non_allowlisted_secret_field()
    {
        var bytes=Encoding.UTF8.GetBytes("[{\"ID\":\"z\",\"Name\":\"z\",\"Namespace\":\"default\",\"Type\":\"future\",\"Env\":{\"TOKEN\":\"SECRET_CANARY\"}},{\"ID\":\"a\",\"Name\":\"a\",\"Namespace\":\"default\",\"Type\":\"service\",\"Status\":\"running\"}]");
        var r=NomadWorkloadProjection.Project("Jobs",bytes,"default","1",false);
        var json=JsonSerializer.Serialize(r,new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.DoesNotContain("SECRET_CANARY",json);Assert.DoesNotContain("$type",json);
        using var doc=JsonDocument.Parse(json);var items=doc.RootElement.GetProperty("items");Assert.Equal("a",items[0].GetProperty("id").GetString());
        Assert.Equal(new[]{"id","name","type","status"},items[0].EnumerateObject().Select(x=>x.Name));Assert.Equal(JsonValueKind.Null,items[1].GetProperty("type").ValueKind);
    }
    [Theory]
    [InlineData("[{\"ID\":\"x\",\"Name\":\"x\",\"Namespace\":\"foreign\"}]")]
    [InlineData("[{\"ID\":\"x\",\"Name\":\"x\",\"Namespace\":\"default\",\"Meta\":{\"x\":1,\"x\":2}}]")]
    [InlineData("null")][InlineData("{}")][InlineData("[]{}")][InlineData("[{\"ID\":\"x\"}]")]
    public void Malformed_duplicate_foreign_or_incomplete_records_are_not_disclosed(string json)=>Assert.ThrowsAny<Exception>(()=>NomadWorkloadProjection.Project("Jobs",Encoding.UTF8.GetBytes(json),"default","1",false));
    [Fact]
    public void Allocation_projection_has_exact_fields_and_unknown_status_is_null()
    {
        var json="[{\"ID\":\"00000001-0000-0000-0000-000000000000\",\"Namespace\":\"default\",\"JobID\":\"x\",\"TaskGroup\":\"g\",\"ClientStatus\":\"future\",\"DesiredStatus\":\"run\",\"NodeID\":\"SECRET_CANARY\"}]";
        var r=NomadWorkloadProjection.Project("Allocations",Encoding.UTF8.GetBytes(json),"default","18446744073709551615",false);
        var output=JsonSerializer.Serialize(r,new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.DoesNotContain("SECRET_CANARY",output);using var doc=JsonDocument.Parse(output);
        var item=doc.RootElement.GetProperty("items")[0];Assert.Equal(new[]{"id","jobId","taskGroup","clientStatus","desiredStatus"},item.EnumerateObject().Select(x=>x.Name));Assert.Equal(JsonValueKind.Null,item.GetProperty("clientStatus").ValueKind);
    }
    [Fact]
    public void Duplicate_ids_oversized_lists_and_invalid_indexes_are_rejected()
    {
        var item="{\"ID\":\"x\",\"Name\":\"x\",\"Namespace\":\"default\"}";
        foreach(var json in new[]{"["+item+","+item+"]","["+string.Join(',',Enumerable.Repeat(item,26))+"]"})
            Assert.ThrowsAny<Exception>(()=>NomadWorkloadProjection.Project("Jobs",Encoding.UTF8.GetBytes(json),"default","1",false));
        Assert.ThrowsAny<Exception>(()=>NomadWorkloadProjection.Project("Jobs","[]"u8.ToArray(),"default","01",false));
    }
}
