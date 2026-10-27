---
id: FEAT-SEC-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-26
module_id: MOD-SEC
feature_id: FEAT-SEC-001
phase: MVP
source_paths: ["Kordra.slnx", "src/Modules/ConnectionManagement/", "src/Host/Kordra.Api/Kordra.Api.csproj", "src/Host/Kordra.Api/packages.lock.json", "src/Host/Kordra.Api/Program.cs", "src/Host/Kordra.Api/OfflineAdministration/Credentials/", "src/Host/Kordra.Api/Authorization/CurrentCredentialDependencyReader.cs", "src/Host/Kordra.Api/Authorization/CredentialTargetRead.cs", "src/Host/Kordra.Api/Endpoints/CredentialReadHttpHandler.cs", "src/Host/Kordra.Api/Authorization/AuthorityWriterRegistry.cs", "src/Host/Kordra.Api/Composition/ResourceAuthorityServices.cs", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Host/Kordra.DbMigrator/ConnectionManagementMigrationCommand.cs", "src/Modules/IdentityAccess/Contracts/AuthorityAdmission.cs", "src/Modules/IdentityAccess/Authorization/ProtectedReadAdmission.cs", "src/Modules/IdentityAccess/Authentication/LocalAuthorityFacts.cs"]
test_paths: ["tests/ConnectionManagement.Tests/", "tests/IdentityAccess.Tests/T08CredentialHttpTests.cs", "tests/IdentityAccess.Tests/CurrentAuthorityHttpTests.cs", "tests/IdentityAccess.Tests/T07BoundedReadHttpTests.cs", "tests/IdentityAccess.Tests/LocalAuthenticationTestHost.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
---

# FEAT-SEC-001 — Bind credentials safely to deployment targets

## OP-P0 implementation slice — proposed 2026-10-20

Luna T08 follows current IAM/resource enforcement. Actor authorized User or scoped trusted adapter; `credential.bind` target and `target.manage` where the catalog requires it. Preserve ConnectionProfile/SecretReference/CredentialBinding ownership in SEC. Bind profile/target/tenant/consumer/allowed operation to an opaque secret locator and monotonic rotation revision. Lifecycle Active/Revoked/Expired; same label cannot switch provider/target. Never return resolved secret material to browser, messages, logs or audit.

Provider/key-custody, TLS/bootstrap identity, exact resource RBAC and supported package/CLI pins remain an explicit T08 adoption gate; examples such as Keychain are not interchangeable selected providers. Separate in-process authorized resolution from runtime observation. Resolve only for the exact operation and dispose transient material after bounded use; provider-specific rotation/revocation and reconnect semantics need evidence. A local secret reference does not automatically become accessible to a future remote worker.

Acceptance SEC-P0: wrong tenant/target/consumer, unsupported operation/provider/reference, revoked/expired/rotated binding and TLS identity mismatch deny before a provider read. Lost authorization while resolve/read waits withholds disclosure; no broader credential/context fallback. Duplicate bind uses exact key/body and stale ETag rejects. Synthetic secret markers are absent in API/log/audit/exception paths and transient material is not persisted. Fake provider proves policy only; actual provider test requires separately authorized local fixture/target. Follow [handoff](../../../engineering/luna-implementation-handoff.md); no dependency/secret access adopted by writing this design.


## User story / outcome
**Actor:** Operator / Security Admin. **Goal:** Bind credentials safely to deployment targets. **Primary owner:** `MOD-SEC`. **Phase:** MVP.

## Pre-conditions
Target exists with verified tenant and scope.

## Main flow
Choose secret provider + reference → authorize binding → connect test with temporary credential → save reference metadata → audit rotation.

## Alternatives / exceptional flow
Provider unavailable; rotation invalidates active sessions; permission revoked after approval.

## Messages and integration contracts
`credential.binding.changed.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Secret value in API response; connection test against wrong tenant; revoked ref still accepted.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
No plaintext secret appears in DB/docs/events/logs; scoped binding and rotation audited.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## I0 — OP-P0 credential trust seam proposal

**Proposed only; no product implementation, accepted policy or operational authorization.** This bounded increment prepares OP-01/OP-10 for the read-only [OP-P0 pilot](../../../product/operator-pilot-plan.md), following the [inventory proposal](../../infrastructure-inventory/features/register-target.md) and [profile/environment proposal](../../project-environment/features/environment-catalog.md). The original MVP phase and broader binding/rotation flow above remain. I0 describes constraints for future authorized reads; it does not perform binding, connection tests, rotation or credential access, and does not broaden [OSS Beta acceptance](../../../product/oss-beta-acceptance.md).

Task `F0-OPP0-SEC-SEAM` has contributor actor `repository-contributor`, permission `repository.write` and local documentation scope only. All field names and outcome descriptions below are illustrative proposals pending owner review, not adopted schemas, permission mappings or runtime transitions. The existing message candidate remains unaccepted; no new API/event contract or consumer migration is introduced.

### Proposed local-client versus hosted-service boundary

[MOD-SEC](../module.md) retains ownership of ConnectionProfile, SecretReference, CredentialBinding and RotationStatus. MOD-PRJ supplies project/environment associations, MOD-INV supplies target ownership/identity evidence, and IAM supplies trusted principal and authorization decisions through reviewed contracts. No module reads another owner's internal storage or takes over its domain ownership.

| Execution location | Proposed reference and trust constraint |
|---|---|
| Local Mac client / explicitly authorized local read adapter | A local reference may identify a credential in an approved local store. macOS Keychain is an example, not a selected provider or browser capability. Possessing a reference, unlocking a store or having a local login does not establish tenant, target or read permission. Only an approved local credential-consuming boundary may resolve it for the bounded read; UI/profile views receive authorized metadata and sanitized outcomes only. |
| Independently hosted API / worker | Requires its own authenticated service identity and explicitly authorized provider/reference access for the particular tenant, target and operation. A Mac Keychain item or client-local identifier is not remotely resolvable by assumption. With only a local reference, report unavailable/not configured; do not copy, upload or serialize the secret, proxy unrestricted access through the Mac, or substitute another credential. Any future provisioning/delegation path requires a separate reviewed decision. |
| Client-to-API and API-to-worker communication | Carry only reviewed, disclosure-authorized opaque references and bound operation context, never bearer tokens, passwords, private keys or kubeconfig as business payloads. A reference is metadata, not a bearer capability or grant. Neither a caller-supplied reference nor a successful API check replaces worker authentication, current scoped authorization or provider access checks at the consuming boundary. |

Each proposed resolver must constrain which provider/reference it can access; an opaque ID cannot select an arbitrary vault path, file, URL or another tenant's secret. Keep provider locators and private connection details out of public evidence. No secret provider, transport, workload-identity framework, local helper, host or storage technology is selected here. [Operator Workstation requirements](../../../product/operator-workstation.md) require an independent worker for Mac-close survival; local credential access cannot supply that guarantee.

### Proposed independent tenant, target and operation binding

1. Resolve the authenticated user/service principal, trusted tenant membership/delegation, resource ownership and current permission through their authoritative owners. Separately verify the reference/binding owner and the target's organization/project/environment association; reject cross-tenant associations even if labels or reference strings match. Caller `tenantId`, profile/context names, role names and provider credential claims cannot establish Kordra ownership. Deny by default; platform scope grants no implicit tenant access.
2. An illustrative binding could associate the authorized reference/revision and intended consumer identity with trusted tenant/environment/target references, expected provider identity, bounded resource scope, permitted read operation and validity information. Independently trusted target expectation must not be derived solely from the same untrusted profile or credential being checked. Exact binding establishment, fields and contract ownership require review before implementation.
3. Before resolving or using a credential, verify current authority for that reference, consumer, operation and target scope. Authenticate the destination against approved endpoint and transport trust before presenting credentials; reject untrusted certificates, hostname mismatch or requests to disable validation. Confirm provider identity against the trusted target expectation before accepting inventory evidence. If provider-specific identity discovery needs an authenticated read, its minimal bootstrap operation and trust source require an explicit design decision; missing identity cannot become a verified target or justify broader credential exposure.
4. Keep Kordra action authorization separate from provider privileges. Existing catalog candidates `credential.bind`, `target.read` and `target.manage` retain their documented scopes: binding permission never grants plaintext access, read permission does not grant binding/management, and management does not bypass target/resource policy. Exact connection-test/resolution/read action mapping and service delegation remain unresolved. No new permission or role-name rule is accepted here.
5. For OP-P0 use only a future owner-approved least-privilege read operation/resource set. Enforce that set at the adapter even if a supplied credential is overprivileged; do not request broad administrative credentials by default. Read access does not imply secret enumeration, exec, arbitrary shell, mutation, broker consume/ACK/purge or device publish permission. No fallback to another target, namespace, credential or insecure transport.

### Proposed expiry, revocation and reconnect behavior

- Check both Kordra grant/binding validity and provider credential validity before each permitted attempt, including queued work at the worker boundary. A successful connection test or old approval is not continuing authority. Missing, expired, revoked, stale or unverifiable authority prevents credential use and protected result disclosure; provider unavailability cannot be treated as valid cached authority. Precise validity/recheck and cache mechanisms remain decisions, not implemented guarantees.
- Rotation or a changed reference/binding revision must not silently retarget or upgrade the authority of pending work. Revalidate against the intended target and operation; reject stale dispatch/results rather than substitute a new credential automatically. An already issued provider credential/session may outlive Kordra revocation: provider-side invalidation, session lifetime and residual exposure need explicit owner decisions. Do not claim instantaneous remote revocation.
- On worker reconnect/restart, Mac wake or profile/context switch, reauthenticate the consumer and revalidate current tenant/target association, permission, binding revision, expiry and provider trust before a fresh bounded read. A remembered selection or opaque reference is not authorization. Late results remain tied to their original scope/revision and cannot populate a new selection or expose data after access is revoked.
- A lost/partial read result remains unavailable/unknown until a new authorized observation. Any bounded read retry needs current checks and a deadline; duplicate deliveries and worker crash cannot trigger binding/rotation or mutation replay. This seam does not define a new retry/state machine or make unknown agent outcomes successful. A write approval, expired or otherwise, cannot expand the OP-P0 read operation set or bypass its independent read authorization.

### Proposed redacted audit/log outcomes

Record only disclosure-authorized metadata such as actor/service reference, scoped resource/reference revision, correlation/read-attempt reference, policy version, time and sanitized reason/outcome. Distinguish local policy denial, invalid binding, provider authentication rejection, transport/trust failure and unavailable evidence; connection failure is not evidence of workload health. Reference existence and private identifiers must not leak to an unauthorized caller through errors, logs, cache, exports or audit queries.

No plaintext secret belongs in application DB, profile/UI/API responses, docs, events, queues/DLQ, audit, logs, traces or diagnostic exports. Future approved secret stores are distinct from application reference metadata; encryption at rest alone does not grant application access. Credential material may be used transiently only inside the approved resolver/adapter boundary, with lifetime/cache/disposal controls still to be designed. Provider errors, response bodies, headers, URLs and exception chains are untrusted output: prefer allowlisted sanitized reasons and redact before persistence or emission, including failures during resolution and rotation. Do not rely on the UI to redact already logged material. Audit storage, access, retention and failure behavior need review with the audit owner.

### Planned acceptance / negative matrix — not executed

Future unit/contract/integration cases use synthetic references and secret markers, fake identity/provider stores, fake clocks and an instrumented adapter. These are proposed assertions, not test files or passing product tests; no live secret, endpoint or provider probe is required.

| Case / synthetic setup | Proposed acceptance assertions |
|---|---|
| Valid local or hosted read | Independently trusted same-tenant target/binding, authorized consumer, valid reference and matching provider identity permit only the named bounded read. Each location resolves only its explicitly authorized store reference; metadata/results are sanitized and mutation count is zero. |
| Cross-tenant binding / missing permission | Forge caller tenant, reference owner or environment association; use a platform-only principal or omit scoped read/provider access: deny before secret resolution/provider dispatch, reveal no protected reference existence or cached data, and retain only authorized sanitized denial evidence. Role labels and binding permission do not override denial. |
| Wrong target / reference substitution | Reuse a valid reference for a different target, namespace, consumer, revision or arbitrary provider locator; match aliases but change provider identity: reject use/association and never expose credentials to an untrusted destination. No replacement credential or broader-scope retry. |
| Revoked/expired reference or grant | Advance fake clock or revoke before dispatch, during a delayed read and before result disclosure: no new credential use under invalid authority and no protected stale result/cache exposure. Distinguish provider session revocation limitations; do not assert remote cancellation without evidence. |
| Secret disclosure | Inject markers in provider errors, headers, URLs, payloads and nested exceptions, including failures and rotation paths: no markers in application persistence, API/UI, events/DLQ, logs, traces, audit or exports. Unauthorized callers cannot infer another tenant's reference/locator. |
| Local-only reference at hosted worker | Make a synthetic local store readable only by the client: worker returns sanitized unavailable/not configured, never reads the Mac store or requests plaintext upload, and performs no fallback provider call. Client closure cannot create a durable-execution claim. |
| Worker reconnect / changed binding | Disconnect/restart with expired consumer identity, revoked grant, changed target or rotated revision: reauthenticate/revalidate before fresh reads; reject delayed old-context results. Duplicate delivery does not replay mutation or binding changes, and reconnect does not renew observation timestamps. |
| Insecure certificate bypass | Inject untrusted/expired certificate, hostname mismatch or validation-disable option: reject connection without releasing credentials to the untrusted peer, no bypass retry and no verified-target/success claim. Trust provisioning remains an explicit decision. |
| Overprivileged credential / expired approval | Attempt write, secret enumeration, exec, broker ACK or device publish using an overprivileged fake credential or old write approval: adapter rejects outside the read set; no mutation occurs. A valid read still needs current independent permission. |
| Duplicate read / crash / unknown result | Repeat delivery, reorder results or crash after dispatch: keep scope/revision/correlation, withhold inconclusive or unauthorized output and recheck before any bounded fresh read. No automatic rotation, mutation replay or success inferred from ACK/reconnect. |

### Unresolved decisions and evidence boundary

- `[NEEDS-DECISION]` Assign module/security owners; choose first provider and exact least-privilege read set, provider/reference allowlisting, local versus hosted resolution placement and authorized consumer identities. Keychain, server secret managers and Kubernetes Secrets are examples, not selected interchangeable stores.
- `[NEEDS-DECISION]` Trusted identity/tenant resolution, binding establishment and independent provider identity/bootstrap trust; TLS trust provisioning, service authentication/delegation and exact permission/action mapping. Preserve deny-by-default and tenant/platform separation while these decisions remain open; no insecure-certificate exception is authorized.
- `[NEEDS-DECISION]` Reference/profile storage, encryption/key custody, transient secret lifetime/disposal, caching, expiry/revalidation timing, rotation revision handling, provider-session revocation and audit redaction/access/retention/failure policy. Resolve owner contracts and consumers before implementation; do not invent a persistence or messaging baseline.
- `[NEEDS-DECISION]` Rights-owner consent for company access, credential provisioning/handling, private evidence storage, IP reuse and publication. This proposal grants none; no real secret reads, infrastructure operations or network access occur in this task.
- [ADR-0008](../../../decisions/0008-domain-bootstrap-validation.md) remains accepted only for the narrow artifact-domain/test bootstrap. [ADR-0003](../../../decisions/0003-tenant-authorization.md) and [ADR-0009](../../../decisions/0009-source-architecture.md) remain proposed. Host/trust architecture and policy decisions require their authorized reviewers; this specification accepts no ADR, permission/state-machine change, dependency or new host/project.
- Product source/tests remain **not implemented** and planned cases above have not run. The coordinator must verify the current task-bound execution receipt, deterministic gates, independent native critical review and contributor finding triage for the exact content before task acceptance, then reconcile the existing roadmap/evidence. No historical `validation_failed` task is replayed or promoted; no subsequent task is authorized by this edit.

## Source & test traceability
- Source files: current scoped T08 paths are declared in the feature front matter; broader credential/provider capability remains proposed.
- Test files: owner/native/process/HTTPS/architecture paths are declared in the feature front matter.
- Evidence: local T08 evidence is recorded below; publication/hosted review and broader provider capability remain pending.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.


## T08 bounded design — 2026-10-23

The owner selected macOS Keychain and requested resolution of the T08 design. [Proposed ADR-0015](../../../decisions/0015-credential-boundary-preparation.md) and [Credential boundary v1](../../../contracts/credential-boundary-v1.md) now define the concrete metadata/command/IPC schema, ownership, authority dependencies, U→lookup→C→consumer→D ordering, audit purpose, lifecycle, canonicalization, process limits and SEC08-01–12 acceptance matrix. They replace the earlier preparation-only brief but are still proposed, pending exact human approval; no code, provider read or runtime test is claimed.

Actor: exclusive installation maintainer for offline create/rotate/revoke; current trusted tenant user plus server-selected inventory consumer for a credential-bound target.read. There is no new online credential.bind permission or endpoint. Existing metadata reads remain credential-independent. SEC owns profile/reference/head/history/receipts in the existing reserved ConnectionManagement root. Host adapts SEC evidence to IAM-owned neutral dependency contracts; no circular runtime or cross-owner table dependency is allowed.

The selected local implementation uses an explicit file-based Mac Keychain with native read-only lookup in a bounded child mode of the existing Kordra.Api executable. A separate C commitment prevents secret use after authority changes during lookup; full U/C/D comparison with SEC revision prevents disclosure after rotation/revoke. The installed apphost's ACL/provisioning and actual isolated fixture proof remain operational requirements. No real Kubernetes/Helm/network consumer, credential import/cleanup automation, T09 identity/TLS/RBAC/provider decision, source mapping, dependency or accepted ADR is introduced by writing the design.

Implementation follows contract slices T08.1–T08.5 after approval. A separate explicit permission is needed for create/read/delete of a newly isolated synthetic Keychain fixture; existing personal/company/installation items are excluded. Planned cases are not passed tests. This exact proposal does not complete T08 or native GOAL; prior T07 and historical checkpoint evidence remains untouched.


### Exact T08 design accepted — 2026-10-23

The maintainer accepted ADR-0015 and Credential boundary v1 and separately authorized their new isolated synthetic Keychain fixture. The decision supersedes pending-approval wording in the earlier design history above for this exact T08 scope only. Implement T08.1–T08.5 without re-asking the same provider/design/fixture decision; retain the original proposal hashes and record actual fixture identity before use. The feature remains proposed at module/runtime level: no source/test mappings or implementation evidence exist yet, and no real network consumer or existing credential access is authorized.


## T08 local implementation — 2026-10-24

Actor/permission: API-stopped installation maintainer for exact manifest/hash create/rotate/revoke; current tenant User with target.read and exact resource/registered consumer/SEC binding for credential U/C/D. Source and test paths now map actual files under accepted ADR-0015. This is local implementation evidence, not a complete broader credential MVP or a production-ready provider.

T08.1 supplies BCL metadata/consumer-context contracts, canonical manifests, own EF context/composite constraints and additive migrations with runtime SELECT-only grants. Heads/history advance monotonically; references are immutable and unique per installation. T08.2 supplies offline CLI inspect/apply, SEC/IAM clock-floor/AUD enlistment and exact receipt reconciliation with stored audit time/result/history validation. No setup failure is mislabeled an unknown commit. Down migrations refuse to erase authority/receipt history.

T08.3 supplies neutral IAM read-dependency evidence, explicit server plans, a separate C intent before consumer admission, earliest-expiry/current U/C/D comparison and invocation-local reservations. Consumer context preserves the original invocation/tenant/target/scope. Existing metadata-only behavior remains credential-independent and no credential-bound route/network consumer is registered by Product. T08.4 supplies the bounded broker, read-only native child mode in the existing apphost, exact private IPC, cleared owned buffers, two helper slots and bounded startup/kill/reap. Late/unreaped work closes admission instead of launching replacements. Keychain-mutating imports reside only in test assembly.

T08.5 has owner/manifest/SQL/receipt tests, malformed IPC and child lifecycle fault tests, actual-owner HTTPS secret/redaction/revocation/expiry/audit/cancellation/capacity proofs, explicit restored/PE architecture checks and a native synthetic fixture. Actual native proof creates only a new UUID private store and synthetic service/account items with designated apphost ACL, validates raw bytes/missing/locked/service selection, then deletes the fixture. Permission/evidence stays private under .amr-local/t08; no existing item or cluster is accessed. Exact final gate source digest/counts are recorded separately after validation; planned SEC08 cases are not silently treated as executed.

Installation configuration still requires a stable apphost/hash/Keychain ACL and private immutable locator mapping before any future registered consumer. Mac native proof is platform-specific; Linux validates unsupported behavior and cannot claim native Keychain execution. No runtime startup migration, online credential.bind permission, token import/automatic deletion, remote worker custody, Kubernetes/Helm reader, T09 adoption or new debt is introduced. See [runbook](../../../operations/credential-boundary-v1.md).

### Local T08 validation result — 2026-10-24

Canonical exact-base AMR gates passed on the stable implementation tree: 218 tooling tests and 524 Product tests, zero failures/skips; locked restore, Release build, docs/scope/submodule pin and source ownership checks passed. The Product count includes 32 SEC/IPC/process/native cases and 18 T08 HTTPS cases covering both project and environment scope. Strict doccheck checked 232 Markdown files, 214 IDs and 21 modules with zero errors/warnings. The Mac native fixture completed exact raw-byte/service/ACL/missing/locked checks and verified deletion. Counts establish local validation only; no hosted CI/PR review, existing-secret access, real installation migration or cluster result is claimed. Final receipts remain private under .amr-local/t08/implementation/. Accepted design, checkpoint history and T07 evidence are preserved; no debt or additional architecture approval was introduced.

## PR15 known launch failure correction — 2026-10-24

FEAT-SEC-001 / MOD-SEC addresses canonical finding PR15-SEC-LAUNCH-001 (provider comment 4366071511). NativeSecretLookup observes a completed launch task: a faulted/cancelled start or null return with no helper is unavailable/quiescent, so the broker releases its slot without closing shared admission. An incomplete launch still retains its cleanup ownership/slot and closes admission; a completed successful launch is captured and reaped even if cancellation won. No new schema, permission, provider, package or architecture decision is introduced under accepted ADR-0015/Credential boundary v1.

Regression evidence first reproduced the known-failure classification and actual HTTPS admission failure on the original head. Added lifecycle cases for a throwing factory/null launch and an actual-owner HTTPS case that performs three failed starts across the two-slot budget, acquires the writer fence after each, admits no consumer/C, then successfully reads the unchanged metadata route. The native-process factory is controlled in tests; no existing secret or provider is accessed. Final source-bound gate evidence is private under .amr-local/t08/pr15-launch-failure/. Local repair evidence does not assert provider follow-up or merge approval.

## T09.1 transport supplement proposed — preparation before acceptance, 2026-10-24

[ADR-0016](../../../decisions/0016-kubernetes-workload-observation.md) proposes the Kubernetes workload consumer and a narrow post-C managed-header exception for the .NET transport. [NEEDS-DECISION]: the exception is not accepted by existing ADR-0015 and must not be implemented until exact approval. SEC retains raw-byte Keychain IPC/broker, binding ownership, lifecycle and current U/C/D. No existing secret, real target, credential API or new provider is enabled by this design note.


Accepted scope update — 2026-10-24: NukeGeng approved exact ADR-0016 and Kubernetes workloads v1 for T09.1a–d, including the post-C managed-header exception, offline private-PKI residual, exact Namespace identity GET and additive config/scope/freshness caps. This supersedes pending-design approval wording in this section only. Runtime implementation and K09-01–12 verification remain pending. T09.1e, existing secrets, real clusters, Helm and OBS runtime remain excluded; historical checkpoints are preserved without replay.

## Accepted Nomad consumer supplement — 2026-10-24

[ADR-0017](../../../decisions/0017-nomad-workload-observation.md) accepts extending the narrow post-C managed-header exception to a registered Nomad consumer. NukeGeng accepted this narrow T09.2a–d supplement on 2026-10-24; the independent T08/Kubernetes rules remain in effect. New personal lab tokens are privately stored outside Git, without activating a SEC/Keychain binding. Company credentials, existing Keychain items and Product runtime integration remain excluded; see the [lab runbook](../../../operations/local-nomad-lab.md).

## T09 registered Nomad consumer and own-lab verification — 2026-10-24

Host composes the existing SEC broker/native child with the accepted post-C Nomad consumer; raw bytes remain scoped to the borrowed callback, while transient X-Nomad-Token strings are request-local. Startup validates helper hash/locator metadata but imports no item and performs no provider read. T09-NOMAD-LOCAL-002 explicitly copied only the new personal lab read-only token to a new UUID store with the designated apphost ACL, verified real broker/U/C/D/Nomad output, then deleted the store and synthetic binding rows. No existing Keychain items, company credentials or permanent binding were activated. See the [Nomad runbook](../../../operations/nomad-workloads-v1.md); general secure-erasure/production claims remain excluded.

## T09.1 Kubernetes composition compatibility — 2026-10-24

Accepted ADR-0016 now has an independent configured INV consumer/registry/route. The shared Host dependency reader validates either registered provider’s exact installation/tenant/target/scope/digest while retaining the original SEC snapshot digest and existing U/C/D. Installed configuration may enable Kubernetes, Nomad or both; metadata/default behavior, owner permissions, helper fencing, accepted ADRs and historical checkpoints are preserved. Synthetic current-owner conformance and final source-bound gates are recorded separately; no live Kubernetes/company permission or merge authority is inferred.

## T10 typed registry credential proposal — 2026-10-25

Proposed [ADR-0018](../../../decisions/0018-project-artifact-registration.md) / [Artifact registration v1](../../../contracts/artifact-registration-v1.md) adds a distinct project/registry/repository selection for artifact.registry.read.v1/artifact.register, additive SEC registry metadata/current heads, and global native reference allocation shared with inventory. Existing target tuples/digest bytes and helper protocol remain unchanged. Existing CredentialBroker/helper slots are shared, not duplicated; all reference writers must maintain typed allocation uniqueness. Direct Basic/static Bearer has a narrowly specified post-C managed-header exception requiring exact acceptance, not inherited T09 authority. No registry binding, existing Keychain read or migration is performed. ART10-03/06/07/08/13/14 and old T08/T09 regressions are required after implementation; native/live use is separately scoped.

### Exact T10.1a–f acceptance — 2026-10-25

NukeGeng approved exact ADR-0018 and Artifact registration v1, including this owner’s bounded design dependencies and NukeGeng’s ART/registry slice custody. This supersedes pending-approval language in the T10 proposal above only; wider feature/module status, existing accepted contracts and historical evidence remain unchanged. Implementation, ART10 tests, native/live scope, real provisioning and PR/merge evidence are still separate. No repeat design approval or extra Sol review is needed for covered T10.1a–f work.

## T10.1c implementation scope — 2026-10-25

Accepted ADR-0018 / Artifact registration v1; owner MOD-SEC, custodian NukeGeng, actor trusted project registration caller and separately authorized stopped-API operator. Exact selection is installation/tenant/project/registry/repository for artifact.registry.read.v1/artifact.register. Implement typed metadata/current heads/offline receipt and one global typed reference-allocation relation, metadata-only legacy backfill and both writers’ transactional allocation. Reuse one CredentialBroker/helper protocol/two slots; keep target v1 fields/digest bytes unchanged and require current C before consumer use. Scoped source/test list is private t10c-scope.json; tests use synthetic kordra_test rows and fake/native-isolated helpers only within existing authorizations. No actual registry, existing Keychain, runtime credential HTTP endpoint or company/production change.

## T10.1c local implementation — 2026-10-26

Added typed registry selection/snapshot/manifest, SEC-owned profiles/references/heads/history/receipts and a global installation/reference allocation shared by both writers. Additive migration backfills inventory metadata before exact typed-owner FKs; runtime metadata remains SELECT-only and command receipts inaccessible. Downgrade refuses allocation/history deletion. Old inventory tuples/digest encodings and native request schema stay unchanged.

Existing CredentialBroker now exposes a typed registry callback while retaining one shared two-slot helper pool, confirmed-exit-before-C ordering, cleared owned buffers and unknown-helper fencing. Offline registry-credential inspect/apply uses the stopped-API lease, trusted active tenant/project identity, exact manifest hash, monotonic SEC revisions and atomic SEC/IAM floor/AUD commit. Project revision/expiry changes during staging roll back; pre-commit setup failure leaves admission usable, while uncertain attempted commit closes it for exact receipt reconciliation. No online credential administration or provider access is added.

Targeted evidence: 15 registry metadata/broker tests and 16 offline-command/migration cases passed locally. Migration tests generate the real old/new scripts in a fresh UUID schema inside kordra_test, retain nonempty legacy row bytes, inject transactional failure, verify explicit rerun after rollback and confirm schema removal. These do not prove full artifact HTTP U/C/M/D, managed Authorization transport or ART10 conformance; those remain T10.1d–f. Full current-tree gates and publication are recorded separately. No real installation migration, existing Keychain or registry access; no debt added/closed.

T10.1e composition observes Reservation.HasUnresolvedHelper to retain the shared invocation slot when native helper quiescence is unknown. This is read-only technical lifecycle evidence, not permission or an extra lookup. Existing helper protocol, two-slot pool and inventory digest behavior remain unchanged.

## T11.0 shared solution/architecture acknowledgment — 2026-10-27

The accepted ADR-0005 T11.0 adds a BCL-only ApprovalGovernance Domain project and its test project to Kordra.slnx and the actual architecture graph. Existing feature behavior, permissions, schema and API are unchanged by this shared graph update. APR policy persistence, commands and complete feature validation remain in progress.
