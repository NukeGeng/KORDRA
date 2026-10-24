using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>Explicit server plan; configured providers also bind their immutable trust digest.</summary>
public sealed class CredentialTargetRead(Guid installation, CredentialBroker broker, CredentialConsumer<PrivateReadBuffer> consumer, string? configurationDigest = null) : IProtectedCredentialRead
{
    public ProtectedReadPlan Plan { get; } = ProtectedReadPlan.Credential(installation) with { ConsumerConfigurationDigest = configurationDigest };
    public IProtectedCredentialReadReservation? TryReserve()
    {
        if (!Plan.IsValid || consumer.Id != Plan.ConsumerId) return null;
        var reservation = broker.TryReserve(); return reservation is null ? null : new Reserved(reservation, consumer);
    }
    private sealed class Reserved(CredentialBroker.Reservation reservation, CredentialConsumer<PrivateReadBuffer> consumer) : IProtectedCredentialReadReservation
    {
        public Task<PrivateReadBuffer> Admit(TypedReadSelection selection, ReadDependencySnapshot dependency, IProtectedUseBoundary use, CancellationToken ct) =>
            reservation.ReadAndUseAsync(new CredentialUseContextV1(selection.InvocationId,
                new CredentialSelectionV1(dependency.Selection.Plan.InstallationId, selection.TenantId, selection.TargetId, dependency.Selection.Plan.ConsumerId),
                selection.ScopeKind, selection.ScopeId), dependency.Digest, use.RunAsync, consumer, ct);
        public void Dispose() => reservation.Dispose();
    }
}
