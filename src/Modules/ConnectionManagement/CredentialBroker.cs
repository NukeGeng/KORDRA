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
/// <summary>Trusted registry consumer; borrowed bytes may be interpreted only inside the current C callback.</summary>
public abstract class RegistryCredentialConsumer<T>
{
    public abstract string Id { get; }
    public abstract Task<T> ReadAsync(ReadOnlyMemory<byte> credential, RegistryCredentialUseContextV1 context, CancellationToken ct);
    public override string ToString() => "RegistryCredentialConsumer [redacted]";
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
        public bool HasUnresolvedHelper => Volatile.Read(ref _retained) != 0;
        public Task<T> ReadAndUseAsync<T>(CredentialUseContextV1 context, string expectedDigest,
            Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> useBoundary, CredentialConsumer<T> consumer, CancellationToken ct)
        {
            var selection = context?.Selection;
            return ReadCoreAsync(context?.IsValid == true && selection?.InstallationId == _owner._configuration.InstallationId &&
                consumer is not null && consumer.Id == selection.ConsumerId, expectedDigest, useBoundary,
                async (db, token) =>
                {
                    var store = new CredentialBindingStore(db);
                    var current = await store.ReadCurrentUnderAdmissionAsync(selection!, token);
                    if (current is null || !current.IsValidAt(_owner.Now()) || current.Digest() != expectedDigest) return null;
                    var locator = await store.ReadLocatorUnderAdmissionAsync(current, token);
                    return locator is null ? null : (current.ReferenceId, locator.Value);
                }, (bytes, token) => consumer!.ReadAsync(bytes, context!, token), ct);
        }
        public Task<T> ReadRegistryAndUseAsync<T>(RegistryCredentialUseContextV1 context, string expectedDigest,
            Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> useBoundary, RegistryCredentialConsumer<T> consumer, CancellationToken ct)
        {
            var selection = context?.Selection;
            return ReadCoreAsync(context?.IsValid == true && selection?.InstallationId == _owner._configuration.InstallationId &&
                consumer is not null && consumer.Id == selection.ConsumerId, expectedDigest, useBoundary,
                async (db, token) =>
                {
                    var store = new RegistryCredentialBindingStore(db);
                    var current = await store.ReadCurrentUnderAdmissionAsync(selection!, token);
                    if (current is null || !current.IsValidAt(_owner.Now()) || current.Digest() != expectedDigest) return null;
                    var locator = await store.ReadLocatorUnderAdmissionAsync(current, token);
                    return locator is null ? null : (current.ReferenceId, locator.Value);
                }, (bytes, token) => consumer!.ReadAsync(bytes, context!, token), ct);
        }
        private async Task<T> ReadCoreAsync<T>(bool contextValid, string expectedDigest,
            Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> useBoundary,
            Func<CredentialDbContext, CancellationToken, Task<(Guid ReferenceId, Guid LocatorId)?>> select,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task<T>> consume, CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _used, 1, 0) != 0 || Volatile.Read(ref _released) != 0)
                throw new InvalidOperationException("Credential use unavailable.");
            if (!contextValid || useBoundary is null || expectedDigest is not { Length: 64 } || !expectedDigest.All(char.IsAsciiHexDigitLower))
            { Release(); throw new InvalidOperationException("Credential use unavailable."); }
            SecretLookup? material = null;
            try
            {
                Task<SecretLookup> pending;
                await using (var held = await _owner._gate.AcquireReadAsync(ct))
                {
                    await using var db = await _owner._contexts.CreateDbContextAsync(ct);
                    var selected = await select(db, ct);
                    if (selected is null || !_owner._configuration.Locators.TryGetValue(selected.Value.LocatorId, out var path))
                        throw new InvalidOperationException("Credential use unavailable.");
                    ct.ThrowIfCancellationRequested();
                    pending = _owner._lookup.ReadAsync(new(1, Guid.NewGuid(), _owner._configuration.InstallationId, selected.Value.ReferenceId, path), ct);
                }
                material = await pending;
                if (!material.Quiescent) { Interlocked.Exchange(ref _retained, 1); _owner._gate.Close(); throw new InvalidOperationException("Credential outcome unknown."); }
                Release(); // confirmed helper exit frees the same shared slot before C/consumer I/O
                ct.ThrowIfCancellationRequested();
                if (material.Bytes is not { Length: > 0 and <= 16384 }) throw new InvalidOperationException("Credential use unavailable.");
                ct.ThrowIfCancellationRequested();
                return await useBoundary(token => consume(material.Bytes, token), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new InvalidOperationException("Credential use unavailable."); }
            finally { material?.Dispose(); if (Volatile.Read(ref _retained) == 0) Release(); }
        }
        private void Release() { if (Interlocked.Exchange(ref _released, 1) == 0) _owner._slots.Release(); }
        public void Dispose() { if (Volatile.Read(ref _used) == 0 && Volatile.Read(ref _retained) == 0) Release(); }
    }
    private DateTimeOffset Now() { try { return _clock.GetTrustedUtcNow(); } catch { _gate.Close(); throw; } }
}
