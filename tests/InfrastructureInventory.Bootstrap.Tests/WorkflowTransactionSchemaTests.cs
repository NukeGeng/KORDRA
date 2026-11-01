using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JasperFx;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.Runtime;
using Wolverine.Postgresql.Transport;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowTransactionSchemaTests
{
    public static IReadOnlyList<string> ProposedCases { get; } = Array.AsReadOnly(new[] {"412387ba-ff92-4706-8be6-6484e50b8e2d","fa412c69-881d-4e47-9313-d945a558542f","49f5cac9-2a32-46f0-8f23-823c5a363d1d" });

    [Fact]
    public void MultiOwnerSchemaArtifactsUseOnlyProposedCasesWithoutStartingHostsOrConnections()
    {
        foreach (var text in ProposedCases)
        {
            var caseId = Guid.ParseExact(text, "D"); var schema = "t12_" + caseId.ToString("N");
            using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
            using var workflow = WorkflowDbContext.CreateFixture(connection, caseId);
            using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
            using var host = BuildNativeProfile("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1", schema);
            ConfigureTransportMetadata(host);
            var database = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
            var messageContext = new MessageContext(host.Services.GetRequiredService<IWolverineRuntime>());
            Assert.Null(messageContext.Transaction);
            var native = StableNativeScript(database, schema);
            Assert.Equal(native, StableNativeScript(database, schema));
            Assert.Contains("wolverine_queue_", native);
            Assert.Contains("_scheduled", native);
            using var listening = NativeBuilder("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1", schema, null, true).Build();
            ConfigureTransportMetadata(listening);
            var listeningDatabase = Assert.IsAssignableFrom<IDatabase>(listening.Services.GetRequiredService<IMessageStore>());
            Assert.Equal(native, StableNativeScript(listeningDatabase, schema));
            var scripts = new Dictionary<string, string> {
                ["wfl.sql"] = workflow.Database.GenerateCreateScript(),
                ["aud.sql"] = audit.Database.GenerateCreateScript(),
                ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", schema + ".", StringComparison.Ordinal),
                ["msg-native.sql"] = native,
                ["cleanup.sql"] = "DROP SCHEMA " + schema + " CASCADE;\n"
            };
            Assert.EndsWith("CASCADE;\n", scripts["cleanup.sql"]);
            Assert.DoesNotContain("\\n", scripts["cleanup.sql"]);
            foreach (var script in scripts.Values)
            {
                Assert.Contains(schema, script); Assert.DoesNotContain("SYNTHETIC", script);
                Assert.DoesNotContain("not_opened", script); Assert.DoesNotContain("wolverine_queues", script);
            }
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            if (Environment.GetEnvironmentVariable("KORDRA_WFL_TRANSACTION_ARTIFACT_DIRECTORY") is { Length: > 0 } directory)
            {
                var project = new DirectoryInfo(AppContext.BaseDirectory);
                while (project is not null && !File.Exists(Path.Combine(project.FullName, "Kordra.slnx"))) project = project.Parent;
                Assert.NotNull(project);
                var allowed = Path.GetFullPath(Path.Combine(project.FullName, ".amr-local", "t12-remaining-design", "t12-2c-schema-review"));
                Assert.Equal(allowed, Path.GetFullPath(directory));
                Directory.CreateDirectory(allowed);
                var nullCallback = typeof(MessageContext).Assembly.GetType("Wolverine.Runtime.WorkerQueues.NullChannelCallback");
                var surface = JsonSerializer.Serialize(new {
                    callback = typeof(Wolverine.Transports.IChannelCallback).GetMethods().Select(x => x.ToString()),
                    readEnvelope = typeof(MessageContext).GetMethods().Where(x => x.Name == "ReadEnvelope").Select(x => x.ToString()),
                    nullCallbackPublic = nullCallback?.IsPublic,
                    nullCallbackConstructors = nullCallback?.GetConstructors().Select(x => x.ToString()),
                    nullCallbackStaticMembers = nullCallback?.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Select(x => x.Name)
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(allowed, "public-api.json"), surface);
                var destination = Path.Combine(allowed, caseId.ToString("D")); Directory.CreateDirectory(destination);
                Assert.Null(new DirectoryInfo(destination).LinkTarget);
                foreach (var (name, sql) in scripts)
                {
                    var path = Path.Combine(destination, name);
                    if (File.Exists(path)) Assert.Equal(sql, File.ReadAllText(path)); else File.WriteAllText(path, sql);
                }
                var hashes = scripts.ToDictionary(x => x.Key,
                    x => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))));
                var manifest = JsonSerializer.Serialize(new { status = "proposal_not_operation_authorization", caseId, schema,
                    sha256 = hashes }, new JsonSerializerOptions { WriteIndented = true });
                var file = Path.Combine(destination, "manifest.json");
                if (File.Exists(file)) Assert.Equal(manifest, File.ReadAllText(file)); else File.WriteAllText(file, manifest);
            }
        }
    }

    private static void ConfigureTransportMetadata(IHost host)
    {
        // Public metadata composition only: no ConnectAsync, StartAsync or connection opening.
        // Native transport queue objects are attached during ConfigureAsync, before SQL export.
        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        var transport = runtime.Options.Transports.GetOrCreate<PostgresqlTransport>();
        transport.ConfigureAsync(runtime).GetAwaiter().GetResult();
        Assert.NotEmpty(transport.Queues);
        foreach (var route in new[] { "workflow-transitions-v1", "workflow-results-v1" })
            Assert.Contains(transport.Queues, queue => queue.Name == transport.SanitizeIdentifier(route));
    }

    public static IHost BuildNativeProfile(string locator, string schema) => NativeBuilder(locator, schema, null, false).Build();

    public static IHostBuilder NativeBuilder(string locator, string schema, WorkflowFixtureState? state, bool listen)
    {
        var fixture = state ?? new WorkflowFixtureState(Guid.Empty, locator);
        return Host.CreateDefaultBuilder().ConfigureServices(services => services.AddSingleton(fixture)).UseWolverine(options => {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<WorkflowFixtureIncomingHandler>();
            options.AddSerializer(new WorkflowFixtureWire.Serializer(fixture));
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
            options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromMinutes(1);
            options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMinutes(1);
            options.PublishMessage<WorkflowFixtureIncoming>().ToPostgresqlQueue("workflow-transitions-v1");
            options.PublishMessage<Guid>().ToPostgresqlQueue("workflow-results-v1");
            if (listen) options.ListenToPostgresqlQueue("workflow-transitions-v1").Sequential();
            options.OnException<WorkflowFixtureFailureException>().Discard();
        });
    }

    private static string StableNativeScript(IDatabase database, string expectedSchema)
    {
        _ = database.ToDatabaseScript(); // Native metadata post-processing, not SQL rewriting.
        var schemas = database.AllObjects().SelectMany(x => x.AllNames()).Select(x => x.Schema).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { expectedSchema }, schemas);
        var features = database.BuildFeatureSchemas().Select(x => (IFeatureSchema)new OrderedFeature(x)).ToArray();
        Assert.NotEmpty(features); using var output = new StringWriter();
        features[0].Migrator.WriteScript(output, (migrator, writer) => {
            migrator.WriteSchemaCreationSql(schemas, writer);
            foreach (var feature in SchemaObjectOrdering.InDependencyOrder(features)) feature.WriteFeatureCreation(migrator, writer);
        });
        return output.ToString();
    }

    private sealed class OrderedFeature : IFeatureSchema
    {
        private readonly IFeatureSchema _source;
        public OrderedFeature(IFeatureSchema source)
        {
            _source = source; Objects = (ISchemaObject[])source.Objects.Clone();
            var positions = Enumerable.Range(0, Objects.Length).Where(i => Objects[i].Identifier.Name.StartsWith("wolverine_queue_", StringComparison.Ordinal)).ToArray();
            var ordered = positions.Select(i => Objects[i]).OrderBy(x => x.Identifier.Name, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < positions.Length; i++) Objects[positions[i]] = ordered[i];
        }
        public ISchemaObject[] Objects { get; }
        public string Identifier => _source.Identifier;
        public Migrator Migrator => _source.Migrator;
        public Type StorageType => _source.StorageType;
        public void WritePermissions(Migrator rules, TextWriter writer) => _source.WritePermissions(rules, writer);
        public IEnumerable<Type> DependentTypes() => _source.DependentTypes();
    }
}
