---
id: CTR-CREDENTIAL-BOUNDARY-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-23
---

# Credential boundary v1 — T08 local Mac pilot design

Owner: MOD-SEC / FEAT-SEC-001. Decision: [accepted ADR-0015](../decisions/0015-credential-boundary-preparation.md). NukeGeng accepted this exact T08 design on 2026-10-23. This is accepted design, not implemented code, a verified model profile or permission to read existing secrets. NukeGeng selected macOS Keychain for this pilot. No additional independent AI review is requested by this document.

## 1. Scope and explicit exclusions

T08 delivers SEC-owned metadata, offline binding/rotation/revocation, a revision-bound in-process credential broker, a bounded macOS Keychain reader and synthetic consumer/HTTPS conformance. It does not enable a real Kubernetes/Helm/network adapter. T09 still decides endpoint/CA/hostname and independent provider identity, minimal authenticated bootstrap, resource/verb/namespace allowlists, SDK/CLI pins and authorized real target access. Reading a credential never establishes those facts.

Keep metadata-only target.read v1 and the existing seven-field response unchanged. It needs no credential binding and must continue working without Keychain. Credential-consuming plans are separate explicit server registrations; they cannot fall back to the metadata plan, another reference, OS environment, arbitrary file, default kubeconfig, shell or another provider. Production has no credential-consuming network plan at the end of T08. The synthetic consumer is test-only. Non-macOS credential resolution returns unsupported without probing a store; metadata reads remain available.

## 2. Ownership, actors and permission

SEC owns ConnectionProfile, SecretReference, binding current heads/history and offline command receipts. IAM owns session/grant/policy evaluation, trusted time and protected invocation ordering. INV/PRJ remain resource/association owners; AUD owns append/transaction results. Host composes their published ports without a module reading another owner's tables.

Binding changes are installation-maintainer operations under the existing API-stopped exclusive process lease, with exact installation identity and manifest approval. They are unreachable over HTTP. Audit action sec.credential-binding labels this offline activity; it is not a new user permission or role. FoundationPermissions and the proposed credential.bind catalog entry remain unchanged. Online credential management needs a later decision.

A credential consumer is an allowlisted implementation selected by Host registration, not a string/body field chosen by a client. Initial consumer ID is inventory.readonly.v1, operation target.read, credential format opaque-bytes-v1. IDs do not authenticate code by themselves: trusted Host supplies the consumer object and checks its registered ID against the plan. Arbitrary in-process code, same-UID malicious programs, OS administrators and the database administrator remain inside the pilot trust boundary; no process sandbox or cryptographic capability is claimed.

## 3. SEC metadata and lifecycle

All IDs are nonempty GUIDs. Tenant-owned composite keys include installationId and tenantId. All revisions are positive Int64 values; overflow fails closed. No plaintext, encrypted secret blob, token hash, keychain password or user-supplied locator path is persisted in SEC, audit, manifests or public fixtures.

| Record | Required fields and rules |
|---|---|
| ConnectionProfile | profileId, installationId, tenantId, targetId, consumerId, operation, providerKind=macos-keychain-file-v1, credentialFormat=opaque-bytes-v1. Immutable identity/target/consumer/provider; no endpoint or credentials. |
| SecretReference | referenceId, profileId, private locatorId. Immutable. Keychain account derives from referenceId; service derives from installationId. A new reference is required for every rotation. |
| Binding head | Unique (installationId, tenantId, targetId, consumerId, operation); current bindingId and current revision. Revision increases across every create/rotate/revoke/replacement, including change-back. |
| Binding revision | Append-only bindingId, revision, profileId, referenceId, state active/revoked, notBeforeUtc, expiresAtUtc, manifestId. Composite links require the same tenant/profile/target tuple. |
| Command receipt | Unique (installationId, manifestId); canonical manifest hash, exact nonsecret result, AUD intent ID/hash. Runtime does not need this table. |

Effective state is expired when trusted UTC is at or after expiresAtUtc; equality denies. Active revisions require notBeforeUtc <= now < expiresAtUtc, with expiry no later than 30 days after this publication. This is a Kordra binding lease, not proof of the actual token's external validity. T09 must also account for provider credential expiry. Revoked is terminal for that bindingId. Rotate may renew an active/expired binding with a new reference and revision; it never restores an old revision. A new binding after revocation requires a new binding/profile identity and exact expected previous head identity/revision. Heads and history never reset or disappear.

