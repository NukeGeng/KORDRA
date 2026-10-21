---
id: FEAT-SEC-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
module_id: MOD-SEC
feature_id: FEAT-SEC-001
phase: MVP
source_paths: []
test_paths: []
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
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.
