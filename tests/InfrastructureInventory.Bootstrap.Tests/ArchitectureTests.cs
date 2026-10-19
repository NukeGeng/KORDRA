using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using Kordra.Api;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ArchitectureTests
{
    private const string Contracts = "src/Modules/InfrastructureInventory/Contracts/Kordra.Modules.InfrastructureInventory.Contracts.csproj";
    private const string Inventory = "src/Modules/InfrastructureInventory/Kordra.Modules.InfrastructureInventory.csproj";
    private const string Host = "src/Host/Kordra.Api/Kordra.Api.csproj";
    private const string Artifact = "src/Modules/ArtifactManagement/Domain/Kordra.ArtifactManagement.Domain.csproj";
    private const string BootstrapTests = "tests/InfrastructureInventory.Bootstrap.Tests/InfrastructureInventory.Bootstrap.Tests.csproj";
    private const string ArtifactTests = "tests/ArtifactManagement.Domain.Tests/ArtifactManagement.Domain.Tests.csproj";

    [Fact]
    public void ProductProjects_EnforceActualXmlGraphAndRestoredDependencies()
    {
        var root = RepositoryRoot();
        var graph = ApprovedGraph(root);
        foreach (var (project, references) in graph)
        {
            Assert.Empty(ProjectViolations(project, references));
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
        Assert.Equal(graph.Keys.Concat([Absolute(root, BootstrapTests), Absolute(root, ArtifactTests)]).Order(), solutionProjects);
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
    }

    [Fact]
    public void BuiltProductAssemblies_EnforceRealPeReferencesAndContractsTypeIsolation()
    {
        var core = FrameworkAssemblies(typeof(object).Assembly.Location);
        var web = FrameworkAssemblies(typeof(WebApplication).Assembly.Location);
        var contracts = ReadAssembly(typeof(IInventoryReader).Assembly.Location);
        var inventory = ReadAssembly(typeof(UnavailableInventoryReader).Assembly.Location);
        var host = ReadAssembly(typeof(LocalApiHost).Assembly.Location);
        var artifact = ReadAssembly(ArtifactOutput());

        Assert.Empty(AssemblyViolations(contracts, core));
        Assert.Empty(AssemblyViolations(inventory, core.Append(contracts.Name)));
        Assert.Empty(AssemblyViolations(host, core.Concat(web).Concat([contracts.Name, inventory.Name])));
        Assert.Empty(AssemblyViolations(artifact, core));
        Assert.Contains(contracts.Name, inventory.References);
        Assert.Contains(inventory.Name, host.References);
        Assert.Contains(contracts.Name, host.References);
        Assert.Contains("Kordra.Modules.InfrastructureInventory.Contracts.IInventoryReader", contracts.Types);
        Assert.DoesNotContain(inventory.Types, name => name.StartsWith(
            "Kordra.Modules.InfrastructureInventory.Contracts.", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.Types.Intersect(contracts.Types), name => name != ".<Module>");
        var productAssemblies = new[] { contracts, inventory, host, artifact };
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
        [Absolute(root, Contracts)] = [],
        [Absolute(root, Inventory)] = [Absolute(root, Contracts)],
        [Absolute(root, Host)] = [Absolute(root, Inventory), Absolute(root, Contracts)],
        [Absolute(root, Artifact)] = []
    };

    private static List<string> ProjectViolations(string project, IEnumerable<string> allowedReferences)
    {
        var document = XDocument.Load(project);
        var violations = new List<string>();
        var allowed = allowedReferences.ToHashSet(StringComparer.Ordinal);
        var references = new HashSet<string>(StringComparer.Ordinal);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TargetFramework"] = "net10.0", ["ImplicitUsings"] = "enable", ["Nullable"] = "enable",
            ["TreatWarningsAsErrors"] = "true", ["RestorePackagesWithLockFile"] = "true"
        };
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
            if (name == "Compile" && (string?)element.Attribute("Remove") == "Contracts/**/*.cs" &&
                element.Attributes().Count() == 1 && !element.HasElements) continue;
            violations.Add($"Unapproved XML element: {name}");
        }
        foreach (var target in allowed.Except(references)) violations.Add($"Missing declared project edge: {target}");
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
            Assert.Equal("project", library.Value.GetProperty("type").GetString()));
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
        return new(metadata.GetString(metadata.GetAssemblyDefinition().Name),
            metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray(),
            metadata.TypeDefinitions.Select(handle =>
            {
                var type = metadata.GetTypeDefinition(handle);
                return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            }).ToArray());
    }

    private static string[] AssemblyViolations(AssemblyShape assembly, IEnumerable<string> allowed) =>
        assembly.References.Except(allowed, StringComparer.Ordinal)
            .Select(reference => $"Forbidden assembly edge: {assembly.Name} -> {reference}").ToArray();

    private static string[] FrameworkAssemblies(string assemblyPath) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(assemblyPath)!, "*.dll")
            .Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();

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
            start.Environment["DOTNET_CLI_HOME"] = System.IO.Path.Combine(Path, "cli");
            start.Environment["HOME"] = System.IO.Path.Combine(Path, "home");
            start.Environment["NUGET_PACKAGES"] = System.IO.Path.Combine(Path, "packages");
            var temporary = System.IO.Path.Combine(Path, "tmp");
            Directory.CreateDirectory(temporary);
            start.Environment["TMPDIR"] = temporary;
            start.Environment["TEMP"] = temporary;
            start.Environment["TMP"] = temporary;
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