SEC local composite constraints prevent cross-tenant/profile/reference links. INV/PRJ current ownership is checked through owner ports under the common fence; no cross-owner table reads or speculative cross-owner FK are introduced. Reuse the pinned EF/Npgsql versions adopted for Foundation. Add app.sec_* tables through the offline migrator. Runtime may SELECT required metadata only; explicitly revoke runtime INSERT/UPDATE/DELETE/TRUNCATE on SEC metadata and all access to command receipts. Generic future-table DML grants must not override these restrictions. Offline admin applies reviewed migrations; startup performs no DDL.

## 4. Offline commands, canonicalization and recovery

Private schemaVersion=1 manifest supports create, rotate, revoke. It contains these properties in canonical order, including explicit nulls for fields unused by an operation:

schemaVersion, manifestId, installationId, tenantId, targetId, consumerId, operation, action, expectedBindingId, expectedRevision, bindingId, profileId, referenceId, locatorId, notBeforeUtc, expiresAtUtc.

consumerId and operation have the exact values in section 2. IDs use lowercase GUID D strings; timestamps use UTC .NET O formatting with seven fractional digits; numbers use invariant base-10 integers; strings are fixed ASCII tokens. An omitted property is invalid even when its required value is null. Use a dedicated Utf8JsonWriter in this order with no indentation; SHA-256 covers UTF-8 bytes. Reject duplicate/unknown properties, non-UTC/invalid time, invalid IDs, nonpositive expected revisions except absent-head create=0, oversized manifests (>16 KiB), secrets and inconsistent action fields. PostgreSQL timestamp precision is microseconds: reject manifest times with nonzero sub-microsecond ticks instead of silently truncating; canonical snapshots are read from persisted values. No arbitrary label, path, URL or script is accepted. Do not hash a general serializer's incidental property order.

For create, profileId/referenceId/locatorId and validity fields are required, bindingId is fresh, and expectedBindingId is null only with expectedRevision=0 for an absent head. For rotate, bindingId/profileId equal the selected current values, referenceId is fresh, and locatorId plus validity are required. For revoke, bindingId is selected current, profileId/referenceId/locatorId/notBeforeUtc/expiresAtUtc must be null; retained values come from the selected stored revision. Every non-absent operation requires expectedBindingId and a positive expectedRevision. Create generates an active revision for an absent head, or replaces an exactly selected revoked head with a new binding/profile identity. Rotate retains binding/profile/target/consumer identity but uses a fresh reference; revoke publishes a terminal revision retaining the selected reference for history. All compare the exact expected current head and revision. Stale selection or changed content for the same manifestId conflicts. Reusing an existing reference for rotation is rejected.

Inspect reads only metadata and returns the canonical hash plus bounded nonsecret impact. Apply requires that exact approved hash, installation identity and exclusive offline lease. Through existing owner ports, confirm active tenant/target/project association; stage SEC head/history/receipt plus IAM clock floor and AUD mutation in one enlisted PostgreSQL transaction. Lease, ownership and time are checked again before commit. Keychain secret reads/writes are not performed inside that transaction. Known setup/save failures roll back as unavailable; only an attempted commit with unresolved outcome closes admission as unknown. Disposing a context after an already established result must not change that result.

Exact committed receipt lookup precedes mutable expiry/head checks and verifies its AUD receipt. Same ID/hash returns the historical result without repeating a Keychain lookup, state mutation or credential use. Changed hash conflicts. Unknown ACK requires renewed offline custody and exact lookup, never a second effective rotation. Missing/contradictory evidence stays unknown. A new reference may point to an item not yet provisioned; this cannot grant credential use: missing/locked/untrusted items remain unavailable. Never treat metadata publication as a successful connection test.

Keychain provisioning and PostgreSQL publication are not an atomic transaction. Provision a fresh item first, then publish metadata. Crash between these steps can leave an unreferenced item, which is not selectable. Rotation publishes the new reference before any separately authorized old-item cleanup. Revoke disables Kordra use; it does not revoke an external token or delete a Keychain item. No automatic orphan cleanup or deletion is added. Cleanup requires exact owner-selected item identities and separate operational authorization.

## 5. Internal contracts and compatibility

SEC Contracts stays BCL-only and exports immutable metadata, not a secret-returning wire API:

