using JasperFx;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>Test CLI for metadata generation and the approved isolated custody child.</summary>
public static class WorkflowFixtureProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "custody-fixture", "hold" }))
            return await WorkflowCustodyProcessTests.HoldInChildAsync();
        if (args.Length == 2 && args[0] == "wake-crash-fixture")
            return await WorkflowProcessCrashTests.RunChildAsync(args[1]);
        var wake = args.SequenceEqual(new[] { "wake-codegen", "write" });
        if (!wake && !args.SequenceEqual(new[] { "codegen", "write" }))
            throw new InvalidOperationException("Only bounded native code generation is enabled by this test CLI.");
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "InfrastructureInventory.Bootstrap.Tests.csproj")))
            project = project.Parent;
        if (project is null) throw new InvalidOperationException("Owned test project root required for code generation.");
        Directory.SetCurrentDirectory(project.FullName);
        if (wake)
            return await WorkflowWakeFixtureProfile.Builder("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1",
                "t12_wake_codegen", new WorkflowWakeFixtureState(Guid.Empty, ""), false, true).RunJasperFxCommands(new[] { "codegen", "write" });
        return await WorkflowTransactionSchemaTests.NativeBuilder(
            "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1", "t12_codegen", null, false)
            .RunJasperFxCommands(args);
    }
}
