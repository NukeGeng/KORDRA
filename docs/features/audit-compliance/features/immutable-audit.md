---
id: FEAT-AUD-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-21
module_id: MOD-AUD
feature_id: FEAT-AUD-001
phase: MVP
source_paths: [".github/workflows/product-ci.yml", "Kordra.slnx", "src/Host/Kordra.DbMigrator/", "src/Modules/AuditCompliance/"]
test_paths: ["tests/AuditCompliance.Tests/", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
planned_source_paths: ["Kordra.slnx", "src/Modules/AuditCompliance/Contracts/Kordra.Modules.AuditCompliance.Contracts.csproj", "src/Modules/AuditCompliance/Contracts/packages.lock.json", "src/Modules/AuditCompliance/Contracts/AuditIntent.cs", "src/Modules/AuditCompliance/Contracts/AuditAppendOutcome.cs", "src/Modules/AuditCompliance/Contracts/AuditReceipt.cs", "src/Modules/AuditCompliance/Contracts/IAuditAppendPort.cs", "src/Modules/AuditCompliance/Kordra.Modules.AuditCompliance.csproj", "src/Modules/AuditCompliance/packages.lock.json", "src/Modules/AuditCompliance/Infrastructure/AuditDbContext.cs", "src/Modules/AuditCompliance/Infrastructure/AuditEntry.cs", "src/Modules/AuditCompliance/Infrastructure/AuditIntentCanonicalizer.cs", "src/Modules/AuditCompliance/Infrastructure/AuditAppendStore.cs", "src/Modules/AuditCompliance/Infrastructure/AuditDbContextFactory.cs", "src/Modules/AuditCompliance/Infrastructure/AuditTransactionContext.cs", "src/Modules/AuditCompliance/Infrastructure/Migrations/InitialAuditPersistence.cs", "src/Modules/AuditCompliance/Infrastructure/Migrations/KordraAuditDbContextModelSnapshot.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/AuditComplianceMigrationCommand.cs"]
planned_test_paths: ["tests/AuditCompliance.Tests/AuditCompliance.Tests.csproj", "tests/AuditCompliance.Tests/packages.lock.json", "tests/AuditCompliance.Tests/TestDatabaseConfiguration.cs", "tests/AuditCompliance.Tests/AuditCompliancePersistenceTests.cs", "tests/AuditCompliance.Tests/AuditTransactionEnlistmentTests.cs", "tests/AuditCompliance.Tests/AuditIntentCanonicalizerTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
planned_doc_paths: ["docs/features/audit-compliance/features/immutable-audit.md", "docs/features/audit-compliance/module.md", "docs/features/artifact-management/features/immutable-artifact.md", "docs/features/infrastructure-inventory/features/register-target.md", "docs/features/identity-access/features/local-account-session.md", "docs/engineering/traceability.md", "docs/engineering/change-history.md", "docs/product/roadmap.md", "src/Modules/AuditCompliance/README.md", "src/Host/Kordra.DbMigrator/README.md"]
---

# FEAT-AUD-001 — Tamper-evident privileged action audit trail

## Foundation implementation slice — approved for implementation 2026-10-20

Luna T02 precedes login success and protected reads. Accepted authority/admission v1 owns the exact intent fields, stage outcomes, append semantics, idempotency lookup, bounds and failure behavior; accepted ADR-0012 supplies persistence/role decisions. Internal trusted producers append through AUD owner ports; append never synchronously calls IAM and creates no cyclic runtime dependency. HTTP audit queries require audit.read organization and their own list disclosure proof before enablement. The T02 implementation evidence below covers only the approved append/lookup and persistence foundation.

Persist immutable intent/outcome records with tenant/scope/correlation and canonical nonsecret hash. Same intentId/hash returns existing receipt; changed hash rejects. Enlist owner state and audit in one transaction for privileged mutations. A U/D intent is not proof of action/response completion. Unknown ACK must be resolvable by exact lookup, without protected-work replay. Denial audit remains bounded best-effort; it never turns denial into access. Reviewed migration revokes app UPDATE/DELETE/TRUNCATE on AUD tables, preserving INSERT/SELECT; owner/admin tamper resistance is outside this guarantee.

Acceptance AUD-F1: real isolated PostgreSQL confirms durability, replay/conflict and transaction rollback. AUD-F2: fail before/after commit and lose ACK; receipt lookup distinguishes known from unknown and never invents action success. AUD-F3: app modification/purge and cross-tenant audit query fail; secret markers absent in rows/errors/logs. AUD-F4: audit timeout/saturation blocks required U/D, denial remains bounded and no recursive queue forms. Minimum retention 90 days, no automatic purge.

## T02 implementation evidence — 2026-10-21

The approved T02 slice is implemented locally in the mapped AUD Contracts/Infrastructure, offline migrator and test paths. Intents are allowlisted and canonicalized; the hash includes a separate `Completed`/`Denied`/`Cancelled`/`Unknown` stage outcome, while D stores only result digest and size. Same-intent replay returns the existing receipt, changed-content reuse conflicts, and exact lookup returns a receipt without claiming work completion. `AuditTransactionContext` stages AUD with owner state on one PostgreSQL transaction; only confirmed commit returns `durable`. The append deadline covers context creation, pool wait, connection open, transaction, stage and commit: required appends have a one-second budget, while denial append is best-effort within 100 ms and has no retry queue. The migration retains records, has no purge path, and grants `kordra_app` only SELECT/INSERT on AUD rows.

Verified locally against isolated loopback `kordra_test` as `kordra_test`: 34 AuditCompliance tests passed, including PostgreSQL migration, concurrent duplicate/conflict append, owner/audit transaction rollback and commit, a deferred commit failure returning `Unknown` with exact lookup returning `NotFound`, a deliberately discarded successful caller response resolved by exact lookup, payload exclusion, table and connection-pool saturation bounds, and actual app-role ACL checks. Full deterministic AMR gates passed after the append-budget fix: strict docs (227 Markdown files, 0 errors/warnings), scope (39/39), tooling (175 tests), locked restore, Release build (0 warnings/errors), product tests (48 Artifact, 17 IAM, 34 AUD, 85 Inventory Bootstrap) and source mapping (46 changed source files, 0 problems). The production `kordra` schema was not migrated. The original native T02 task timed out and remains failed without an execution receipt; local gates do not rewrite that failed checkpoint. The separate evidence-recovery successor recorded below resolves the bookkeeping gap. No audit history HTTP route, login, tenant/project/target owner, authorization admission or provider access is included.


## T02 checkpoint reconciliation — 2026-10-21

The maintainer requested correction of T02's checkpoint failure after the AMR efficiency update. The original native checkpoint remains failed after the ten-minute runner timeout and missing terminal receipt; it is retained byte-for-byte. A separate `KORDRA-OP-P0-FOUNDATION-T02-RECOVERED-20261021` successor records `completion_kind: evidence_recovery` with zero new execution attempts, task/content-bound maintenance authorization, the actual historical Luna Max coordinator turn-context, current applicable deterministic gates, contributor triage and complete declared-output hashes in its private companion evidence. This is adoption of already-implemented/verified work, not a new architecture approval or fabricated native completion.

The private recovery record adopts inspected evidence under the maintainer's explicit request. The AMR tooling changes are reviewed separately. T02 validation includes explicitly authorized isolated PostgreSQL fixture effects, and does not classify those fixture effects as repository-only. Scope remains the accepted T02 foundation; no new product source, API, deployment, production migration or T03 execution is introduced. The full test evidence is 199 tooling tests and 184 product tests, including 34 AUD cases; matching fresh product evidence may be reused while docs/scope/mapping validate the current tree. Public feature status remains proposed for the wider audit capabilities and uncommitted implementation; the private successor records completion of T02 only.


## User story / outcome
**Actor:** System / Authorized Auditor. **Goal:** Tamper-evident privileged action audit trail. **Primary owner:** `MOD-AUD`. **Phase:** MVP.

## Pre-conditions
Authenticated source + authorized audit query scope.

## Main flow
Receive security/deploy fact → append with correlation + optional integrity chain → index read view → retain per policy.

## Alternatives / exceptional flow
Audit write unavailable on critical privileged action: fail closed or durable outbox per agreed risk policy.

## Messages and integration contracts
`approval.granted.v1; deployment.verified.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), an audit result can satisfy a required protected-use/disclosure stage only when AUD confirms a durable record bound to that exact intent, decision, stage and authorized scope. A health probe, in-memory enqueue or `Available` flag is not a durable receipt. Proposed append outcomes are `durable`, `rejected`, `unavailable` or `unknown`; only a verified `durable` result supplies a record reference. If policy requires durable audit, rejected, unavailable or unknown outcome blocks that stage. Public denial remains opaque when denial audit also fails. This section is the canonical owner of append-result and durable-receipt semantics; IAM and enforcement consumers link here rather than redefine them.

At the original I2.7 proposal, formal ownership and concrete audit behavior were unresolved. Accepted ADR-0012 assigns NukeGeng accountability for this Foundation slice and MOD-AUD ownership of append/query storage. T02 now supplies local persistence evidence for durability, idempotency and acknowledgement lookup; no outbox or broader messaging architecture is adopted.

## Negative & recovery scenarios
Tenant A exports B records; erase production approval evidence; include secret in audit.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Search deployment timeline by correlation and show principal/scope/outcome reliably.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Remaining proposed work outside T02
- Define and authorize the future audit query contract; T02 exposes no history/list endpoint.
- Add owner integrations and prove tenant-scoped audit disclosure before exposing history.
- Add operational retention/export controls only under separately approved scope; T02 adds no purge job.

## Source & test traceability
- Source files: mapped to `Kordra.slnx`, `src/Modules/AuditCompliance/` and `src/Host/Kordra.DbMigrator/`.
- Test files: mapped to `tests/AuditCompliance.Tests/` and the executable project-graph checks in `tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs`.
- Evidence: local PostgreSQL and unit evidence plus full deterministic gates are recorded above; the original native GOAL task still lacks a terminal receipt and is retained as failed; the separately authorized T02 evidence-recovery successor above records inspected completion without replay. The private companion manifest includes all 38 declared output hashes.
- Open decisions: broader query/export and retention operations remain outside T02.


## T02 review commit boundary

The AUD commit extends the shared product CI fixture with its isolated database locator, and maps the AUD project/Contracts, explicit migrator command, 34 AUD tests and updated architecture graph. The fixture and all role provisioning are disposable CI resources, never production setup. Hosted CI results are pending push; prior whole-worktree tooling counts describe local historical evidence, while commit-tree validation is recorded separately. IAM, Artifact and Inventory retain ownership of their shared solution/migrator/architecture files.

## PR #9 correction — cleanup stays within the append wait budget

PR9-AUD-CLEANUP-001 carries the original deadline/caller token through rollback, transaction detachment, conflict lookup and context disposal. `AppendAsync` bounds its entire wait, including provider cleanup: 100 ms for denial and one second for required append. If cancellation/cleanup stalls, the existing operation retains sole ownership of its resources until cleanup finishes; no retry, cleanup queue, early connection-pool return or later successful response is introduced. After expiry no new append phase starts. A possible commit returns Unknown and requires exact intent lookup, including a confirmed server commit whose cleanup delayed the acknowledgement. Direct owner-transaction callers must still dispose their own transaction/connection; their cleanup receives the linked deadline rather than an uncancellable token. Regression coverage includes blocked rollback cancellation and deliberately stalled context disposal for both deadline classes, with eventual resource release and exact lookup. Authentication/admission v1 and numeric policy limits remain unchanged.
