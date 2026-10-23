---
id: FEAT-TEN-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-22
module_id: MOD-TEN
feature_id: FEAT-TEN-001
phase: MVP
source_paths: ["Kordra.slnx", "src/Host/Kordra.Api/TenantAuditTransactionAdapter.cs", "src/Host/Kordra.DbMigrator/TenantManagementMigrationCommand.cs", "src/Modules/TenantManagement/Contracts/Kordra.Modules.TenantManagement.Contracts.csproj", "src/Modules/TenantManagement/Contracts/TenantAuthority.cs", "src/Modules/TenantManagement/Contracts/packages.lock.json", "src/Modules/TenantManagement/Infrastructure/Migrations/20261022041945_InitialTenantPersistence.Designer.cs", "src/Modules/TenantManagement/Infrastructure/Migrations/20261022041945_InitialTenantPersistence.cs", "src/Modules/TenantManagement/Infrastructure/Migrations/TenantDbContextModelSnapshot.cs", "src/Modules/TenantManagement/Infrastructure/TenantDbContext.cs", "src/Modules/TenantManagement/Infrastructure/TenantDbContextFactory.cs", "src/Modules/TenantManagement/Kordra.Modules.TenantManagement.csproj", "src/Modules/TenantManagement/TenantMembershipService.cs", "src/Modules/TenantManagement/packages.lock.json"]
test_paths: ["tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/TenantManagement.Tests/TenantManagement.Tests.csproj", "tests/TenantManagement.Tests/TenantMembershipTests.cs", "tests/TenantManagement.Tests/packages.lock.json"]
planned_source_paths: ["src/Modules/TenantManagement/Kordra.Modules.TenantManagement.csproj", "src/Modules/TenantManagement/Contracts/Kordra.Modules.TenantManagement.Contracts.csproj", "src/Modules/TenantManagement/Contracts/TenantAuthority.cs", "src/Modules/TenantManagement/Infrastructure/TenantDbContext.cs", "src/Modules/TenantManagement/Infrastructure/TenantDbContextFactory.cs", "src/Modules/TenantManagement/TenantMembershipService.cs"]
planned_test_paths: ["tests/TenantManagement.Tests/TenantManagement.Tests.csproj", "tests/TenantManagement.Tests/TenantMembershipTests.cs"]
---

# FEAT-TEN-001 — Organization membership lifecycle

## Foundation implementation slice — approved for implementation 2026-10-20

Luna T04 in the implementation handoff follows authentication/audit. Actor local User; catalog org.manage and membership.assign-role at verified organization scope. Accepted ADR-0012 assigns TEN scoped-assignment ownership and IAM definition interpretation; accepted authority/admission v1 supplies exact command DTOs, lifecycle, ordering and bounds. This approved scope authorizes the slice for implementation; it is not implementation evidence.

Deliver durable Active/Suspended tenant, Active/Revoked membership and Active/Revoked/Expired exact-scope assignment, monotonic authority revisions, self membership query, assignment/revoke and tenant suspension. Offline explicit workspace initialization supplies founding authority; account enrollment creates no membership. Pre-enrolled local principals avoid email/invitation delivery in this first slice; the broader invitation lifecycle remains incomplete. Role names never control authorization; no platform-to-tenant inheritance or parent-scope conversion.

Acceptance TEN-F1: one principal belongs to A and B without merged scope. TEN-F2: same manifest/key replay returns one result; conflicting hash, wrong tenant/member, stale ETag or grant above grantor ceiling rejects safely. TEN-F3: suspension/revoke races protected reads through the common writer gate; no new old-authority admission after commit. TEN-F4: state/audit crash or ACK loss reconciles without unrecorded success or revived grant. Include expiry equality, retired definition and ambiguous effective assignment. Planned roots: TEN module and tests/TenantManagement.Tests/, mapped only after creation.


## User story / outcome
**Actor:** Tenant Admin. **Goal:** Organization membership lifecycle. **Primary owner:** `MOD-TEN`. **Phase:** MVP.

## Pre-conditions
Organization active; actor được cấp permission tương ứng.

## Main flow
Create invitation → verify email/identity → accept once → membership scope → bind role template with grantor check → audit.

## Alternatives / exceptional flow
Invitation expired/revoked; org suspended; user already belongs org; role template updated.

