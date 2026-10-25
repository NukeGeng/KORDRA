using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;
using Xunit;

namespace Kordra.Modules.InfrastructureInventory.Bootstrap.Tests;

public sealed class KubernetesProjectionTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static JsonObject Item(string kind = "Pod") => JsonNode.Parse("""
        {"apiVersion":"v1","kind":"Pod","metadata":{"name":"sample","namespace":"default","uid":"uid-1","generation":7,"labels":{"secret":"CANARY"}},
         "spec":{"containers":[{"name":"main","image":"example.test/team/app:v1@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","env":[{"name":"SECRET","value":"CANARY"}]}]},
         "status":{"phase":"Running","conditions":[{"type":"Ready","status":"True","message":"CANARY"}],"containerStatuses":[{"name":"main","imageID":"containerd://sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}],"podIP":"CANARY"}}
        """)!.AsObject();
    private static JsonObject List(JsonObject? item = null) => new() { ["apiVersion"] = "v1", ["kind"] = "PodList", ["metadata"] = new JsonObject { ["resourceVersion"] = "opaque-version" }, ["items"] = new JsonArray(item ?? Item()) };
    private static Kordra.Modules.InfrastructureInventory.Contracts.KubernetesResourceSetV1 Project(JsonObject list, string kind = "Pod") =>
        KubernetesWorkloadProjection.Project(kind, JsonSerializer.SerializeToUtf8Bytes(list), "default");
    [Fact]
    public void Pod_facts_and_images_are_allowlisted_and_never_include_raw_provider_fields()
    {
        var set = Project(List()); var item = Assert.Single(set.Items); var image = Assert.Single(item.Images);
        Assert.True(item.PodReady); Assert.Equal("Running", item.PodPhase); Assert.Null(item.DesiredReplicas);
        Assert.Equal(Digest, image.DeclaredDigest); Assert.Equal(Digest, image.ReportedRuntimeDigest);
        var wire = JsonSerializer.Serialize(set); Assert.DoesNotContain("CANARY", wire); Assert.DoesNotContain("imageID", wire);
    }
    [Theory]
    [InlineData("Deployment")][InlineData("StatefulSet")][InlineData("DaemonSet")]
    public void Controllers_report_counts_and_never_invent_Pod_facts(string kind)
    {
        var item = Item(); item["kind"] = kind; item["apiVersion"] = "apps/v1";
        item["spec"] = new JsonObject { ["replicas"] = 3, ["template"] = new JsonObject { ["spec"] = Item()["spec"]!.DeepClone() } };
        item["status"] = new JsonObject { ["observedGeneration"] = 6, ["readyReplicas"] = 2, ["availableReplicas"] = 1, ["desiredNumberScheduled"] = 3, ["numberReady"] = 2, ["numberAvailable"] = 1 };
        var list = List(item); list["kind"] = kind + "List"; list["apiVersion"] = "apps/v1";
        var projected = Assert.Single(Project(list,kind).Items);
        Assert.Equal(3,projected.DesiredReplicas); Assert.Equal(2,projected.ReadyReplicas); Assert.Equal(1,projected.AvailableReplicas);
        Assert.Equal(6L,projected.ObservedGeneration); Assert.Null(projected.PodPhase); Assert.Null(projected.PodReady); Assert.Null(Assert.Single(projected.Images).ReportedRuntimeDigest);
    }
    [Theory]
    [InlineData("https://user:password@registry.test/repo:tag")][InlineData("repo:tag\n")][InlineData("repo:tag?token=CANARY")][InlineData("repo@md5:123")][InlineData("not a reference")]
    public void Unsupported_image_evidence_is_null_without_returning_raw_input(string reference)
    {
        var item = Item(); item["spec"]!["containers"]![0]!["image"] = reference; item["status"]!["containerStatuses"]![0]!["imageID"] = "opaque-untrusted-runtime";
        var image = Assert.Single(Assert.Single(Project(List(item)).Items).Images);
        Assert.Null(image.ReportedReference); Assert.Null(image.DeclaredDigest); Assert.Null(image.ReportedRuntimeDigest);
    }
    [Theory]
    [InlineData("identity")][InlineData("namespace")][InlineData("kind")][InlineData("version")][InlineData("name")][InlineData("count")][InlineData("ready")][InlineData("duplicate-item")][InlineData("duplicate-image")][InlineData("too-many-images")][InlineData("too-many-items")][InlineData("long-image")]
    public void Invalid_projected_fields_fail_instead_of_truncating_or_merging(string fault)
    {
        var item = Item(); var list = List(item);
        switch(fault)
        {
            case "identity": item["metadata"]!["uid"] = "wrong uid"; break;
            case "namespace": item["metadata"]!["namespace"] = "foreign"; break;
            case "kind": item["kind"] = "Secret"; break;
            case "version": list["apiVersion"] = "unknown/v1"; break;
            case "name": item["metadata"]!["name"] = "a..b"; break;
            case "count": item["metadata"]!["generation"] = -1; break;
            case "ready": item["status"]!["conditions"]!.AsArray().Add(item["status"]!["conditions"]![0]!.DeepClone()); break;
            case "duplicate-item": list["items"]!.AsArray().Add(item.DeepClone()); break;
            case "duplicate-image": item["spec"]!["containers"]!.AsArray().Add(item["spec"]!["containers"]![0]!.DeepClone()); break;
            case "too-many-images": for(var i=0;i<16;i++)item["spec"]!["containers"]!.AsArray().Add(new JsonObject{["name"]="c"+i,["image"]="repo:v1"}); break;
            case "too-many-items": for(var i=0;i<25;i++)list["items"]!.AsArray().Add(item.DeepClone()); break;
            case "long-image": item["spec"]!["containers"]![0]!["image"] = new string('a',513); break;
        }
        Assert.ThrowsAny<Exception>(()=>Project(list));
    }
    [Fact]
    public void Duplicate_unknown_JSON_and_depth_limits_are_enforced_and_missing_facts_remain_null()
    {
        foreach(var text in new[]{"{\"unknown\":1,\"unknown\":2}",new string('[',33)+new string(']',33)})
            Assert.ThrowsAny<Exception>(()=>KubernetesWorkloadProjection.Project("Pod",Encoding.UTF8.GetBytes(text),"default"));
        var item=Item(); item.Remove("spec");item.Remove("status");item["metadata"]!.AsObject().Remove("generation");
        var x=Assert.Single(Project(List(item)).Items);Assert.Empty(x.Images);Assert.Null(x.Generation);Assert.Null(x.PodReady);Assert.Null(x.PodPhase);
        var list=List();list["metadata"]!["continue"]="NEVER_RETURN_THIS";Assert.Equal("partial",Project(list).Coverage);Assert.DoesNotContain("NEVER_RETURN_THIS",JsonSerializer.Serialize(Project(list)));
    }
    [Theory]
    [InlineData("Pod",64)][InlineData("Pod",253)]
    [InlineData("Deployment",64)][InlineData("Deployment",253)]
    [InlineData("StatefulSet",64)][InlineData("StatefulSet",253)]
    [InlineData("DaemonSet",64)][InlineData("DaemonSet",253)]
    public void Workload_subdomains_accept_long_segments_within_the_total_limit(string kind, int length)
    {
        var item=Item();var name=new string('a',length);item["metadata"]!["name"]=name;
        item["kind"]=kind;item["apiVersion"]=kind=="Pod"?"v1":"apps/v1";
        var list=List(item);list["kind"]=kind+"List";list["apiVersion"]=kind=="Pod"?"v1":"apps/v1";
        Assert.Equal(name,Assert.Single(Project(list,kind).Items).Name);
    }
    [Fact]
    public void Workload_subdomains_allow_long_segments_separated_by_dots_but_cap_total_length()
    {
        var item=Item();var name=new string('a',64)+"."+new string('b',188);item["metadata"]!["name"]=name;
        Assert.Equal(253,name.Length);Assert.Equal(name,Assert.Single(Project(List(item)).Items).Name);
        var tooLong=Item();tooLong["metadata"]!["name"]=name+"c";
        Assert.Throws<InvalidDataException>(()=>Project(List(tooLong)));
    }
    [Theory]
    [InlineData("a..b")][InlineData("a.-b")][InlineData("a-.b")][InlineData(".a")][InlineData("a.")]
    [InlineData("UPPER")][InlineData("under_score")][InlineData("slash/name")][InlineData("")]
    public void Workload_subdomains_still_reject_malformed_segments(string name)
    {
        var item=Item();item["metadata"]!["name"]=name;
        Assert.Throws<InvalidDataException>(()=>Project(List(item)));
    }
    [Theory]
    [InlineData(63)][InlineData(64)]
    public void Container_names_retain_the_separate_63_character_label_limit(int length)
    {
        var item=Item();item["spec"]!["containers"]![0]!["name"]=new string('a',length);
        if(length==64)Assert.Throws<InvalidDataException>(()=>Project(List(item)));
        else Assert.Equal(length,Assert.Single(Assert.Single(Project(List(item)).Items).Images).ContainerName.Length);
    }

}