- CredentialSelectionV1: installationId, tenantId, targetId, consumerId, operation. All values come from trusted current owner selection and registered consumer.
- CredentialBindingSnapshotV1: schemaVersion=1, selection, bindingId, revision, profileId, referenceId, providerKind, credentialFormat, state, notBeforeUtc, expiresAtUtc and canonical metadata digest. locatorId remains inside SEC broker/provider plumbing, not client DTOs. Its installation mapping is loaded and sealed once per boot; no runtime path/ACL/executable hot reload is supported. Changing that mapping requires API-stopped custody and restart, which invalidates the old boot/session evidence.
- ICredentialBindingReader.ReadCurrentUnderAdmissionAsync(selection, ct): exact current metadata or null; caller holds the common read fence. No Keychain access here.

IAM Contracts adds an owner-neutral read-dependency selection/snapshot port. Required dependency kind credential-binding has exactly one snapshot; its digest binds all SEC fields above, including current head identity/revision, selection and effective expiry. The SEC metadata digest is lowercase SHA-256 over canonical UTF-8 JSON in this exact order: schemaVersion, installationId, tenantId, targetId, consumerId, operation, bindingId, revision, profileId, referenceId, providerKind, credentialFormat, state, notBeforeUtc, expiresAtUtc. Use section 4 encoding rules and persisted UTC values. Host adapts SEC metadata to this port. IAM runtime references its own Contracts, not SEC runtime/Contracts. SEC runtime may reference IAM Contracts for the existing trusted clock/offline lease, INV Contracts for owner observations and AUD Contracts; Host supplies any additional current owner checks through typed ports. No circular runtime edge is permitted.

Host selects a closed read plan before U: metadata-v1 has no credential dependency; credential-bound-v1 requires the exact registered consumer and SEC dependency. Missing resolver, missing snapshot, unsupported consumer/provider or invalid current binding denies. The plan is not accepted from HTTP input. Credential-bound plans include plan ID, consumer and dependency snapshot in an explicitly versioned canonical authority digest (version 2); no comparison may silently drop SEC evidence. Existing metadata-only digest calculation can stay byte-compatible; never reinterpret a historical digest/receipt as carrying SEC evidence. Existing metadata-only U/D has no implicit credential authority.

Keep existing IProtectedTargetRead and metadata consumers working. Add a credential-operation adapter that receives an IAM-owned IProtectedUseBoundary for its one admitted invocation. Its RunAsync callback may start only the registered consumer once, after section 6's current revalidation/audit. It is not a reusable permit and is not exported to HTTP/serialized messages. Secret lease and broker are runtime-internal types with redacted ToString; no byte-returning public Contracts method. The trusted consumer gets a bounded byte view only during its callback and must not retain/copy it into caches, strings, exceptions or logs.

AuthorityCommitment gains a trailing optional purpose with an explicit enum ReadAdmission/CredentialConsumption; old constructors default to ReadAdmission. Only U permits these purposes; D rejects CredentialConsumption. AUD schemaVersion 1 and existing U/D stage values remain: ordinary U safeReason authority-<digest>, credential consumption U safeReason credential-use-<digest>, and D remains distinct. Both reason forms fit the existing token/length rules. Credential consumption therefore has its own unique durable intent without inventing a new AUD stage or reusing the original U receipt. Update actual constructors/adapters/fixtures together and reject unknown purpose values. No external API/message schema version changes are required because these are additive in-process contracts; the seven-field metadata wire API remains unchanged.

## 6. U → lookup → C → consumer → D

C below is the credential-consumption commitment, distinct from response disclosure D.