## Messages and integration contracts
`tenant.membership.changed.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), MOD-TEN remains the source of organization status and membership lifecycle facts. Any future owner result must bind the exact subject and tenant lookup to authoritative status, revision, validity and revocation evidence; caller-supplied tenant/principal IDs or fixture flags are not authority. Membership in multiple tenants never merges scope. Missing, stale, suspended, revoked, conflicting or unavailable evidence remains unresolved and denies protected access without disclosing tenant existence.

At the original I2.7 proposal, role-assignment persistence/resolution and formal ownership were open. Accepted ADR-0012 resolves ownership for this Foundation slice: MOD-TEN owns scoped assignment facts, IAM interprets RoleDefinition, and NukeGeng is accountable for the approved slice. Broader invitation/lifecycle scope and runtime evidence remain incomplete; the earlier I2.7 approval alone adopted no contract or API.

## Negative & recovery scenarios
TenantAdmin A mời người vào B; tự cấp platform.*; replay invitation; suspend bypass.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Một user thuộc A+B nhưng dữ liệu vẫn tách; grant không vượt quyền người cấp.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## Source & test traceability
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.

## T04 bounded implementation — 2026-10-22

Accepted ADR-0012 and authority/admission-v1 govern this owner slice. Implementation remains unpublished. T04 adds exact versioned IAM definition ports first, then TEN-owned tenant/membership/scoped-assignment persistence and handlers. Actor identity comes from trusted composition, never command-body subject claims. Public HTTP routes, invitations/email and positive inventory remain disabled until T06. Role definitions describe permission/scope rules; only TEN assignments create tenant authority. Required negative tests include stale/retired definitions, deny rules, unknown catalog/scope, foreign tenant/member, grant beyond grantor ceiling, revision conflict, expiry equality, ambiguous assignments, duplicate/conflicting commands, audit rollback/unknown outcome and cancellation. No production migration or real account/tenant provisioning is performed. The owner explicitly authorizes Sol Medium for this T04 only; global AMR routing is unchanged.

## T04 TEN owner slice — local implementation 2026-10-22

FEAT-TEN-001 / MOD-TEN owns ten_organizations, ten_memberships, ten_assignments and hashed command receipts in its own context/history. IAM is consumed only through BCL Contracts; TEN does not reference IAM/AUD runtime assemblies. Host composition alone enlists AUD in the TEN transaction. Additive offline migrations reject Down; the guarded migrator target is local kordra/kordra_admin, while all executed mutation tests use only kordra_test/kordra_test.

Actor and use case: offline LocalMaintainer explicitly creates founding tenant/member/grants with installation lease and stopped API; ordinary authenticated subject selection is supplied by a trusted session adapter. Online owner commands require current org.manage or membership.assign-role plus the grantor's exact current action/scope ceiling. No body principal or role label grants authority. All state changes use the injected common writer gate and current revision; queries hold read admission or require their trusted caller to hold it. Tenant suspension, membership/assignment revocation, expiry equality, retired/current-definition mismatch, ancestor invalidation, explicit deny and ambiguous grants fail closed.

Scope association comes from a required trusted PRJ/INV owner port; there is no positive default implementation. Founding grants are explicit, exact scoped and audited. Delegation records exact source assignment/revision provenance and cannot outlive any authorizing source. Composite tenant/member foreign keys prevent cross-tenant assignment. Minimal self-membership results include only references/revisions, never names or merged permissions. Defensive overflow limits (32 rules/candidates/provenance depth, 50 selector references) deny rather than truncate authorization facts.

TenantMembershipTests exercises isolated real PostgreSQL migrations, same-principal multi-tenancy, grantor ceiling, foreign membership/FK rejection, replay/conflict, stale revisions, deny/ambiguity, retirement, expiry, revocation/suspension, admission cancellation, authority loss during AUD staging, and lost commit ACK recovery without duplicate effects. Pre-commit enlistment failure never poisons admission. Owner state, receipt and audit commit together; unknown commit closes admission until reconciliation/restart under the existing boot protocol.

T04 provides owner handlers and test composition only. HTTP routes remain unregistered until T06. Full offline workspace manifest composition with explicit PRJ/INV resources depends on T05/T06; no real account/tenant provisioning, runtime migration, cluster connection or invitation/email flow was performed. The wider feature remains proposed outside this approved Foundation slice.

## PR12 receipt reconciliation correction — 2026-10-23

FEAT-TEN-001 / MOD-TEN; finding 4345647477. ExecuteAsync still requires the current trusted authenticated subject and an active IAM account, then reconciles the subject/content-bound durable receipt before resolving mutable TEN authority. This permits an exact retry after the committed command suspends its own tenant or revokes its own membership/assignment; it does not restore grants. Changed content or another subject cannot claim the same receipt; new commands still require current effective permission and scope. No public schema, permission model, migration or accepted ADR changes. TenantMembershipTests reproduces the previous suspend/membership retry failures and covers all three self-invalidating operations, conflicting content, fresh-key denial, foreign/anonymous subject and disabled account. Existing lost-ACK and audit/atomicity tests remain required.
