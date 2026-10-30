using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ArchitectureTests
{
    private const string Credentials = "src/Modules/ConnectionManagement/Kordra.Modules.ConnectionManagement.csproj";
    private const string CredentialContracts = "src/Modules/ConnectionManagement/Contracts/Kordra.Modules.ConnectionManagement.Contracts.csproj";
    private const string CredentialTests = "tests/ConnectionManagement.Tests/ConnectionManagement.Tests.csproj";
    private const string Contracts = "src/Modules/InfrastructureInventory/Contracts/Kordra.Modules.InfrastructureInventory.Contracts.csproj";
    private const string Inventory = "src/Modules/InfrastructureInventory/Kordra.Modules.InfrastructureInventory.csproj";
    private const string Host = "src/Host/Kordra.Api/Kordra.Api.csproj";
    private const string ReleaseContracts = "src/Modules/ReleaseManagement/Contracts/Kordra.Modules.ReleaseManagement.Contracts.csproj";
    private const string ReleaseRuntime = "src/Modules/ReleaseManagement/Kordra.Modules.ReleaseManagement.csproj";
    private const string ReleaseDomain = "src/Modules/ReleaseManagement/Domain/Kordra.ReleaseManagement.Domain.csproj";
    private const string ReleaseRuntimeTests = "tests/ReleaseManagement.Tests/ReleaseManagement.Tests.csproj";
    private const string ReleaseDomainTests = "tests/ReleaseManagement.Domain.Tests/ReleaseManagement.Domain.Tests.csproj";
    private const string ApprovalContracts = "src/Modules/ApprovalGovernance/Contracts/Kordra.Modules.ApprovalGovernance.Contracts.csproj";
    private const string ApprovalRuntime = "src/Modules/ApprovalGovernance/Kordra.Modules.ApprovalGovernance.csproj";
    private const string ApprovalDomain = "src/Modules/ApprovalGovernance/Domain/Kordra.ApprovalGovernance.Domain.csproj";
    private const string ApprovalRuntimeTests = "tests/ApprovalGovernance.Tests/ApprovalGovernance.Tests.csproj";
    private const string ApprovalDomainTests = "tests/ApprovalGovernance.Domain.Tests/ApprovalGovernance.Domain.Tests.csproj";
    private const string Artifact = "src/Modules/ArtifactManagement/Domain/Kordra.ArtifactManagement.Domain.csproj";
    private const string ArtifactRuntime = "src/Modules/ArtifactManagement/Kordra.Modules.ArtifactManagement.csproj";
    private const string ArtifactContracts = "src/Modules/ArtifactManagement/Contracts/Kordra.Modules.ArtifactManagement.Contracts.csproj";
    private const string ArtifactRuntimeTests = "tests/ArtifactManagement.Tests/ArtifactManagement.Tests.csproj";
    private const string IdentityAccess = "src/Modules/IdentityAccess/Kordra.Modules.IdentityAccess.csproj";
    private const string ProjectContracts = "src/Modules/ProjectEnvironment/Contracts/Kordra.Modules.ProjectEnvironment.Contracts.csproj";
    private const string ProjectEnvironment = "src/Modules/ProjectEnvironment/Kordra.Modules.ProjectEnvironment.csproj";
    private const string ProjectTests = "tests/ProjectEnvironment.Tests/ProjectEnvironment.Tests.csproj";
    private const string IdentityContracts = "src/Modules/IdentityAccess/Contracts/Kordra.Modules.IdentityAccess.Contracts.csproj";
    private const string TenantContracts = "src/Modules/TenantManagement/Contracts/Kordra.Modules.TenantManagement.Contracts.csproj";
    private const string TenantManagement = "src/Modules/TenantManagement/Kordra.Modules.TenantManagement.csproj";
    private const string TenantTests = "tests/TenantManagement.Tests/TenantManagement.Tests.csproj";
    private const string AuditContracts = "src/Modules/AuditCompliance/Contracts/Kordra.Modules.AuditCompliance.Contracts.csproj";
    private const string AuditCompliance = "src/Modules/AuditCompliance/Kordra.Modules.AuditCompliance.csproj";
    private const string Security = "src/BuildingBlocks/Security/Kordra.BuildingBlocks.Security.csproj";
    private const string DbMigrator = "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj";
    private const string BootstrapTests = "tests/InfrastructureInventory.Bootstrap.Tests/InfrastructureInventory.Bootstrap.Tests.csproj";
    private const string ArtifactTests = "tests/ArtifactManagement.Domain.Tests/ArtifactManagement.Domain.Tests.csproj";
    private const string IdentityTests = "tests/IdentityAccess.Tests/IdentityAccess.Tests.csproj";
    private const string AuditTests = "tests/AuditCompliance.Tests/AuditCompliance.Tests.csproj";
    private const string MessagingSpikeTests = "tests/Messaging.Durability.Tests/Messaging.Durability.Tests.csproj";

    [Fact]
    public void ProductProjects_EnforceActualXmlGraphAndRestoredDependencies()
    {
        var root = RepositoryRoot();
        var graph = ApprovedGraph(root);
        foreach (var (project, references) in graph)
        {
            Assert.Empty(ProjectViolations(project, references, ApprovedPackages(root, project)));
            AssertRestoredGraph(project, references);
        }
        Assert.False(HasCycle(graph.Keys.ToDictionary(project => project, ReadProjectEdges, StringComparer.Ordinal)));
        foreach (var project in graph.Keys)
        {
            Assert.Equal(project == Absolute(root, Host) ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk",
                (string?)XDocument.Load(project).Root!.Attribute("Sdk"));
        }

        var solution = XDocument.Load(Path.Combine(root, "Kordra.slnx"));
        var solutionProjects = solution.Descendants("Project")
            .Select(element => Absolute(root, RequiredAttribute(element, "Path"))).Order().ToArray();
        Assert.Equal(graph.Keys.Concat([
            Absolute(root, BootstrapTests), Absolute(root, ArtifactTests), Absolute(root, IdentityTests), Absolute(root, MessagingSpikeTests),
            Absolute(root, ReleaseRuntimeTests), Absolute(root, ReleaseDomainTests), Absolute(root, ApprovalDomainTests), Absolute(root, ApprovalRuntimeTests), Absolute(root, AuditTests), Absolute(root, TenantTests), Absolute(root, ProjectTests), Absolute(root, CredentialTests), Absolute(root, ArtifactRuntimeTests)
        ]).Order(), solutionProjects);
        var solutionList = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        solutionList.ArgumentList.Add("sln");
        solutionList.ArgumentList.Add(Path.Combine(root, "Kordra.slnx"));
        solutionList.ArgumentList.Add("list");
        using (var process = Process.Start(solutionList) ?? throw new InvalidOperationException("dotnet sln could not start."))
        {
            var exited = process.WaitForExit(15_000);
            Assert.True(exited, "dotnet sln list did not finish within its bound.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0, $"dotnet sln list failed: {error}");
            var listedProjects = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(path => Absolute(root, path))
                .Order().ToArray();
            Assert.Equal(solutionProjects, listedProjects);
        }
        var actualProductProjects = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path)).Order().ToArray();
        Assert.Equal(graph.Keys.Order(), actualProductProjects);

        // Implicit MSBuild imports can otherwise hide an edge outside the project XML.
        foreach (var project in graph.Keys)
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(project)!);
                 directory is not null && directory.FullName.StartsWith(root, StringComparison.Ordinal);
                 directory = directory.Parent)
            {
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                {
                    Assert.False(File.Exists(Path.Combine(directory.FullName, name)), $"Unapproved implicit import: {name}");
                }
            }
        }

        var runtime = XDocument.Load(Absolute(root, Inventory));
        Assert.Contains(runtime.Descendants("Compile"), element => (string?)element.Attribute("Remove") == "Contracts/**/*.cs");
        var tests = XDocument.Load(Absolute(root, BootstrapTests));
        var packages = tests.Descendants("PackageReference").ToDictionary(
            element => RequiredAttribute(element, "Include"), element => RequiredAttribute(element, "Version"));
        var pinned = new Dictionary<string, string>
        {
            ["Microsoft.NET.Test.Sdk"] = "18.10.1", ["xunit"] = "2.9.3", ["xunit.runner.visualstudio"] = "3.1.5"
        };
        Assert.Equal(pinned.OrderBy(pair => pair.Key), packages.OrderBy(pair => pair.Key));
        Assert.All(tests.Descendants("PackageReference"), element => Assert.Equal("all", (string?)element.Attribute("PrivateAssets")));
        Assert.Equal(new[] { Absolute(root, Host), Absolute(root, Inventory), Absolute(root, Contracts) }.Order(),
            ReadProjectEdges(Absolute(root, BootstrapTests)).Order());
        Assert.Equal(new[] { Absolute(root, Host), Absolute(root, DbMigrator), Absolute(root, IdentityAccess), Absolute(root, Security) }.Order(),
            ReadProjectEdges(Absolute(root, IdentityTests)).Order());
        Assert.Equal(new[] { Absolute(root, Host), Absolute(root, DbMigrator), Absolute(root, IdentityAccess),
            Absolute(root, TenantManagement), Absolute(root, Security) }.Order(), ReadProjectEdges(Absolute(root, TenantTests)).Order());
        Assert.Equal(new[] { Absolute(root, Host), Absolute(root, DbMigrator), Absolute(root, IdentityAccess), Absolute(root, Security),
            Absolute(root, TenantManagement), Absolute(root, ProjectEnvironment), Absolute(root, Inventory) }.Order(), ReadProjectEdges(Absolute(root, ProjectTests)).Order());
        var projectTests = XDocument.Load(Absolute(root, ProjectTests));
        Assert.Equal(pinned.OrderBy(pair => pair.Key), projectTests.Descendants("PackageReference").ToDictionary(
            element => RequiredAttribute(element, "Include"), element => RequiredAttribute(element, "Version")).OrderBy(pair => pair.Key));
        Assert.All(projectTests.Descendants("PackageReference"), element => Assert.Equal("all", (string?)element.Attribute("PrivateAssets")));
        var tenantTests = XDocument.Load(Absolute(root, TenantTests));
        Assert.Equal(pinned.OrderBy(pair => pair.Key), tenantTests.Descendants("PackageReference").ToDictionary(
            element => RequiredAttribute(element, "Include"), element => RequiredAttribute(element, "Version")).OrderBy(pair => pair.Key));
        Assert.All(tenantTests.Descendants("PackageReference"), element => Assert.Equal("all", (string?)element.Attribute("PrivateAssets")));
        var auditTests = XDocument.Load(Absolute(root, AuditTests));
        var auditPackages = auditTests.Descendants("PackageReference").ToDictionary(
            element => RequiredAttribute(element, "Include"), element => RequiredAttribute(element, "Version"));
        Assert.Equal(pinned.OrderBy(pair => pair.Key), auditPackages.OrderBy(pair => pair.Key));
        Assert.All(auditTests.Descendants("PackageReference"), element => Assert.Equal("all", (string?)element.Attribute("PrivateAssets")));
        Assert.Equal(new[]
        {
            Absolute(root, Host), Absolute(root, DbMigrator), Absolute(root, AuditCompliance), Absolute(root, AuditContracts)
        }.Order(), ReadProjectEdges(Absolute(root, AuditTests)).Order());
    }

    [Fact]
    public void Messaging_spike_is_test_only_with_exact_candidate_pins_and_no_product_edges()
    {
        var root = RepositoryRoot(); var project = Absolute(root, MessagingSpikeTests);
        var xml = XDocument.Load(project);
        Assert.Empty(ReadProjectEdges(project));
        AssertRestoredGraph(project, []);
        Assert.Equal("true", xml.Descendants("IsTestProject").Single().Value);
        Assert.Equal("false", xml.Descendants("IsPackable").Single().Value);
        Assert.Equal("true", xml.Descendants("RestorePackagesWithLockFile").Single().Value);
        var expected = new Dictionary<string, string>
        {
            ["Microsoft.NET.Test.Sdk"] = "18.10.1", ["xunit"] = "2.9.3", ["xunit.runner.visualstudio"] = "3.1.5",
            ["Microsoft.EntityFrameworkCore"] = "10.0.11", ["Microsoft.EntityFrameworkCore.Design"] = "10.0.11",
            ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "10.0.3", ["WolverineFx"] = "6.59.0",
            ["WolverineFx.EntityFrameworkCore"] = "6.59.0", ["WolverineFx.Postgresql"] = "6.59.0"
        };
        Assert.Equal(expected.OrderBy(x => x.Key), xml.Descendants("PackageReference").ToDictionary(
            x => RequiredAttribute(x, "Include"), x => RequiredAttribute(x, "Version")).OrderBy(x => x.Key));
        foreach (var reference in xml.Descendants("PackageReference").Where(x => new[]
            { "Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio", "Microsoft.EntityFrameworkCore.Design" }.Contains(RequiredAttribute(x, "Include"))))
            Assert.Equal("all", (string?)reference.Attribute("PrivateAssets"));
    }

    [Fact]
    public void Artifact_tests_keep_the_exact_approved_graph_and_pins()
    {
        var root = RepositoryRoot();
        var project = XDocument.Load(Absolute(root, ArtifactRuntimeTests));
        Assert.Equal(new[] { ArtifactRuntime, DbMigrator, Host }.Select(x => Absolute(root, x)).Order(),
            ReadProjectEdges(Absolute(root, ArtifactRuntimeTests)).Order());
        var pins = XDocument.Load(Absolute(root, ArtifactTests)).Descendants("PackageReference")
            .ToDictionary(x => RequiredAttribute(x, "Include"), x => RequiredAttribute(x, "Version"));
        Assert.Equal(pins.OrderBy(x => x.Key), project.Descendants("PackageReference")
            .ToDictionary(x => RequiredAttribute(x, "Include"), x => RequiredAttribute(x, "Version")).OrderBy(x => x.Key));
        Assert.All(project.Descendants("PackageReference"), x => Assert.Equal("all", (string?)x.Attribute("PrivateAssets")));
        var runtime = XDocument.Load(Absolute(root, ArtifactRuntime));
        Assert.Contains(runtime.Descendants("Compile"), x => (string?)x.Attribute("Remove") == "Domain/**/*.cs");
        Assert.Contains(runtime.Descendants("Compile"), x => (string?)x.Attribute("Remove") == "Contracts/**/*.cs");
    }

    [Fact]
    public void Credential_tests_keep_the_exact_approved_test_graph_and_pins()
    {
        var root = RepositoryRoot(); var project = XDocument.Load(Absolute(root, CredentialTests));
        var expected = new[] { Host, DbMigrator, IdentityAccess, Security, Credentials }.Select(x => Absolute(root, x)).Order();
        Assert.Equal(expected, ReadProjectEdges(Absolute(root, CredentialTests)).Order());
        var pins = XDocument.Load(Absolute(root, IdentityTests)).Descendants("PackageReference").ToDictionary(x => RequiredAttribute(x,"Include"), x => RequiredAttribute(x,"Version"));
        Assert.Equal(pins.OrderBy(x=>x.Key), project.Descendants("PackageReference").ToDictionary(x => RequiredAttribute(x,"Include"),x=>RequiredAttribute(x,"Version")).OrderBy(x=>x.Key));
        Assert.All(project.Descendants("PackageReference"), x=>Assert.Equal("all",(string?)x.Attribute("PrivateAssets")));
    }

    [Fact]
    public void BuiltProductAssemblies_EnforceRealPeReferencesAndContractsTypeIsolation()
    {
        var core = FrameworkAssemblies(typeof(object).Assembly.Location);
        var web = FrameworkAssemblies(typeof(WebApplication).Assembly.Location);
        var contracts = ReadAssembly(typeof(IInventoryReader).Assembly.Location);
        var inventory = ReadAssembly(typeof(UnavailableInventoryReader).Assembly.Location);
        var host = ReadAssembly(typeof(LocalApiHost).Assembly.Location);
        var identity = ReadAssembly(typeof(LocalAuthenticationService).Assembly.Location);
        var auditContracts = ReadAssembly(typeof(IAuditAppendPort).Assembly.Location);
        var audit = ReadAssembly(typeof(AuditAppendStore).Assembly.Location);
        var security = ReadAssembly(typeof(FairAsyncReaderWriterAdmissionGate).Assembly.Location);
        var identityContracts = ReadAssembly(typeof(Kordra.Modules.IdentityAccess.Contracts.IRoleDefinitionReader).Assembly.Location);
        var tenantContracts = ReadAssembly(typeof(Kordra.Modules.TenantManagement.Contracts.ITenantSubjectReader).Assembly.Location);
        var tenant = ReadAssembly(typeof(Kordra.Modules.TenantManagement.TenantMembershipService).Assembly.Location);
        var projectContracts = ReadAssembly(typeof(Kordra.Modules.ProjectEnvironment.Contracts.IProjectOwnershipReader).Assembly.Location);
        var project = ReadAssembly(typeof(Kordra.Modules.ProjectEnvironment.ProjectCatalogStore).Assembly.Location);
        var credentialContracts = ReadAssembly(typeof(Kordra.Modules.ConnectionManagement.Contracts.CredentialBindingSnapshotV1).Assembly.Location);
        var credentials = ReadAssembly(typeof(Kordra.Modules.ConnectionManagement.CredentialBindingStore).Assembly.Location);
        var releaseContracts = ReadAssembly(ArtifactOwnedOutput(ReleaseContracts, "Kordra.Modules.ReleaseManagement.Contracts"));
        var releaseRuntime = ReadAssembly(ArtifactOwnedOutput(ReleaseRuntime, "Kordra.Modules.ReleaseManagement"));
        var releaseDomain = ReadAssembly(ArtifactOwnedOutput(ReleaseDomain, "Kordra.ReleaseManagement.Domain"));
        var approvalDomain = ReadAssembly(ArtifactOwnedOutput(ApprovalDomain, "Kordra.ApprovalGovernance.Domain"));
        var approvalContracts = ReadAssembly(ArtifactOwnedOutput(ApprovalContracts, "Kordra.Modules.ApprovalGovernance.Contracts"));
        var approvalRuntime = ReadAssembly(ArtifactOwnedOutput(ApprovalRuntime, "Kordra.Modules.ApprovalGovernance"));
        var artifact = ReadAssembly(ArtifactOutput());
        var artifactRuntimePath = ArtifactOwnedOutput(ArtifactRuntime, "Kordra.Modules.ArtifactManagement");
        var artifactRuntime = ReadAssembly(artifactRuntimePath);
        var artifactContracts = ReadAssembly(ArtifactOwnedOutput(ArtifactContracts, "Kordra.Modules.ArtifactManagement.Contracts"));
        var persistencePackages = new[]
        {
            "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "Npgsql"
        };

        Assert.Empty(AssemblyViolations(contracts, core));
        Assert.Empty(AssemblyViolations(inventory, core.Concat(persistencePackages).Concat([contracts.Name, projectContracts.Name])));
        Assert.Empty(AssemblyViolations(host, core.Concat(web).Concat(persistencePackages).Concat(
            [contracts.Name, inventory.Name, identity.Name, identityContracts.Name, tenant.Name, tenantContracts.Name, auditContracts.Name, audit.Name, project.Name, projectContracts.Name, security.Name, credentials.Name, credentialContracts.Name, artifact.Name, artifactRuntime.Name, artifactContracts.Name, approvalDomain.Name, approvalContracts.Name, approvalRuntime.Name, releaseDomain.Name, releaseContracts.Name, releaseRuntime.Name])));
        Assert.Empty(AssemblyViolations(projectContracts, core));
        Assert.Empty(AssemblyViolations(project, core.Concat(persistencePackages).Append(projectContracts.Name)));
        Assert.Equal(new[] { projectContracts.Name }, project.References.Where(x => x.StartsWith("Kordra.", StringComparison.Ordinal)));
        Assert.Equal(new[] { contracts.Name, projectContracts.Name }.Order(), inventory.References.Where(x => x.StartsWith("Kordra.", StringComparison.Ordinal)).Order());
        Assert.DoesNotContain(project.Types, x => x.StartsWith("Kordra.Modules.ProjectEnvironment.Contracts.", StringComparison.Ordinal));
        Assert.Empty(AssemblyViolations(credentialContracts, core));
        Assert.Empty(AssemblyViolations(credentials, core.Concat(persistencePackages).Concat([credentialContracts.Name, identityContracts.Name, security.Name])));
        Assert.DoesNotContain(credentials.Types, x => x.StartsWith("Kordra.Modules.ConnectionManagement.Contracts.", StringComparison.Ordinal));
        Assert.Equal(new[] { credentialContracts.Name, identityContracts.Name, security.Name }.Order(), credentials.References.Where(x => x.StartsWith("Kordra.", StringComparison.Ordinal)).Order());
        Assert.Empty(AssemblyViolations(identityContracts, core));
        Assert.Empty(AssemblyViolations(tenantContracts, core));
        Assert.Empty(AssemblyViolations(identity, core.Concat(web).Concat(persistencePackages).Concat([identityContracts.Name, auditContracts.Name, security.Name])));
        Assert.Empty(AssemblyViolations(tenant, core.Concat(persistencePackages).Concat([tenantContracts.Name, identityContracts.Name, auditContracts.Name, security.Name])));
        Assert.Equal(new[] { tenantContracts.Name, identityContracts.Name, auditContracts.Name, security.Name }.Order(),
            tenant.References.Where(x => x.StartsWith("Kordra.", StringComparison.Ordinal)).Order());
        Assert.DoesNotContain(tenant.Types, x => x.StartsWith("Kordra.Modules.TenantManagement.Contracts.", StringComparison.Ordinal));
        Assert.DoesNotContain(identity.Types, x => x.StartsWith("Kordra.Modules.IdentityAccess.Contracts.", StringComparison.Ordinal));
        Assert.Empty(AssemblyViolations(security, core));
        Assert.Empty(AssemblyViolations(auditContracts, core));
        Assert.Empty(AssemblyViolations(releaseDomain, core));
        Assert.Empty(AssemblyViolations(releaseContracts, core));
        Assert.Empty(AssemblyViolations(releaseRuntime, core.Concat(persistencePackages).Concat([releaseDomain.Name, releaseContracts.Name])));
        Assert.DoesNotContain(releaseRuntime.Types, name => name.StartsWith("Kordra.ReleaseManagement.Domain.", StringComparison.Ordinal)
            || name.StartsWith("Kordra.Modules.ReleaseManagement.Contracts.", StringComparison.Ordinal));
        Assert.Empty(AssemblyViolations(approvalDomain, core));
        Assert.Empty(AssemblyViolations(approvalContracts, core));
        Assert.Empty(AssemblyViolations(approvalRuntime, core.Concat(persistencePackages).Concat([approvalDomain.Name, approvalContracts.Name])));
        Assert.Equal(new[] { approvalDomain.Name, approvalContracts.Name }.Order(),
            approvalRuntime.References.Where(x => x.StartsWith("Kordra.", StringComparison.Ordinal)).Order());
        Assert.DoesNotContain(approvalRuntime.Types, name => name.StartsWith("Kordra.ApprovalGovernance.Domain.", StringComparison.Ordinal)
            || name.StartsWith("Kordra.Modules.ApprovalGovernance.Contracts.", StringComparison.Ordinal));
        Assert.Empty(AssemblyViolations(artifact, core));
        Assert.Empty(AssemblyViolations(artifactContracts, core));
        Assert.Empty(AssemblyViolations(artifactRuntime, core.Concat(persistencePackages).Concat([artifact.Name, artifactContracts.Name])));
        Assert.DoesNotContain(artifactRuntime.Types, name => name.StartsWith("Kordra.ArtifactManagement.Domain.", StringComparison.Ordinal)
            || name.StartsWith("Kordra.Modules.ArtifactManagement.Contracts.", StringComparison.Ordinal));
        Assert.DoesNotContain(artifactRuntime.Types.Intersect(artifactContracts.Types), name => name != ".<Module>");
        Assert.Contains(contracts.Name, inventory.References);
        Assert.Contains(inventory.Name, host.References);
        Assert.Contains(contracts.Name, host.References);
        var hostProductReferences = host.References.Where(reference => new[]
            { contracts.Name, inventory.Name, identity.Name, tenant.Name, tenantContracts.Name, auditContracts.Name, audit.Name, project.Name, projectContracts.Name, security.Name, identityContracts.Name, credentials.Name, credentialContracts.Name, artifact.Name, artifactRuntime.Name, artifactContracts.Name }
            .Contains(reference, StringComparer.Ordinal)).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { contracts.Name, inventory.Name, identity.Name, tenant.Name, tenantContracts.Name, auditContracts.Name, audit.Name, project.Name, projectContracts.Name, security.Name, identityContracts.Name, credentials.Name, credentialContracts.Name, artifact.Name, artifactRuntime.Name, artifactContracts.Name }
            .Order(StringComparer.Ordinal), hostProductReferences);
        Assert.Equal(new[] { auditContracts.Name, security.Name }.Order(StringComparer.Ordinal),
            identity.References.Where(reference => reference == auditContracts.Name || reference == security.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(new[] { auditContracts.Name },
            audit.References.Where(reference => reference == auditContracts.Name));
        Assert.Contains("Kordra.Modules.InfrastructureInventory.Contracts.IInventoryReader", contracts.Types);
        Assert.DoesNotContain(inventory.Types, name => name.StartsWith(
            "Kordra.Modules.InfrastructureInventory.Contracts.", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.Types.Intersect(contracts.Types), name => name != ".<Module>");
        var productAssemblies = new[] { contracts, inventory, host, identity, auditContracts, audit, security, artifact, identityContracts, tenantContracts, tenant, projectContracts, project, credentials, credentialContracts, artifactRuntime, artifactContracts };
        var productNames = productAssemblies.Select(assembly => assembly.Name).ToHashSet(StringComparer.Ordinal);
        Assert.False(HasCycle(productAssemblies.ToDictionary(assembly => assembly.Name,
            assembly => assembly.References.Where(productNames.Contains).ToArray(), StringComparer.Ordinal)));
    }

    [Fact]
    public void ProjectDetector_RejectsForbiddenEdgesPackagesImportsAndCycles()
    {
        using var fixture = new TemporaryFixture();
        var root = RepositoryRoot();
        var original = XDocument.Load(Absolute(root, Contracts));
        var forbidden = new XDocument(original);
        forbidden.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", Absolute(root, Inventory)))));
        var project = Path.Combine(fixture.Path, "Forbidden.Contracts.csproj");
        forbidden.Save(project);
        Assert.Contains(ProjectViolations(project, []), violation => violation.StartsWith("Forbidden project edge:", StringComparison.Ordinal));

        foreach (var item in new[]
        {
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Synthetic.Forbidden"))),
            new XElement("Import", new XAttribute("Project", "synthetic.targets")),
            new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "Synthetic.Forbidden"))),
            new XElement("Target", new XAttribute("Name", "HiddenEdge"), new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", Absolute(root, Artifact)))))
        })
        {
            var altered = new XDocument(original);
            altered.Root!.Add(item);
            altered.Save(project);
            Assert.NotEmpty(ProjectViolations(project, []));
        }

        var cycleA = Path.Combine(fixture.Path, "CycleA.csproj");
        var cycleB = Path.Combine(fixture.Path, "CycleB.csproj");
        foreach (var (from, to) in new[] { (cycleA, cycleB), (cycleB, cycleA) })
        {
            new XDocument(new XElement("Project", new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", to))))).Save(from);
        }
        Assert.True(HasCycle(new[] { cycleA, cycleB }.ToDictionary(path => path, ReadProjectEdges, StringComparer.Ordinal)));
    }

    [Fact]
    public async Task CompiledNegativeFixtures_RejectActualRuntimeAndArtifactAssemblyEdges()
    {
        // These are execution stand-ins, never a claim that a product Agent exists.
        using var fixture = new TemporaryFixture();
        var runtimeAssembly = typeof(UnavailableInventoryReader).Assembly.Location;
        var contractsAssembly = typeof(IInventoryReader).Assembly.Location;
        var artifactAssembly = ArtifactOutput();
        var cases = new[]
        {
            (Name: "Forbidden.Contracts", Source: "public static class Leak { public static System.Type Value => typeof(Kordra.Modules.InfrastructureInventory.UnavailableInventoryReader); }",
                Dependencies: new[] { runtimeAssembly, contractsAssembly }, Forbidden: ReadAssembly(runtimeAssembly).Name),
            (Name: "Forbidden.ExecutionStandIn", Source: "public static class Leak { public static System.Type Value => typeof(Kordra.ArtifactManagement.Domain.OCIImageDigest); }",
                Dependencies: new[] { artifactAssembly }, Forbidden: ReadAssembly(artifactAssembly).Name)
        };
        var core = FrameworkAssemblies(typeof(object).Assembly.Location);
        foreach (var item in cases)
        {
            var project = fixture.WriteProject(item.Name, item.Source, item.Dependencies);
            // The same XML detector rejects direct binary references as hidden product edges.
            Assert.Contains(ProjectViolations(project, []), violation => violation.Contains("Reference", StringComparison.Ordinal));
            await fixture.BuildAsync(project);
            var built = ReadAssembly(Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", "net10.0", item.Name + ".dll"));
            Assert.Contains(item.Forbidden, built.References);
            Assert.Contains(AssemblyViolations(built, core), violation => violation.Contains(item.Forbidden, StringComparison.Ordinal));
        }
    }

    private static Dictionary<string, string[]> ApprovedGraph(string root) => new(StringComparer.Ordinal)
    {
        [Absolute(root, CredentialContracts)] = [],
        [Absolute(root, Credentials)] = [Absolute(root, CredentialContracts), Absolute(root, IdentityContracts), Absolute(root, Security)],
        [Absolute(root, Contracts)] = [],
        [Absolute(root, Inventory)] = [Absolute(root, Contracts), Absolute(root, ProjectContracts)],
        [Absolute(root, ProjectContracts)] = [],
        [Absolute(root, ProjectEnvironment)] = [Absolute(root, ProjectContracts)],
        [Absolute(root, Host)] = [Absolute(root, Inventory), Absolute(root, Contracts), Absolute(root, IdentityAccess),
            Absolute(root, AuditCompliance), Absolute(root, AuditContracts), Absolute(root, TenantManagement), Absolute(root, ProjectEnvironment), Absolute(root, Credentials), Absolute(root, ArtifactRuntime), Absolute(root, ArtifactContracts), Absolute(root, ApprovalRuntime), Absolute(root, ReleaseRuntime)],
        [Absolute(root, ReleaseContracts)] = [],
        [Absolute(root, ReleaseRuntime)] = [Absolute(root, ReleaseDomain), Absolute(root, ReleaseContracts)],
        [Absolute(root, ReleaseDomain)] = [],
        [Absolute(root, ApprovalContracts)] = [],
        [Absolute(root, ApprovalRuntime)] = [Absolute(root, ApprovalDomain), Absolute(root, ApprovalContracts)],
        [Absolute(root, ApprovalDomain)] = [],
        [Absolute(root, Artifact)] = [],
        [Absolute(root, ArtifactContracts)] = [],
        [Absolute(root, ArtifactRuntime)] = [Absolute(root, Artifact), Absolute(root, ArtifactContracts)],
        [Absolute(root, IdentityAccess)] = [Absolute(root, AuditContracts), Absolute(root, Security), Absolute(root, IdentityContracts)],
        [Absolute(root, IdentityContracts)] = [],
        [Absolute(root, TenantContracts)] = [],
        [Absolute(root, TenantManagement)] = [Absolute(root, TenantContracts), Absolute(root, IdentityContracts), Absolute(root, AuditContracts), Absolute(root, Security)],
        [Absolute(root, AuditContracts)] = [],
        [Absolute(root, AuditCompliance)] = [Absolute(root, AuditContracts)],
        [Absolute(root, Security)] = [],
        [Absolute(root, DbMigrator)] = [Absolute(root, IdentityAccess), Absolute(root, AuditCompliance), Absolute(root, TenantManagement), Absolute(root, ProjectEnvironment), Absolute(root, Inventory), Absolute(root, Credentials), Absolute(root, ArtifactRuntime), Absolute(root, ApprovalRuntime), Absolute(root, ReleaseRuntime)]
    };

    private static IReadOnlyDictionary<string, string> ApprovedPackages(string root, string project)
    {
        var identity = Absolute(root, IdentityAccess);
        var audit = Absolute(root, AuditCompliance);
        var migrator = Absolute(root, DbMigrator);
        if (project == Absolute(root, ArtifactRuntime) || project == Absolute(root, ApprovalRuntime) || project == Absolute(root, ReleaseRuntime))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.EntityFrameworkCore.Design"] = "10.0.11",
                ["Microsoft.EntityFrameworkCore.Relational"] = "10.0.11",
                ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "10.0.3"
            };
        }
        if (project == audit || project == Absolute(root, Credentials) || project == Absolute(root, TenantManagement) || project == Absolute(root, ProjectEnvironment) || project == Absolute(root, Inventory))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.EntityFrameworkCore.Design"] = "10.0.11",
                ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "10.0.3"
            };
        }
        if (project == identity)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = "10.0.11",
                ["Microsoft.EntityFrameworkCore.Design"] = "10.0.11",
                ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "10.0.3"
            };
        }

        if (project == migrator)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.EntityFrameworkCore.Design"] = "10.0.11"
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static List<string> ProjectViolations(
        string project,
        IEnumerable<string> allowedReferences,
        IReadOnlyDictionary<string, string>? allowedPackages = null)
    {
        var document = XDocument.Load(project);
        var violations = new List<string>();
        var allowed = allowedReferences.ToHashSet(StringComparer.Ordinal);
        var references = new HashSet<string>(StringComparer.Ordinal);
        allowedPackages ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var packages = new HashSet<string>(StringComparer.Ordinal);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TargetFramework"] = "net10.0", ["ImplicitUsings"] = "enable", ["Nullable"] = "enable",
            ["TreatWarningsAsErrors"] = "true", ["RestorePackagesWithLockFile"] = "true"
        };
        if (project == Absolute(RepositoryRoot(), DbMigrator)) properties["OutputType"] = "Exe";
        var root = document.Root!;
        if (root.Name != "Project" || root.Attributes().Any(attribute => attribute.Name != "Sdk"))
            violations.Add("Unapproved project root/attributes");
        var sdk = (string?)root.Attribute("Sdk");
        if (sdk is not ("Microsoft.NET.Sdk" or "Microsoft.NET.Sdk.Web")) violations.Add("Unapproved SDK");
        foreach (var element in root.Descendants())
        {
            var name = element.Name.LocalName;
            if (element.Name.Namespace != XNamespace.None || element.Attribute("Condition") is not null)
                violations.Add($"Unapproved conditional/namespaced XML: {name}");
            if (name is "PropertyGroup" or "ItemGroup")
            {
                if (element.Parent != root || element.HasAttributes) violations.Add($"Nested/modified group: {name}");
                continue;
            }
            if ((name is "PrivateAssets" or "IncludeAssets") && element.Parent?.Name == "PackageReference")
                continue; // The package node validates the exact metadata and values.
            if (element.Parent?.Name == "PropertyGroup" && properties.TryGetValue(name, out var value))
            {
                if (element.HasAttributes || element.HasElements || element.Value != value || root.Descendants(name).Count() != 1)
                    violations.Add($"Unapproved property value/override: {name}");
                continue;
            }
            if (name == "ProjectReference" && element.Parent?.Name == "ItemGroup")
            {
                var include = (string?)element.Attribute("Include");
                if (include is null || include.Contains('$') || element.HasElements || element.Attributes().Count() != 1)
                {
                    violations.Add("Unapproved computed/modified ProjectReference");
                    continue;
                }
                var target = Absolute(Path.GetDirectoryName(project)!, include);
                if (!references.Add(target)) violations.Add($"Duplicate project edge: {target}");
                if (!allowed.Contains(target)) violations.Add($"Forbidden project edge: {target}");
                continue;
            }
            if (name == "PackageReference" && element.Parent?.Name == "ItemGroup")
            {
                var include = (string?)element.Attribute("Include");
                var version = (string?)element.Attribute("Version");
                if (include is null || version is null || element.Attributes().Count() != 2 ||
                    !packages.Add(include) || !allowedPackages.TryGetValue(include, out var expectedVersion) ||
                    version != expectedVersion)
                {
                    violations.Add($"Unapproved package reference: {include ?? "<missing>"}");
                    continue;
                }

                var expectedMetadata = include == "Microsoft.EntityFrameworkCore.Design"
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["PrivateAssets"] = "all",
                        ["IncludeAssets"] = "runtime; build; native; contentfiles; analyzers; buildtransitive"
                    }
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                var actualMetadata = element.Elements().ToDictionary(
                    child => child.Name.LocalName, child => child.Value, StringComparer.Ordinal);
                if (actualMetadata.Count != expectedMetadata.Count ||
                    expectedMetadata.Any(pair => actualMetadata.GetValueOrDefault(pair.Key) != pair.Value) ||
                    element.Elements().Any(child => child.HasAttributes || child.HasElements))
                {
                    violations.Add($"Unapproved package metadata: {include}");
                }

                continue;
            }
            if (name == "Compile" && (string?)element.Attribute("Remove") == "Contracts/**/*.cs" &&
                element.Attributes().Count() == 1 && !element.HasElements) continue;
            // Only these two existing HTTPS/database fixture helpers are linked; no test-project edge or arbitrary source import.
            if (project == Absolute(RepositoryRoot(), "tests/ArtifactManagement.Tests/ArtifactManagement.Tests.csproj") &&
                name == "Compile" && element.Parent?.Name == "ItemGroup" && element.Attributes().Count() == 2 && !element.HasElements &&
                new[] { "LocalAuthenticationTestHost.cs", "TestDatabaseConfiguration.cs" }.Any(file =>
                    (string?)element.Attribute("Include") == "../IdentityAccess.Tests/" + file &&
                    (string?)element.Attribute("Link") == "Fixtures/" + file)) continue;
            if ((project == Absolute(RepositoryRoot(), ApprovalRuntime) || project == Absolute(RepositoryRoot(), ReleaseRuntime)) && element.Parent?.Name == "ItemGroup" &&
                name == "Compile" && (string?)element.Attribute("Remove") == "Domain/**/*.cs" &&
                element.Attributes().Count() == 1 && !element.HasElements) continue;
            if (project == Absolute(RepositoryRoot(), ArtifactRuntime) && element.Parent?.Name == "ItemGroup" &&
                element.Attributes().Count() == 1 && !element.HasElements &&
                ((name == "Compile" && (string?)element.Attribute("Remove") == "Domain/**/*.cs") ||
                 (name == "InternalsVisibleTo" && (string?)element.Attribute("Include") == "ArtifactManagement.Tests" &&
                  root.Descendants("InternalsVisibleTo").Count() == 1))) continue;
            violations.Add($"Unapproved XML element: {name}");
        }
        foreach (var target in allowed.Except(references)) violations.Add($"Missing declared project edge: {target}");
        foreach (var package in allowedPackages.Keys.Except(packages, StringComparer.Ordinal))
            violations.Add($"Missing declared package reference: {package}");
        foreach (var property in properties.Keys)
            if (root.Descendants(property).Count() != 1) violations.Add($"Missing/duplicate required property: {property}");
        return violations;
    }

    private static string[] ReadProjectEdges(string project) => XDocument.Load(project).Descendants("ProjectReference")
        .Select(element => Absolute(Path.GetDirectoryName(project)!, RequiredAttribute(element, "Include"))).ToArray();

    private static void AssertRestoredGraph(string project, string[] expectedReferences)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        Assert.All(assets.RootElement.GetProperty("libraries").EnumerateObject(), library =>
        {
            var type = library.Value.GetProperty("type").GetString();
            Assert.True(type is "project" or "package", $"Unexpected restored library type: {type}");
        });
        var restored = assets.RootElement.GetProperty("project").GetProperty("restore")
            .GetProperty("frameworks").GetProperty("net10.0").GetProperty("projectReferences")
            .EnumerateObject().Select(reference => Path.GetFullPath(reference.Name)).Order().ToArray();
        Assert.Equal(expectedReferences.Order(), restored);
    }

    private static bool HasCycle(Dictionary<string, string[]> graph)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var finished = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string node)
        {
            if (active.Contains(node)) return true;
            if (finished.Contains(node)) return false;
            active.Add(node);
            if (graph.TryGetValue(node, out var edges) && edges.Any(Visit)) return true;
            active.Remove(node);
            finished.Add(node);
            return false;
        }
        return graph.Keys.Any(Visit);
    }

    private sealed record AssemblyShape(string Name, string[] References, string[] Types);

    private static AssemblyShape ReadAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        string TypeName(TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            var owner = type.GetDeclaringType();
            return owner.IsNil ? metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name)
                : TypeName(owner) + "+" + metadata.GetString(type.Name);
        }
        return new(metadata.GetString(metadata.GetAssemblyDefinition().Name),
            metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray(),
            metadata.TypeDefinitions.Select(TypeName).ToArray());
    }

    private static string[] AssemblyViolations(AssemblyShape assembly, IEnumerable<string> allowed) =>
        assembly.References.Except(allowed, StringComparer.Ordinal)
            .Select(reference => $"Forbidden assembly edge: {assembly.Name} -> {reference}").ToArray();

    private static string[] FrameworkAssemblies(string assemblyPath) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(assemblyPath)!, "*.dll")
            .Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();

    private static string ArtifactOwnedOutput(string project, string assemblyName)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var path = Path.Combine(Path.GetDirectoryName(Absolute(RepositoryRoot(), project))!, "bin", configuration, "net10.0", assemblyName + ".dll");
        Assert.True(File.Exists(path), "Build the approved ART projects before architecture validation.");
        return path;
    }

    private static string ArtifactOutput()
    {
        // Root solution builds Artifact independently; bootstrap tests add no Artifact project edge.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var path = Path.Combine(RepositoryRoot(), "src", "Modules", "ArtifactManagement", "Domain",
            "bin", configuration, "net10.0", "Kordra.ArtifactManagement.Domain.dll");
        Assert.True(File.Exists(path), "Build the root solution before running bootstrap architecture tests.");
        return path;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Kordra.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static bool IsBuildOutput(string path) => path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin");
    private static string Absolute(string directory, string relative) => Path.GetFullPath(Path.Combine(directory, relative));
    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? throw new InvalidOperationException($"Missing {name}");

    private sealed class TemporaryFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kordra-bootstrap-" + Guid.NewGuid().ToString("N"));

        public TemporaryFixture()
        {
            Directory.CreateDirectory(Path);
            // Do not inherit repository/user build imports, sources, or SDK roll-forward policy.
            File.WriteAllText(System.IO.Path.Combine(Path, "Directory.Build.props"), "<Project />");
            File.WriteAllText(System.IO.Path.Combine(Path, "Directory.Build.targets"), "<Project />");
            File.WriteAllText(System.IO.Path.Combine(Path, "Directory.Packages.props"), "<Project />");
            File.WriteAllText(System.IO.Path.Combine(Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            File.WriteAllText(System.IO.Path.Combine(Path, "global.json"),
                "{\"sdk\":{\"version\":\"10.0.400\",\"rollForward\":\"latestPatch\",\"allowPrerelease\":false}}");
        }

        public string WriteProject(string name, string source, string[] dependencies)
        {
            var directory = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(directory);
            var project = System.IO.Path.Combine(directory, name + ".csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("RestoreSources", ""), new XElement("NuGetAudit", "false")),
                new XElement("ItemGroup", dependencies.Select(dependency => new XElement("Reference",
                    new XAttribute("Include", System.IO.Path.GetFileNameWithoutExtension(dependency)),
                    new XElement("HintPath", dependency)))))).Save(project);
            File.WriteAllText(System.IO.Path.Combine(directory, "Leak.cs"), source);
            return project;
        }

        public async Task BuildAsync(string project)
        {
            await RunDotnetAsync(["restore", project, "--configfile", System.IO.Path.Combine(Path, "NuGet.Config"),
                "--disable-parallel", "-p:NuGetAudit=false", "-p:UseSharedCompilation=false", "-nodeReuse:false"]);
            await RunDotnetAsync(["build", project, "--configuration", "Release", "--no-restore", "--disable-build-servers",
                "-p:UseSharedCompilation=false", "-nodeReuse:false"]);
        }

        private async Task RunDotnetAsync(string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // The fixture inherits no user environment, credentials or remote package sources.
            start.Environment.Clear();
            // PATH locates the installed SDK; Windows also needs its OS directory.
            foreach (var key in new[] { "PATH", "SystemRoot" })
                if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
            var cliHome = System.IO.Path.Combine(Path, "cli");
            var home = System.IO.Path.Combine(Path, "home");
            Directory.CreateDirectory(cliHome);
            Directory.CreateDirectory(home);
            start.Environment["DOTNET_CLI_HOME"] = cliHome;
            start.Environment["HOME"] = home;
            start.Environment["NUGET_PACKAGES"] = System.IO.Path.Combine(Path, "packages");
            var temporary = System.IO.Path.Combine(Path, "tmp");
            Directory.CreateDirectory(temporary);
            start.Environment["TMPDIR"] = temporary;
            start.Environment["TEMP"] = temporary;
            start.Environment["TMP"] = temporary;
            if (OperatingSystem.IsLinux())
            {
                var runtimeDirectory = System.IO.Path.Combine(Path, "runtime");
                Directory.CreateDirectory(runtimeDirectory);
                var privateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                File.SetUnixFileMode(home, privateDirectory);
                File.SetUnixFileMode(cliHome, privateDirectory);
                File.SetUnixFileMode(runtimeDirectory, privateDirectory);
                start.Environment["XDG_RUNTIME_DIR"] = runtimeDirectory;
            }
            start.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            start.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
            start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start installed SDK.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                var output = await stdout;
                var error = await stderr;
                Assert.True(process.ExitCode == 0, $"Fixture SDK command failed ({process.ExitCode}):\n{output}\n{error}");
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
                throw new TimeoutException("Isolated fixture SDK command exceeded 60 seconds; its process tree was terminated.");
            }
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
