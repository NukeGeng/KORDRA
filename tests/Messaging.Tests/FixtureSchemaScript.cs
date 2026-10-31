using Weasel.Core;
using Weasel.Core.Migrations;

namespace Kordra.Messaging.Tests;

/// <summary>Fixed two-queue fixture only: public native DDL, stable independent queue order, no SQL rewriting.</summary>
internal static class FixtureSchemaScript
{
    private static readonly string[] QueueOrder = ["wolverine_queue_nativecompatibility", "wolverine_queue_nativecompatibility_scheduled",
        "wolverine_queue_native_result", "wolverine_queue_native_result_scheduled"];

    internal static string Create(IDatabase database, IReadOnlyList<string>? reviewedQueueOrder = null)
    {
        var order = reviewedQueueOrder?.ToArray() ?? QueueOrder;
        if (order.Length != QueueOrder.Length || order.Distinct(StringComparer.Ordinal).Count() != QueueOrder.Length ||
            order.Any(name => !QueueOrder.Contains(name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Exact reviewed fixture queue object set required.");
        // Let the public native script API perform its metadata post-processing first.
        _ = database.ToDatabaseScript();
        var features = database.BuildFeatureSchemas().Select(f => (IFeatureSchema)new OrderedFeature(f, order)).ToArray();
        if (features.Length == 0) throw new InvalidOperationException("Native fixture schema metadata missing.");
        var migrator = features[0].Migrator;
        var schemas = database.AllObjects().SelectMany(o => o.AllNames()).Select(n => n.Schema)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        using var output = new StringWriter();
        migrator.WriteScript(output, (native, writer) =>
        {
            native.WriteSchemaCreationSql(schemas, writer);
            foreach (var feature in SchemaObjectOrdering.InDependencyOrder(features))
                feature.WriteFeatureCreation(native, writer);
        });
        return output.ToString();
    }

    private sealed class OrderedFeature : IFeatureSchema
    {
        private readonly IFeatureSchema _source;
        public OrderedFeature(IFeatureSchema source, string[] order)
        {
            _source = source; Objects = (ISchemaObject[])source.Objects.Clone();
            var positions = Enumerable.Range(0, Objects.Length)
                .Where(i => Objects[i].Identifier.Name.StartsWith("wolverine_queue_", StringComparison.Ordinal)).ToArray();
            var queues = positions.Select(i => Objects[i]).ToArray();
            if (queues.Any(q => !QueueOrder.Contains(q.Identifier.Name, StringComparer.Ordinal)))
                throw new InvalidOperationException("Unreviewed native fixture queue metadata.");
            Array.Sort(queues, (a, b) => Array.IndexOf(order, a.Identifier.Name).CompareTo(Array.IndexOf(order, b.Identifier.Name)));
            for (var i = 0; i < positions.Length; i++) Objects[positions[i]] = queues[i];
        }
        public ISchemaObject[] Objects { get; }
        public string Identifier => _source.Identifier;
        public Migrator Migrator => _source.Migrator;
        public Type StorageType => _source.StorageType;
        public void WritePermissions(Migrator rules, TextWriter writer) => _source.WritePermissions(rules, writer);
        public IEnumerable<Type> DependentTypes() => _source.DependentTypes();
    }
}
