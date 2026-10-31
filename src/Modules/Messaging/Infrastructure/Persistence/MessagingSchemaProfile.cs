using JasperFx;
using Wolverine;
using Wolverine.Postgresql;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Fixed trusted composition profile; never constructed from a request, envelope or queue name.</summary>
public static class MessagingSchemaProfile
{
    public const string EnvelopeSchema = "msg";
    public const string TransportSchema = "msg_transport";
    public const int Revision = 1;

    public static void Configure(WolverineOptions options, string privateLocator)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(privateLocator)) throw new ArgumentException("Private locator required.", nameof(privateLocator));
        options.UsePostgresqlPersistenceAndTransport(privateLocator, EnvelopeSchema, TransportSchema);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
        options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
        options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
        options.Durability.HandledMessageCleanupBatchSize = 100;
        options.Durability.HandledMessageCleanupMaxBatchesPerCycle = 10;
        options.MaxIncomingEnvelopeDataSize = 32 * 1024;
        // No endpoint/consumer registration, auto-provision or host activation in this library profile.
    }
}