1. Reserve existing invocation capacity and an immediate broker slot; no unbounded queue. Under common read admission, resolve current subject/grant/definition/owner/resource-policy plus the server plan and SEC dependency. Deny mismatches, revoked/not-yet-valid/expired state or unsupported plan before starting a helper. Include binding expiry in the earliest expiry across all authority.
2. Append/confirm original U intent. Recheck time/cancellation. Synchronously admit one exact local Keychain lookup for this immutable selection under U; then release admission while awaiting the helper outside the gate. No fallback or retargeting. U authorizes this bounded local lookup only; it grants no right to invoke a consumer or disclose a result later.
3. After the helper exits and its result has been fully validated, reacquire the common read gate for C. Resolve all original authority and SEC evidence again and require exact digest equality with U; use the minimum original/current expiry. Any grant/owner/policy/binding change, including rotate-away-and-back, destroys the result before the consumer runs.
4. Confirm a new durable CredentialConsumption intent for this invocation, recheck time/cancellation, and synchronously admit exactly one registered consumer callback while still fenced. C authorizes only that immutable operation. Release the gate while awaiting bounded consumer I/O. Unknown C commit closes admission; unavailable/rejected C discards material with no callback. No retry or reusable lease follows.
5. Erase broker-owned secret buffers when the callback completes/fails/cancels. Before D, reacquire admission, re-resolve and compare all U/C owner and SEC evidence, bound validity by the earliest expiry, append distinct D digest/size intent, then use the existing caller-bound response handoff. Keep its bounded write/flush fence and partial-output/unknown behavior. Secret bytes never enter PrivateReadBuffer.

All SEC head mutations are registered invalidating writers under the same fence, although T08 exposes them only offline with API stopped. Synthetic tests exercise the writer interleavings through owned handlers, not direct unfenced database edits. If revocation commits before C, no consumer starts; if C already admitted a read, that read may finish afterward but no new C is allowed and changed evidence prevents D. Already dispatched external read packets and already handed-off bytes are not retractable. This design accepts that limited residual; T09 must restrict the consumer to bounded read-only operations. Never describe it as instant revocation of provider access.

There is no separate credential.read user permission: a matching SEC binding is a restriction on a particular authorized target.read plan, not a grant. Platform scope has no implicit tenant credential access. The browser receives only the pre-existing allowlisted metadata or a sanitized failure, never binding locators or secret material.

## 7. Keychain implementation and process boundary

Select the file-based Keychain implementation for the CLI-hosted, user-login Mac pilot. Apple recommends data-protection Keychain for app-style access but documents entitlement/provisioning requirements for command-line tools; this bounded pilot does not silently add an app bundle, access group or Apple Developer account requirement. File-based Keychain has older APIs and a longer-term migration risk. Revisit this decision for packaged desktop distribution, background daemon, remote worker or production hardening. Do not promise Mac Keychain access remotely.

Use native Security/CoreFoundation APIs through BCL interop in a short-lived child mode of the same Kordra.Api executable; no new service, host project, SDK package, /usr/bin/security command or shell. Runtime lookup is read-only. Parent launches the absolute installed apphost, never a PATH lookup, dotnet command supplied by a caller or mutable per-request executable. A real provider requires a stable owner-provisioned executable identity/ACL; generic dotnet-wide or Allow all applications access is not acceptable. Replacing/unsigned-changing that executable requires renewed owner ACL provisioning. Do not auto-grant access or trust certificates/signatures.

Before the first Keychain call, child disables user interaction and treats failure to do so as unavailable. Open only the exact keychain identified by private installation locatorId; no default/search-list fallback. Use SecItemCopyMatching for one generic-password item, with an explicit single-keychain search list, service vn.id.kordra.credentials/<installationId-D> and account <referenceId-D>. Parent checks locatorId belongs to the installation and immutable reference. The private keychain path is not stored in a tenant request or published manifest. Only owner-approved private locator mappings are loaded at boot; a missing mapping fails closed. Manual replacement of a keychain file or editing an existing item while API runs is unsupported. Provisioning must use a fresh reference instead of in-place item updates. SEC cannot detect arbitrary malicious same-UID/administrator edits without a different trust model; no such tamper-proof claim is made. Returned native data must be the expected type and 1–16384 bytes. Do not normalize, decode, print or trim secret bytes. Not-found, locked store, access denial, UI requirement and native errors map to fixed unavailable categories without raw OS diagnostics.

SecItemCopyMatching is blocking; Task.Run/cancellation alone cannot establish completion. Child has no HTTP listener, DB connection or external provider action. It returns through a private anonymous pipe and exits. Parent validates exact child PID/handle ownership, protocol and process exit before C can occur; EOF or secret bytes without successful exit are not success. Kill/reap only this invocation's owned child, never another process or securityd. If exit cannot be established within the cleanup budget, close installation admission, retain the unresolved slot and report unknown; never release a fence while unresolved credential-consumer work can continue.

