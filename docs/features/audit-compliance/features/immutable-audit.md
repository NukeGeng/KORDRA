---
id: FEAT-AUD-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-21
module_id: MOD-AUD
feature_id: FEAT-AUD-001
phase: MVP
source_paths: []
test_paths: []
planned_source_paths: ["Kordra.slnx", "src/Modules/AuditCompliance/Contracts/Kordra.Modules.AuditCompliance.Contracts.csproj", "src/Modules/AuditCompliance/Contracts/packages.lock.json", "src/Modules/AuditCompliance/Contracts/AuditIntent.cs", "src/Modules/AuditCompliance/Contracts/AuditAppendOutcome.cs", "src/Modules/AuditCompliance/Contracts/AuditReceipt.cs", "src/Modules/AuditCompliance/Contracts/IAuditAppendPort.cs", "src/Modules/AuditCompliance/Kordra.Modules.AuditCompliance.csproj", "src/Modules/AuditCompliance/packages.lock.json", "src/Modules/AuditCompliance/Infrastructure/AuditDbContext.cs", "src/Modules/AuditCompliance/Infrastructure/AuditEntry.cs", "src/Modules/AuditCompliance/Infrastructure/AuditIntentCanonicalizer.cs", "src/Modules/AuditCompliance/Infrastructure/AuditAppendStore.cs", "src/Modules/AuditCompliance/Infrastructure/AuditDbContextFactory.cs", "src/Modules/AuditCompliance/Infrastructure/AuditTransactionContext.cs", "src/Modules/AuditCompliance/Infrastructure/Migrations/InitialAuditPersistence.cs", "src/Modules/AuditCompliance/Infrastructure/Migrations/KordraAuditDbContextModelSnapshot.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/AuditComplianceMigrationCommand.cs"]
planned_test_paths: ["tests/AuditCompliance.Tests/AuditCompliance.Tests.csproj", "tests/AuditCompliance.Tests/packages.lock.json", "tests/AuditCompliance.Tests/TestDatabaseConfiguration.cs", "tests/AuditCompliance.Tests/AuditCompliancePersistenceTests.cs", "tests/AuditCompliance.Tests/AuditTransactionEnlistmentTests.cs", "tests/AuditCompliance.Tests/AuditIntentCanonicalizerTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
planned_doc_paths: ["docs/features/audit-compliance/features/immutable-audit.md", "docs/features/audit-compliance/module.md", "docs/features/artifact-management/features/immutable-artifact.md", "docs/features/infrastructure-inventory/features/register-target.md", "docs/features/identity-access/features/local-account-session.md", "docs/engineering/traceability.md", "docs/engineering/change-history.md", "docs/product/roadmap.md", "src/Modules/AuditCompliance/README.md", "src/Host/Kordra.DbMigrator/README.md"]
---

# FEAT-AUD-001 — Tamper-evident privileged action audit trail

## Foundation implementation slice — approved for implementation 2026-10-20

Luna T02 precedes login success and protected reads. Accepted authority/admission v1 owns the exact intent fields, stage outcomes, append semantics, idempotency lookup, bounds and failure behavior; accepted ADR-0012 supplies persistence/role decisions. Internal trusted producers append through AUD owner ports; append never synchronously calls IAM and creates no cyclic runtime dependency. HTTP audit queries require audit.read organization and their own list disclosure proof before enablement. The T02 implementation evidence below covers only the approved append/lookup and persistence foundation.

Persist immutable intent/outcome records with tenant/scope/correlation and canonical nonsecret hash. Same intentId/hash returns existing receipt; changed hash rejects. Enlist owner state and audit in one transaction for privileged mutations. A U/D intent is not proof of action/response completion. Unknown ACK must be resolvable by exact lookup, without protected-work replay. Denial audit remains bounded best-effort; it never turns denial into access. Reviewed migration revokes app UPDATE/DELETE/TRUNCATE on AUD tables, preserving INSERT/SELECT; owner/admin tamper resistance is outside this guarantee.

Acceptance AUD-F1: real isolated PostgreSQL confirms durability, replay/conflict and transaction rollback. AUD-F2: fail before/after commit and lose ACK; receipt lookup distinguishes known from unknown and never invents action success. AUD-F3: app modification/purge and cross-tenant audit query fail; secret markers absent in rows/errors/logs. AUD-F4: audit timeout/saturation blocks required U/D, denial remains bounded and no recursive queue forms. Minimum retention 90 days, no automatic purge.



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

At the original I2.7 proposal, formal ownership and concrete audit behavior were unresolved. Accepted ADR-0012 assigns NukeGeng accountability for this Foundation slice and MOD-AUD ownership of append/query storage. T02 must supply local persistence evidence for durability, idempotency and acknowledgement lookup; no outbox or broader messaging architecture is adopted.

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

T02 is approved for implementation only. Planned source/test/output paths above define ownership; no new AUD implementation or passing test is claimed by this design commit.
