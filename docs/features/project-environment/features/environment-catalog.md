---
id: FEAT-PRJ-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-20
module_id: MOD-PRJ
feature_id: FEAT-PRJ-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-PRJ-001 — Create and govern deployment environments

## Foundation implementation slice — approved for implementation 2026-10-20

Luna T05 PRJ task follows TEN/audit under accepted ADR-0012 and authority/admission v1. Actor authenticated User; project.manage for catalog mutation, project.read for adopted scoped reads. Persist exact Project/Application/Environment ownership, Active/Suspended/Deleted lifecycle and revisions, explicit Development/Test/Staging/Production classification and policy association. ConnectionProfile stays SEC-owned. This approved slice is not implementation evidence.

The contract owns command DTOs and transactional two-owner target binding. Project-only target requires resolved environment absence; unknown lookup never supplies it. First slice rejects tenant/project moves rather than relabeling IDs. Creation grants no implicit target/deployment rights. INV association updates enlist PRJ/INV owner handlers and AUD in one transaction under the writer gate, never cross-owner table access.

Acceptance PRJ-F1: create catalog objects once per idempotency key with audit and stable owner references. PRJ-F2: foreign tenant/project/target, stale revision, invalid policy/classification and name-based downgrade reject. PRJ-F3: crash between association updates rolls back both; no reader sees a half-binding. PRJ-F4: suspended/deleted/unknown/conflicting owner facts deny U/D, including change-and-change-back. Planned roots: existing PRJ reservation and tests/ProjectEnvironment.Tests/; no speculative frontmatter mappings.


## User story / outcome
**Actor:** Tenant Admin / Project Owner. **Goal:** Create and govern deployment environments. **Primary owner:** `MOD-PRJ`. **Phase:** MVP.

## Pre-conditions
Tenant active và actor có quyền quản lý project.

## Main flow
Create project/application → create classified environment → bind approved target → assign policy → activate.

## Alternatives / exceptional flow
Rename environment không đổi security classification; policy change requires audit/review.

