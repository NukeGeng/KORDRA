---
id: FEAT-AUD-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-26
module_id: MOD-AUD
feature_id: FEAT-AUD-001
phase: MVP
source_paths: ["src/Modules/AuditCompliance/Contracts/AuditMutationEvidence.cs", "src/Modules/AuditCompliance/Infrastructure/AuditMutationEvidenceReader.cs", ".github/workflows/product-ci.yml", "Kordra.slnx", "src/Host/Kordra.DbMigrator/", "src/Modules/AuditCompliance/"]
test_paths: ["tests/IdentityAccess.Tests/ReleaseMutationTransactionTests.cs", "tests/AuditCompliance.Tests/", "tests/IdentityAccess.Tests/CurrentAuthorityHttpTests.cs", "tests/IdentityAccess.Tests/ProtectedReadHttpTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/ProjectEnvironment.Tests/ResourcePolicyIntegrationTests.cs", "tests/Messaging.Tests/AuditFixtureModelTests.cs", "tests/Messaging.Tests/TransactionAdapterCompatibilityTests.cs", "tests/Messaging.Tests/NativeConsumerCompatibilityTests.cs", "tests/Messaging.Tests/CompatibilityProgram.cs", "tests/Messaging.Tests/CommitReplyFault.cs", "tests/Messaging.Tests/NativeIngressDatabaseTests.cs", "tests/Messaging.Tests/NativeIngressArtifactTests.cs", "tests/Messaging.Tests/NativeTimerDatabaseTests.cs", "tests/Messaging.Tests/NativeRetryDatabaseTests.cs", "tests/Messaging.Tests/NativeRetryArtifactTests.cs", "tests/Messaging.Tests/MetadataFailureDatabaseTests.cs", "tests/Messaging.Tests/NativeExhaustionDatabaseTests.cs", "tests/Messaging.Tests/NativeExhaustionArtifactTests.cs", "tests/Messaging.Tests/LifecycleBoundaryDatabaseTests.cs"]
planned_source_paths: ["Kordra.slnx", "src/Modules/AuditCompliance/Contracts/Kordra.Modules.AuditCompliance.Contracts.csproj", "src/Modules/AuditCompliance/Contracts/packages.lock.json", "src/Modules/AuditCompliance/Contracts/AuditIntent.cs", "src/Modules/AuditCompliance/Contracts/AuditAppendOutcome.cs", "src/Modules/AuditCompliance/Contracts/AuditReceipt.cs", "src/Modules/AuditCompliance/Contracts/IAuditAppendPort.cs", "src/Modules/AuditCompliance/Kordra.Modules.AuditCompliance.csproj", "src/Modules/AuditCompliance/packages.lock.json", "src/Modules/AuditCompliance/Infrastructure/AuditDbContext.cs", "src/Modules/AuditCompliance/Infrastructure/AuditEntry.cs", "src/Modules/AuditCompliance/Infrastructure/AuditIntentCanonicalizer.cs", "src/Modules/AuditCompliance/Infrastructure/AuditAppendStore.cs", "src/Modules/AuditCompliance/Infrastructure/AuditDbContextFactory.cs", "src/Modules/AuditCompliance/Infrastructure/AuditTransactionContext.cs", "src/Modules/AuditCompliance/Infrastructure/Migrations/InitialAuditPersistence.cs", "src/Modules/AuditCompliance/Infrastructure/Migrations/KordraAuditDbContextModelSnapshot.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/AuditComplianceMigrationCommand.cs"]
planned_test_paths: ["tests/AuditCompliance.Tests/AuditCompliance.Tests.csproj", "tests/AuditCompliance.Tests/packages.lock.json", "tests/AuditCompliance.Tests/TestDatabaseConfiguration.cs", "tests/AuditCompliance.Tests/AuditCompliancePersistenceTests.cs", "tests/AuditCompliance.Tests/AuditTransactionEnlistmentTests.cs", "tests/AuditCompliance.Tests/AuditIntentCanonicalizerTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
planned_doc_paths: ["docs/features/audit-compliance/features/immutable-audit.md", "docs/features/audit-compliance/module.md", "docs/features/artifact-management/features/immutable-artifact.md", "docs/features/infrastructure-inventory/features/register-target.md", "docs/features/identity-access/features/local-account-session.md", "docs/engineering/traceability.md", "docs/engineering/change-history.md", "docs/product/roadmap.md", "src/Modules/AuditCompliance/README.md", "src/Host/Kordra.DbMigrator/README.md"]
---

# FEAT-AUD-001 — Tamper-evident privileged action audit trail

## T06 accepted resource-policy audit consumer design — 2026-10-22

[Resource Policy v1](../../../contracts/resource-policy-v1.md) / [ADR-0014](../../../decisions/0014-resource-policy-v1.md) specifies the maintainer-accepted exact policy/binding facts in the existing nonsecret canonical intent binding, one enlisted IAM/PRJ/AUD transaction for offline publication/rebinding, and distinct existing U/D stages. No AUD schema/history reinterpretation, policy payload disclosure or new audit-query authority is approved here. Unknown commits retain admission v1 fencing/reconciliation; setup-only failure remains distinct. Exact T06 design approval was recorded on 2026-10-22; transaction/output test evidence remains pending.

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

## PR #9 hosted-CI correction — receipt timestamp precision

The first conflict-free hosted run exposed a receipt mismatch on Linux: .NET recorded-at values can contain 100 ns ticks, while PostgreSQL persists microseconds. AUD now normalizes its server-generated recorded-at timestamp to UTC microseconds before persistence and receipt creation, preserving exact receipt equality on lookup/replay. A BCL TimeProvider seam injects sub-microsecond timestamps in two deterministic PostgreSQL regressions; existing receipt-equality assertions stay strict. No schema, hash fields, authentication/admission policy, dependency or migration changes are required. The failed run remains recorded as run 39563238897 on f50ae68; replacement CI must validate the corrected head.

## T03 local-authentication consumer boundary — 2026-10-21

The accepted IAM T03 slice composes AUD through the host adapter in `FEAT-IAM-002`; IAM does not read or write AUD tables. Successful login and account/session mutations stage required AUD records in the same PostgreSQL transaction as IAM state, and uncertain audit/commit outcomes fail closed. AUD’s HTTP history/query surface remains disabled. The health-only API profile does not register AUD persistence; AUD is available only to the explicit local-pilot composition. The exact cross-module project/assembly graph remains covered by `tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs`.

## T04 shared-owner integration — 2026-10-22

Shared solution, DbMigrator references/dispatch and API transitive lock files include TEN persistence. TenantAuditTransactionAdapter lives only in Host: setup never commits; staged AUD plus owner mutation/receipt commit atomically, failed pre-commit validation rolls back and unknown commit remains fail-closed. TEN runtime consumes AUD Contracts only. Existing AUD append/schema semantics and immutable records are unchanged. TenantMembershipTests covers rollback, one audit after exact replay and lost-ACK recovery; ArchitectureTests retains graph and compiled forbidden-edge checks. No runtime migration or production operation was executed.

## T05 shared-owner integration — 2026-10-22

T05 Host CatalogMutationCoordinator enlists owner PRJ/INV contexts and AUD in one transaction. AUD storage/append contracts are unchanged. TargetAssociationTests checks one audit on replay, rollback after first owner save, and lost-ACK reconciliation. Shared solution/DbMigrator/API lock graphs add only the approved owner persistence dependencies.

## T06 implementation evidence — 2026-10-23

T06 preserves schemaVersion 1 historical intents and current append-only AUD grants. Offline policy composition uses an explicit IOfflineAuditLease and exact admin/test transaction role guard; the existing runtime transaction path still rejects admin. Custody is checked at staging/commit. Owner state, policy head, selected binding/history, receipt and IAM clock floor enlist with AUD in one commit. U/D use distinct result-bound receipts and nonsecret canonical binding digests, with actual HTTP ordering/partial failure proof. New fault tests distinguish setup-only rollback from lost ACK after real commit, fence unknown outcome and reconcile without repeating publication. No AUD query API, policy payload storage/disclosure, retention purge or real production operation is added.

PR14 P2 co-owner acknowledgment (2026-10-23): the offline policy coordinator rechecks bind validity after actual AUD staging and before invoking commit. The expiry-during-stage regression asserts zero commit calls and rollback of staged AUD/PRJ/IAM state; exact committed receipt recovery still uses the original AUD hash. No AUD schema, canonicalization, protocol or unknown-commit semantics change.


### T07 bounded-read consumer acknowledgment — 2026-10-23

AUD retains the actual durable U/D append contract; the T07 test decorator records real commitments and injects unavailable/unknown only in the test host. No audit payload/schema or owner permission changes. Shared Host composition and the actual-owner HTTP fixture are mapped here; the new adapter/probe paths belong to [FEAT-INV-001](../../infrastructure-inventory/features/register-target.md). T07 keeps the existing seven-field metadata v1 wire shape.


### Proposed T08 credential dependency — 2026-10-23

The owner-requested [Credential boundary v1](../../../contracts/credential-boundary-v1.md) and [ADR-0015](../../../decisions/0015-credential-boundary-preparation.md) define a pending SEC dependency/C-consumption supplement and its actual consumers. Existing accepted behavior/source mappings remain unchanged until exact approval and implementation. T07 metadata-only U/D is not treated as credential-use proof; existing AUD v1 stages and metadata wire shape stay stable in the proposal. See the contract for compatibility, owner boundaries and planned acceptance; no provider call or test completion is claimed here.


### T08 shared implementation consumer — 2026-10-24

Accepted ADR-0015 adds the SEC owner runtime/Contracts/test graph and scoped Host/offline migration composition. The existing source/test mappings above cover the shared solution, authority/HTTP/audit/architecture consumers changed in the same increment. [FEAT-SEC-001](../../connection-secrets/features/secret-binding.md#t08-local-implementation--2026-10-24) owns exact new paths and local evidence. IAM neutral dependencies/current C intent do not add a circular SEC runtime edge; AUD keeps v1 stages/schema, metadata wire v1 and existing role permissions remain unchanged. Synthetic fixture/native proof does not authorize provider network access or change this module's business ownership.

## T10 artifact transaction consumer proposal — 2026-10-25

[Artifact registration v1](../../../contracts/artifact-registration-v1.md) under proposed [ADR-0018](../../../decisions/0018-project-artifact-registration.md) uses existing AUD schema/stages: Pending+U and terminal ART result/claim+Mutation each commit atomically with the clock floor; C uses Use, D uses Disclosure and safe failure bookkeeping uses Outcome. No new AUD stage, business message or audit-as-success assumption. Unknown commit fencing applies only after an attempted commit, retaining the pre-commit cancellation regression. ART owns receipts and recovery; AUD remains neutral. No implementation/test evidence is claimed; ART10-03/10/11/12 must prove these new consumers after approval.

### Exact T10.1a–f acceptance — 2026-10-25

NukeGeng approved exact ADR-0018 and Artifact registration v1, including this owner’s bounded design dependencies and NukeGeng’s ART/registry slice custody. This supersedes pending-approval language in the T10 proposal above only; wider feature/module status, existing accepted contracts and historical evidence remain unchanged. Implementation, ART10 tests, native/live scope, real provisioning and PR/merge evidence are still separate. No repeat design approval or extra Sol review is needed for covered T10.1a–f work.

## T10.1e transaction observation scope — 2026-10-26

Under accepted ADR-0018, Host atomically enlists ART/IAM-floor/AUD and must distinguish pre-commit setup/save failure from an actual attempted commit with unknown acknowledgement. Expose the existing AuditTransactionContext internal phase as read-only CommitAttempted observation; no new stage/schema, business permission, commit or review authority. The observation is not evidence of success. Coordinator tests must keep admission usable for known pre-commit failures and fence only actual unknown commit outcomes.

## T10 stopped artifact recovery — 2026-10-27

The artifact recovery increment uses a stopped-custody AUD receipt reader on a guarded owner connection and appends its outcome in the same transaction as the ART state and IAM clock floor. The reader grants no HTTP audit-query authority. Plan/hash, quiescence and ambiguous-outcome behavior are specified in [artifact registration v1](../../../contracts/artifact-registration-v1.md).

## T11 proposed consumer seam — 2026-10-27

AUD consumer acknowledgment for [Release and approval v1](../../../contracts/release-approval-v1.md) / [ADR-0005](../../../decisions/0005-immutable-release.md), awaiting exact human acceptance. Compose release/policy/approval writes and command receipts with existing enlisted AUD and IAM floor on one connection. Use safe subject/result hashes, owner IDs and fixed codes, never canonical subject bytes, raw key/body or session. Existing audit intent bounds and true commit uncertainty rules remain; no integration event, audit-bypassing recovery or second commit.

This is planned additive scope, not implemented source/test evidence or a change to the accepted T01–T10 baseline. Preserve current source/test mappings; Developer adds precise new paths and negative/compatibility tests only when the owning slice exists. R11-02/03/10/12/15 are required as applicable. No real database/provider/secret operation is authorized.

## T11.1 shared graph acknowledgment — 2026-10-27

Accepted ADR-0005 adds a BCL-only REL Domain and test project to the solution and actual architecture graph. This graph change does not alter this owner’s runtime behavior or grant release authority. T11.1 owner ports, persistence and HTTP remain in progress.

## T11.1 local receipt verification — 2026-10-27

Accepted ADR-0005 covers a neutral AUD owner reader that compares an exact mutation receipt hash and scope against immutable AUD facts. It does not grant permission or query access. REL uses it only as a candidate for a future fresh disclosure, and fences on missing or contradictory evidence. The transaction component and synthetic PostgreSQL tests are local and not yet wired to HTTP; complete T11 validation and operational recovery remain pending.


### Shared solution integration — T12.0 (2026-10-30)

The shared `Kordra.slnx` adds only the test-owned Messaging.Durability.Tests compatibility/fault spike accepted by [ADR-0019](../../../decisions/0019-durable-transport-workflow-bootstrap.md). This feature's production source, package pins, contracts, permission model and owner persistence remain unchanged. New messaging evidence belongs to FEAT-MSG-001 and does not establish runtime messaging adoption for this owner.


### Proposed T12.1 isolated AUD fixture seam — 2026-10-30

[ADR-0020](../../../decisions/0020-messaging-runtime-adoption.md) explicitly proposes guarded UUID-schema testability in AuditDbContext, AuditDbContextFactory and AuditTransactionContext for actual-AUD/shared MSG transaction proof. Production app schema, runtime target guards, immutable audit/commit authority remain unchanged. This is not implemented or accepted; owner NukeGeng must approve the test seam with T12.1. No dependency from MSG Infrastructure into AUD runtime is proposed.


### T12.1 scoped acceptance recorded — 2026-10-30

NukeGeng accepted [ADR-0020](../../../decisions/0020-messaging-runtime-adoption.md), [Durable Messaging v1](../../../contracts/durable-messaging-v1.md) and T12.1a–f on the exact reviewed proposal, including the guarded AUD UUID-schema test seam. Earlier proposed notes are historical. No Product implementation is established by this annotation; production target guards/authority remain unchanged. Design approval grants no real installation migration, new role/HBA, credential or provider access. T12.1a public-API/actual-AUD compatibility proof precedes dependent slices.


### T12.1a guarded test seam in progress — 2026-10-30

Implemented the ADR-0020-approved isolated-test context/transaction factory and schema-aware model cache identity. Only exact kordra_test/kordra_test loopback target without session overrides is accepted; schema derives from a nonempty case GUID. Existing runtime/offline CreateAsync paths, app schema defaults and target guards remain unchanged. No factory opens a connection or executes DDL. Model/negative tests in Messaging.Tests validate no-I/O schema separation and production/privileged target rejection. Real enlisted transaction proof and fixture operational permission remain separate; no T12.1 completion claimed.


T12.1a targeted result: 9 no-connection tests passed, no skips (stager guards + actual AUD model schema separation/production target rejection). Initial test graph selected EF 10.0.4 transitively and failed CS1705; the test project now explicitly pins the existing accepted EF 10.0.11/Npgsql EF 10.0.3, preserving Product pins. Actual DB/commit/ACK proof remains pending; these tests do not close compatibility gate a.


## T12.1a actual AUD/public API transaction probe — 2026-10-30

Owner separately approved the exact eight UUID schemas and private kordra_test locator. Four real PostgreSQL cases now pass: producer and persisted-inbox staging, each commit/rollback. Actual AUD context is the sole commit-capable path; owner/floor, AUD row, outgoing result and inbox Handled share the caller transaction. Independent snapshots see no uncommitted owner/audit/result/Handled state. Wrong schema/cross-connection binding and staging after transaction completion reject. No EF Internals or direct SQL writes to native Wolverine tables are used by the runtime prototype.

The first extended fixture incorrectly disabled the durability agent, preventing native node schema setup (42P01); that failed receipt is retained. Corrected staging probes keep normal startup and delay recovery polling beyond the bounded probe, explicitly not live handler/ACK evidence. Four tests pass on corrected content; all eight approved schemas are absent afterward. No app/existing-data, kordra, role/HBA, Keychain or provider operation occurred. Next acceptance remains native-context enlistment/consumer ACK and actual fault/unknown-outcome compatibility before T12.1a can close. Full quality gates/publication are pending feature completion.


## T12.1a native consumer and lost-COMMIT evidence — 2026-10-30

Added CallerOwnedEnvelopeEnlistment implementing the pinned public IEnvelopeTransaction and MessageContext enlistment APIs, retaining AUD as the sole root commit path. Actual two-process native consumer tests now cover normal handling, crash before commit, crash after commit before Handled-memory/flush/ACK, restart and new envelope ID/same logical key. Independent queries assert one logical effect/result and atomic AUD/inbox Handled/outbox outcomes. Runtime adapter uses no EF Internals or reflection; no host/business registration exists.

The first native run failed because MessageContext forwards an empty outstanding snapshot on enlistment; the native enlistment now treats that empty snapshot as no-op while nonempty batches retain stage limits. The failed run remains retained. Native consumer rerun passed three cases.

A test-only, version-guarded Npgsql 10.0.3 read-stream fault observes and removes the actual server CommandComplete(COMMIT) before Npgsql consumes it. AUD returns Unknown; a new connection finds the exact durable intent/hash, owner/floor and outgoing record, without replay. This test instrumentation uses reflection only in the test project on an exact unpooled loopback fixture connection; it does not bypass AUD target guards or enter Product runtime. It is not a mock exception after calling CommitAsync successfully. A second commit is rejected.

Current targeted suite: 17 passed, zero failed/skipped; actual AUD staging has five cases and native consumer has three, plus nine no-I/O guard/model cases. Post-test read-only verification found zero of the eight approved schemas and zero owned child processes. Source/test mappings and private progress receipts are updated. Solution/architecture/full gates and remaining kernel slices are still pending; targeted compatibility evidence alone is not T12.1 completion, production identity, distributed authority or provider-effect proof.


### T12.1e native ingress fixture preparation — 2026-10-31

FEAT-MSG-001 prepares a UUID-only actual-AUD/native MSG fixture for the dedicated incoming rule and native DLQ. It consumes the accepted isolated AUD model factory to emit offline review DDL; no AUD runtime/schema/authority semantics change. Source/test paths are mapped above. Database execution requires a separate exact operation approval and is not established by successful compilation or offline SQL generation. The fixture includes one valid vendor control and four synthetic rejected messages, then checks the own effect/receipt/DLQ data and captured synthetic logs only. Production guards, actual installation data, role/HBA/grants and external providers remain excluded.


### T12.1e actual native ingress result and timer preparation — 2026-10-31

The separately approved UUID ingress/DLQ case passed using the actual isolated AUD model: one valid message produced one effect/logical receipt/AUD, while four synthetic invalid deliveries were retained as safe native dead letters without a token sentinel in body/exception/captured logs. The dedicated new schema was dropped and independently confirmed absent. No AUD production table, role/HBA/grant or authority semantics changed. A new timer/restart fixture reuses the same offline model-DDL factory for a different exact UUID; it is compiled but awaits its own operation approval. Seed rollback/commit concerns native scheduling metadata, not a new independent business/AUD commit path.
