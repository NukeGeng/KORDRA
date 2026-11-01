using JasperFx;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>Metadata-only CLI for public native static code generation; no fixture execution command.</summary>
public static class WorkflowFixtureProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.SequenceEqual(new[] { "codegen", "write" }))
            throw new InvalidOperationException("Only bounded native code generation is enabled by this test CLI.");
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "InfrastructureInventory.Bootstrap.Tests.csproj")))
            project = project.Parent;
        if (project is null) throw new InvalidOperationException("Owned test project root required for code generation.");
        Directory.SetCurrentDirectory(project.FullName);
        return await WorkflowTransactionSchemaTests.NativeBuilder(
            "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1", "t12_codegen", null, false)
            .RunJasperFxCommands(args);
    }
}