Child startup accepts only a fixed internal mode plus inherited pipe handles. It does not load API configuration, migrate, open a listener or run user-provided commands. Clear inherited credentials/environment except strictly required nonsecret runtime/bootstrap entries; close unrelated inherited handles. The child mode is not a privilege elevation: same OS UID/Keychain ACL remains the trust boundary. Main Kordra invocation still requires IAM/SEC authority before launching it.

## 8. Private child protocol and bounds

Protocol is private IPC version 1, not a public business message. Redirected streams must never be inherited by a terminal, diagnostic logger or shell. Input frame: 4-byte unsigned big-endian length followed by UTF-8 JSON (max 4096 bytes), exact keys in this order: schemaVersion, requestId, installationId, referenceId, keychainPath. Reject unknown/duplicate keys, empty IDs, invalid absolute configured path or trailing bytes. IDs are metadata, not authority; parent has already selected them.

Output frame: ASCII KSEC, one version byte=1, status byte (0 success, 1 unavailable, 2 invalid), 16 request-ID bytes in RFC 4122/network order, 4-byte unsigned big-endian payload length, payload, then EOF. The fixed output header is 26 bytes. Child exits 0 only after one complete handled-status frame; malformed input/internal protocol failure exits nonzero and cannot succeed. Only success may carry 1–16384 raw bytes; other statuses require length zero. Exact request ID, one frame, no trailing data and successful process exit are mandatory. EOF/oversize/wrong identity/extra output are unavailable and erase the buffer. Stderr is not retained or logged and is drained with a fixed 1024-byte limit; excess terminates the child. Never call ReadToEnd on unbounded provider output.

Initial bounds: at most 2 Keychain children per installation, inside the existing 16-invocation capacity; slot acquisition is immediate/nonqueued. A slot is released only after confirmed child exit. Keychain lookup has 2 seconds, including process startup; cancellation/kill/reap has at most 500 ms and consumes the existing 5-second U-to-consumer provider budget rather than resetting it. The overall 10-second invocation, 1-second gate acquisition, 2-second gate-held stage and 1-second audit budgets remain. C has its own existing-size gate stage but grants no extra overall/provider time. Over-budget results deny/abort, never truncate or retry. A permanently hung child closes new admission rather than accumulating replacements.

Parent/child release native objects and zero owned mutable byte buffers in finally blocks. No managed secret strings or secret fingerprints are created. Native Keychain, OS pipe buffers, runtime/OS memory copies and crash-dump policy prevent a guarantee that every historical copy is erased; do not claim secure hardware isolation or perfect zeroization. Disable secret-bearing dumps/log capture operationally before live use; root/same-user process inspection remains outside the pilot guarantee.

## 9. Implementation slices and consumers

Use the existing reserved src/Modules/ConnectionManagement owner root (MOD-SEC), not a second ConnectionSecrets directory. Exact source/test mappings are populated only once files exist.

| Slice | Planned implementation and required evidence |
|---|---|
| T08.1 SEC metadata | ConnectionManagement runtime/Contracts; pinned EF/Npgsql; SEC context/composite constraints/head-history/receipt; owner tests and actual runtime SQL grants. |
| T08.2 Offline administration | Existing API OfflineAdministration/Credentials folder and DbMigrator registration; manifest inspect/apply/lookup; enlisted SEC/IAM-clock/AUD proof and commit-ACK recovery. No online binding route. |
| T08.3 Authority dependency | IAM Contracts/runtime, Host Authorization/Composition adapters; exact U/C/D dependency comparison, earliest expiry, default metadata compatibility and writer registry. |
| T08.4 Broker and child | SEC Keychain infrastructure + API internal child entrypoint/launcher, bounded binary pipe, cancellation/reap, redaction/cleanup, stable installed apphost checks. |
| T08.5 Conformance | New ConnectionManagement.Tests, existing IAM/INV/AUD/architecture tests and real HTTPS with synthetic consumer; separately authorized isolated Keychain fixture on Mac. |

API folder-organization changes are already implemented on a separate local branch; integrate that layout before adding T08 Host files. Preserve T07's exact observation port/allowlist and existing checkpoint receipts. No automatic replay or model dispatch is part of migration. Update actual assembly graph/lock/source mappings and OpenAPI source identities without adding unrelated packages, schemas or services. Contracts assemblies stay BCL-only; Product build/tests remain independent of Codex credentials.

## 10. Acceptance and operational boundary

These are planned cases, not passed results:

