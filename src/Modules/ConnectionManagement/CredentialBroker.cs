using System.Runtime.CompilerServices;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ConnectionManagement;

/// <summary>Trusted Host-registered consumer; bytes exist only during this callback. Never serialized.</summary>
public abstract class CredentialConsumer<T>
{
    public abstract string Id { get; }
    public abstract Task<T> ReadAsync(ReadOnlyMemory<byte> credential, CredentialUseContextV1 context, CancellationToken ct);
    public override string ToString() => "CredentialConsumer [redacted]";
}
public sealed class CredentialBroker
{
    private static readonly ConditionalWeakTable<FairAsyncReaderWriterAdmissionGate, SemaphoreSlim> Slots = new();
    private readonly IDbContextFactory<CredentialDbContext> _contexts;
    private readonly FairAsyncReaderWriterAdmissionGate _gate;
    private readonly IAuthorityClock _clock;
    private readonly KeychainAccessConfiguration _configuration;
    private readonly ISecretLookup _lookup;
    private readonly SemaphoreSlim _slots;
    public CredentialBroker(IDbContextFactory<CredentialDbContext> contexts, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, KeychainAccessConfiguration configuration)
        : this(contexts, gate, clock, configuration, new NativeSecretLookup(configuration)) { }
    internal CredentialBroker(IDbContextFactory<CredentialDbContext> contexts, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
        KeychainAccessConfiguration configuration, ISecretLookup lookup)
    { _contexts = contexts; _gate = gate; _clock = clock; _configuration = configuration; _lookup = lookup; _slots = Slots.GetValue(gate, _ => new(2, 2)); }
    public Reservation? TryReserve() => _slots.Wait(0) ? new(this) : null;
    public sealed class Reservation : IDisposable
    {
        private readonly CredentialBroker _owner;
        private int _used, _released, _retained;
        internal Reservation(CredentialBroker owner) => _owner = owner;
        public async Task<T> ReadAndUseAsync<T>(CredentialUseContextV1 context, string expectedDigest,
            Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> useBoundary, CredentialConsumer<T> consumer, CancellationToken ct)
        {
            var selection = context?.Selection;
            if (Interlocked.CompareExchange(ref _used, 1, 0) != 0 || Volatile.Read(ref _released) != 0)
                throw new InvalidOperationException("Credential use unavailable.");
            if (context is null || !context.IsValid || selection is null || selection.InstallationId != _owner._configuration.InstallationId || consumer is null || consumer.Id != selection.ConsumerId)
            { Release(); throw new InvalidOperationException("Credential use unavailable."); }
            SecretLookup? material = null;
            try
            {
                Task<SecretLookup> pending;
                await using (var held = await _owner._gate.AcquireReadAsync(ct))
                {
                    await using var db = await _owner._contexts.CreateDbContextAsync(ct); var store = new CredentialBindingStore(db);
                    var current = await store.ReadCurrentUnderAdmissionAsync(selection, ct);
                    if (current is null || !current.IsValidAt(_owner.Now()) || current.Digest() != expectedDigest) throw new InvalidOperationException("Credential use unavailable.");
                    var locator = await store.ReadLocatorUnderAdmissionAsync(current, ct);
                    if (locator is null || !_owner._configuration.Locators.TryGetValue(locator.Value, out var path)) throw new InvalidOperationException("Credential use unavailable.");
                    ct.ThrowIfCancellationRequested();
                    pending = _owner._lookup.ReadAsync(new(1, Guid.NewGuid(), selection.InstallationId, current.ReferenceId, path), ct);
                }
                material = await pending;
                if (!material.Quiescent) { Interlocked.Exchange(ref _retained, 1); _owner._gate.Close(); throw new InvalidOperationException("Credential outcome unknown."); }
                Release(); // confirmed helper exit frees its slot before C/consumer I/O
                ct.ThrowIfCancellationRequested();
                if (material.Bytes is not { Length: > 0 and <= 16384 }) throw new InvalidOperationException("Credential use unavailable.");
                ct.ThrowIfCancellationRequested();
                return await useBoundary(token => consumer.ReadAsync(material.Bytes, context, token), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new InvalidOperationException("Credential use unavailable."); } // no inner exception or raw provider diagnostics
            finally { material?.Dispose(); if (Volatile.Read(ref _retained) == 0) Release(); }
        }
        private void Release() { if (Interlocked.Exchange(ref _released, 1) == 0) _owner._slots.Release(); }
        public void Dispose() { if (Volatile.Read(ref _used) == 0 && Volatile.Read(ref _retained) == 0) Release(); }
    }
    private DateTimeOffset Now() { try { return _clock.GetTrustedUtcNow(); } catch { _gate.Close(); throw; } }
}