## Messages and integration contracts
`environment.classification.changed.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), MOD-PRJ remains the source of project-to-tenant and environment-to-project/tenant ownership, classification, target binding and applicable policy association. Future owner evidence must bind the exact lookup and current owner revision. A project-only target requires an authoritative, resolved absence of an environment; failed or unavailable environment lookup is not absence. Names, labels and read-model projections cannot establish ownership. Missing, stale, conflicting or unavailable ownership/policy evidence denies access.

At the original I2.7 proposal, formal ownership and concrete policy assembly were unresolved. Accepted ADR-0012 assigns NukeGeng accountability for the approved Foundation slice and MOD-PRJ ownership of its project/environment facts; the broader module remains proposed. The prior I2.7 approval alone adopted no source mapping or runtime behavior.

## Negative & recovery scenarios
Attach target của org B; downgrade production classification without approval; duplicate resource id.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Environment always has explicit classification and policy; no name-based production checks.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## I0 — OP-P0 connection-profile/environment seam proposal

**Proposed only; source not implemented, no operational approval.** This bounded increment prepares the profile/environment part of [OP-P0](../../../product/operator-pilot-plan.md) and [OP-01](../../../product/operator-workstation.md), following the updated [FEAT-INV-001/I0 inventory proposal](../../infrastructure-inventory/features/register-target.md). The original MVP phase and broader environment-governance flow above remain; I0 proposes read-only selection and display, not target registration, classification/policy changes, credential binding or deployment. It does not broaden [OSS Beta acceptance](../../../product/oss-beta-acceptance.md).

All metadata names, labels and associations below are illustrative proposals pending owner review, not adopted storage schemas, public contracts, permission rules or runtime state transitions. This task is `F0-OPP0-PRJ-SPEC`; contributor scope is local documentation under `repository.write`, not a product or infrastructure grant.

### Proposed profile metadata and associations

The existing [MOD-SEC module](../../connection-secrets/module.md) owns the `ConnectionProfile` aggregate and authoritative connection metadata. MOD-PRJ contributes its project/application/environment classification and authorized target associations to an assembled read view; it does not create or persist a second profile aggregate. Unresolved storage technology or local/server placement does not reopen that domain ownership.

- An illustrative assembled profile/environment view could expose an authorized `ConnectionProfile` reference/revision and descriptive labels supplied by MOD-SEC, together with distinct trusted organization, project, environment and selected target references supplied by their owners. Optional source-system, Git/CI, registry and provider-context aliases remain descriptive metadata only; they cannot select broader access or establish target identity. Real addresses, company identifiers and private profiles stay outside public repository evidence.
- Project/application/environment ownership remains in MOD-PRJ. Resolve organization/project/environment associations from trusted ownership information, with an explicit environment classification and associated policy reference. Classification is independent of profile/environment names: renaming a production profile to “dev” leaves its production classification, policy and visible production indicator intact. I0 cannot downgrade classification or change policy.
- A selected target must have an authorized association with that environment and the same trusted tenant scope. The profile references the inventory-owned target and its trusted identity expectation/resource scope; it does not create, transfer or validate target ownership itself. A matching alias or endpoint label cannot replace independently checked provider identity. Unknown identity remains unknown, not a verified target.
- Carry only an opaque credential-binding/reference identifier for the selected scope, subject to disclosure authorization. [Connections & Secrets / FEAT-SEC-001](../../connection-secrets/features/secret-binding.md) retains secret resolution, provider selection, expiry, rotation and binding validity. No token, password, kubeconfig or secret value belongs in profile metadata, logs, events or UI. A reference is neither a credential nor permission; local credential availability grants no server-worker access.
- Profile revision and selection context identify which association a read/display concerns. Profile metadata freshness is separate from inventory observation freshness; saving, renaming or reconnecting cannot refresh target evidence. Revision, expiry and freshness mechanisms remain owner decisions before implementation.

### Proposed active-target visibility and selection/reconnect behavior

1. Show the selected profile plus organization/project/environment, explicit classification (including a production indicator), target reference/identity summary and bounded resource scope throughout target-specific views. Display metadata as reported/unverified until trusted association and identity checks succeed; disclose identifiers only within current permission. Keep client connection availability separate from observed workload health and show evidence time/coverage supplied by its owner.
2. Before a bounded read, resolve the authenticated principal, trusted tenant ownership, environment-target association and current scoped permission through the owning modules. Profile labels, role labels, aliases and caller `tenantId` never authorize. Deny by default; platform scope grants no implicit tenant access. Missing/expired/revoked binding or unknown/mismatched target identity prevents dispatch; do not fall back to another profile, target, namespace, credential or TLS bypass.
3. On profile/target selection change, invalidate prior in-flight results for the active view and revalidate the new association. Correlate reads/results with tenant, environment, target and selection revision as proposed in FEAT-INV-001/I0; late results from the previous selection cannot populate the new view. Cached evidence is accessible only under current authorization and remains tied to its original target, capture time and coverage.
4. A disconnected Mac/client means current observation is unavailable; it does not prove an unhealthy workload. A fresh authorized, identity-bound observation may independently show an unhealthy workload even when its read path is reachable. Keep these dimensions and their reasons distinct; missing, stale or untrusted evidence stays unknown. MOD-PRJ displays owner-supplied evidence, not a locally inferred cluster-health decision.
5. After reconnect, Mac wake or client restart, a remembered profile is only a selection hint. Revalidate identity, associations, permissions and credential binding, then obtain a new bounded read before displaying current observed status. Do not silently retarget, upgrade old timestamps, replay mutations or claim background execution survives Mac closure. Durable execution/host decisions remain outside I0.

### Proposed module seams — existing ownership retained

| Owner | Profile/environment seam for later contract review |
|---|---|
| MOD-PRJ / this feature | Project/application/environment associations and explicit classification contributed to an assembled selection view. No ConnectionProfile aggregate ownership/persistence and no direct reads of another module's internal storage. |
| [MOD-INV / FEAT-INV-001](../../infrastructure-inventory/features/register-target.md) | Owns target registration, target identity/capability and inventory evidence. Consume reviewed references/results; profile selection cannot fabricate observed cluster state or registration approval. |
| [MOD-SEC / FEAT-SEC-001](../../connection-secrets/features/secret-binding.md) | Retains ConnectionProfile, authoritative connection metadata, SecretReference, CredentialBinding and RotationStatus ownership. Consume reviewed authorized profile/reference/validity results only; provider, persistence technology, local/server placement and local-versus-worker trust are not selected here. |
| [MOD-IAM / FEAT-IAM-001](../../identity-access/features/authorize-resource.md) | Owns authorization decisions for trusted principal and resource scope. Existing `project.read`, `project.manage` or `target.read` catalog candidates are not adopted as a complete profile action mapping. |
| [MOD-OBS / FEAT-OBS-001](../../observability/features/health-verification.md) | Owns health verification semantics with scoped evidence/freshness; transport loss cannot become a workload-health finding in the profile view. |

No new dependency, host, API/event contract or ownership transfer is adopted. Existing message candidates above remain candidates; provider secrets, authorization decisions, observed cluster state and inventory stay with their existing owners.

### Planned acceptance / negative matrix — not executed

Future unit/contract/integration cases use synthetic profiles, fake identities/authorization, fake clocks and an instrumented read adapter. These are planned assertions, not test files, executed product tests or passing results; no live target or credential is needed.

| Case / synthetic setup | Planned assertions |
|---|---|
| Valid read-only selection | Trusted same-tenant organization/project/environment/target association, explicit classification, current permission and valid binding expose the permitted active-target summary; a fresh identity-matched read is tied to selection revision and original capture time. No mutation call or secret value is emitted. |
| Wrong tenant or target association | Forged caller `tenantId`, another tenant's target/credential ref, platform-only principal, or same-tenant target bound to a different environment is denied before provider dispatch. No protected cached evidence or identifier leaks; matching labels/aliases cannot override association checks. |
| Production profile renamed | Rename label/alias to “dev”: stable environment classification, production indicator, policy reference and access requirements remain unchanged; no policy downgrade or extra authority. |
| Missing permission or missing/expired binding | Deny the read and protected cache access when scoped permission is absent/revoked; missing/expired/revoked credential binding prevents dispatch and yields a sanitized unavailable/denied reason. A saved selection, role label or local credential cannot bypass either check. Revalidate after expiry during reconnect. |
| Disconnected client versus unhealthy workload | Client transport loss with no fresh workload evidence shows disconnected/unknown observation, not unhealthy workload; an independently fresh unhealthy observation remains a separate evidence-backed signal even with a reachable read path. Preserve authorized last-known evidence timestamps. |
| Stale profile or association changed | An obsolete profile revision or removed/rebound environment-target association cannot dispatch using cached validity. Require refreshed trusted association; do not silently choose a replacement target or refresh observation time. |
| Unknown or mismatched target evidence | Missing provider identity, stale/missing timestamps, partial evidence or an identity mismatch cannot produce a verified target/current healthy claim. Keep sanitized reason and evidence provenance; no alias matching or broader-context fallback. |
| Selection switch / delayed duplicate result | Switch context during a delayed read, then receive duplicate/out-of-order old results: old evidence cannot overwrite the active target view or merge tenant scopes. Repeated selection does not authorize mutation or create target/credential bindings. |
| Restart / reconnect / revoked access | Remembered selection after restart requires current scope, permission, binding and identity checks plus a fresh read; revoked access hides protected cached data. No mutation replay, automatic remote job claim or implied Mac-close survival. |
| Module ownership isolation | Build the view from reviewed owner references/results; no MOD-PRJ profile aggregate or direct connection-secret storage read is introduced. ConnectionProfile updates remain in MOD-SEC and cannot be implied by changing an environment view. |

### Unresolved decisions and evidence boundary

- `[NEEDS-DECISION]` Assign owners; select IdP/trusted tenant resolution and approve exact profile/read permissions, scope checks, revocation/recheck semantics and audit disclosure/retention with IAM/security owners. No role-name rule or new policy engine is chosen.
- `[NEEDS-DECISION]` Persistence technology and profile revision/freshness rules; local-only versus server/team-managed storage and synchronization/conflict handling within the existing MOD-SEC profile ownership, plus reviewed read-view/association contracts with MOD-PRJ. No database, Keychain/provider choice, domain ownership transfer or local-to-worker credential transport is adopted.
- `[NEEDS-DECISION]` First supported target and trustworthy provider-identity mechanism, bounded read capability set and observation freshness/ordering contracts with inventory/observability owners. Do not infer Kubernetes or Compose/SSH selection from examples.
- `[NEEDS-DECISION]` UI and host architecture/ADR: browser client versus native packaging, API/worker topology and durable state location. [ADR-0009](../../../decisions/0009-source-architecture.md) remains proposed; [ADR-0008](../../../decisions/0008-domain-bootstrap-validation.md) remains accepted only for the narrow artifact-domain/test bootstrap. Neither authorizes a new profile implementation, host or dependency here.
- Product source/tests for this seam are **not implemented**. Planned cases above have not run. Documentation checks, task-bound execution receipt, independent native review and contributor triage must be recorded separately by the coordinator for the exact current content; none constitutes product verification or company-resource approval. Roadmap/evidence reconciliation and the subsequent credential-seam task remain coordinator work after applicable gates/review/triage; no historical `validation_failed` task is replayed.

## Source & test traceability
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.