| Cases | Required observations |
|---|---|
| SEC08-01 | Golden project/environment scope: current session/grant/policy/owner + SEC binding; U, separate C and D intent IDs; only existing sanitized fields reach actual HTTPS. |
| SEC08-02 | Wrong tenant/target/consumer/operation/provider/format/reference, missing dependency or platform-only grant: zero child launches/consumer calls/body bytes. |
| SEC08-03 | Not-before, equality expiry, revoked/rotated binding, old-reference reuse and ABA/change-back: deny; earliest expiry caps C/D. |
| SEC08-04 | While lookup waits, revoke grant/binding or change policy/owner through fenced writers: helper output discarded and zero consumer calls. While consumer waits: zero D/body after invalidation. Both C-versus-writer orderings tested. |
| SEC08-05 | U/C/D audit unavailable, rejection, setup cancellation and unknown ACK: no unauthorized next stage; only genuinely unresolved commits fence the host. |
| SEC08-06 | Exact duplicate create/rotate/revoke, stale expected head, changed hash, committed receipt after expiry/revoke and lost ACK: one effective mutation and correct historical lookup. |
| SEC08-07 | IPC truncation/extra output/wrong ID/oversize/native wrong type, locked/not-found/UI-required keychain and unsupported OS: no fallback, fixed failure, no secret in output/log/audit/exception. |
| SEC08-08 | Hung child, cancellation before/after spawn, secret then nonzero exit, delayed exit and failed reap: bounded owned-child cleanup; no new work after closure, no unrelated process killed. |
| SEC08-09 | 2-child/16-invocation bounds, no queue/retry, full provider/overall deadlines, fair writer progress and parent/callback cleanup. Never weaken existing fairness/partial-write tests. |
| SEC08-10 | PostgreSQL owner/tenant composite constraints, app DDL/DML denial, additive migration/restart, unknown recovery and forbidden assembly edges. No startup migration. |
| SEC08-11 | Exact disposable file-based Keychain fixture: native raw-byte roundtrip, intended executable ACL, no-prompt locked case, scoped search collision and rotation/revoke proof. Only newly created synthetic items/store; no personal/login/system secret reads. |
| SEC08-12 | Default metadata-only API remains Keychain-independent; no production synthetic consumer, provider network read, credential HTTP API or permission-catalog widening. |

Acceptance of this design alone does not authorize reading or changing existing Keychains, personal/company credentials or any cluster. Live test permission must explicitly name a new isolated temporary keychain, synthetic items, test executable, create/read/delete scope and exact cleanup. A proposed convenient permission is to create one UUID-named test-only keychain under the private test evidence directory, provision random in-memory synthetic items restricted to the test apphost, read only those items, delete through Keychain APIs, and remove only owned leftovers after verified cleanup. Do not silently include this in a design-only approval.

Completion reporting separates implemented source, synthetic deterministic tests, actual isolated Keychain proof, unsupported/configuration-dependent cases and hosted PR review. Missing platform/ACL/fixture evidence is not a pass; no full T08/provider or Operator Pilot readiness claim follows from fake-only tests. Sol architecture work ends with this proposal/decision; implementation follows human acceptance and Codex Auto PR Review remains the PR reviewer.

## Primary platform references

[Apple TN3137](https://developer.apple.com/documentation/technotes/tn3137-on-mac-keychains) distinguishes file-based/data-protection implementations, explicit search-list behavior and signing/provisioning requirements. [SecItemCopyMatching](https://developer.apple.com/documentation/security/secitemcopymatching(_:_:)) documents its blocking execution. The process isolation, numeric bounds, metadata and authorization protocol above are Kordra design decisions, not Apple guarantees. Native support and cleanup must be verified on the actual supported Mac before activation.


## Acceptance and fixture decision — 2026-10-23

The owner accepted this contract and ADR-0015 for the exact T08 scope. Pre-acceptance contract SHA-256: 250251a09262b5795ecb38c68f30559252fc2342d03265cb7923417aec3eb71d. Original bytes and the separate fixture authorization are retained in .amr-local/approvals/adr-0015-t08/. The owner also explicitly permitted section 10's new isolated temporary synthetic-Keychain fixture; the missing operational permission described in that proposal is now satisfied only for that exact scope. No existing item, real provider/cluster access or completed native test is implied. All acceptance cases still require implementation/executed evidence.
