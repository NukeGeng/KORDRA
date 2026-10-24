---
id: FEAT-TEN-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-23
module_id: MOD-TEN
feature_id: FEAT-TEN-001
phase: MVP
source_paths: ["Kordra.slnx", "src/Host/Kordra.Api/Audit/TenantAuditTransactionAdapter.cs", "src/Host/Kordra.DbMigrator/TenantManagementMigrationCommand.cs", "src/Modules/TenantManagement/Contracts/Kordra.Modules.TenantManagement.Contracts.csproj", "src/Modules/TenantManagement/Contracts/TenantAuthority.cs", "src/Modules/TenantManagement/Contracts/packages.lock.json", "src/Modules/TenantManagement/Infrastructure/Migrations/20261022041945_InitialTenantPersistence.Designer.cs", "src/Modules/TenantManagement/Infrastructure/Migrations/20261022041945_InitialTenantPersistence.cs", "src/Modules/TenantManagement/Infrastructure/Migrations/TenantDbContextModelSnapshot.cs", "src/Modules/TenantManagement/Infrastructure/TenantDbContext.cs", "src/Modules/TenantManagement/Infrastructure/TenantDbContextFactory.cs", "src/Modules/TenantManagement/Kordra.Modules.TenantManagement.csproj", "src/Modules/TenantManagement/TenantMembershipService.cs", "src/Modules/TenantManagement/TenantOrganizationAuthority.cs", "src/Modules/TenantManagement/packages.lock.json"]
test_paths: ["tests/IdentityAccess.Tests/CurrentAuthorityHttpTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/TenantManagement.Tests/TenantManagement.Tests.csproj", "tests/TenantManagement.Tests/TenantMembershipTests.cs", "tests/TenantManagement.Tests/packages.lock.json"]
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

## T05 shared-owner integration — 2026-10-22

T05 test composition resolves exact PRJ owner scopes through its own Contracts/store and tests real TEN scoped grants for catalog/target operations. TEN source, persistence and permission rules are unchanged; its test lock graph expands transitively through Host. No cookie authority, positive default or production resource adapter was introduced.

## T06 implementation evidence — 2026-10-23

T06 consumes current detailed scoped-grant and active-organization owner facts under already-held admission; no nested gate or cross-owner table read is introduced. Shared production composition enlists the IAM clock floor with TEN state/AUD. The previously observed self-suspend/self-revoke command-replay defect is corrected locally: current trusted active subject and exact subject/body receipt are checked before mutable grant authority; replay cannot mint a new operation. Two additional TEN regressions retain invalidated state and deny changed-body/new-command/anonymous attempts. Real current TEN grants feed the HTTPS metadata/v2 tests; these do not open a TEN management HTTP API or claim remote review findings resolved on old PR heads.

## PR14 provenance deadline correction — 2026-10-23

FEAT-TEN-001 / MOD-TEN, consumed by IAM-001; finding 4355763873. The shared TEN resolver now returns validation plus the minimum non-null expiry of the selected assignment and every validated provenance branch, including nested/shared ancestors. An invalid chain remains absent authority; a valid chain with no deadlines remains unbounded. Current scoped-grant facts and grantor-ceiling checks consume this same effective deadline. No role/scope/permission, wire shape, schema, owner boundary or accepted ADR changes.

Regression fixtures use synthetic stored chains with earlier upstream expiry, including a shared DAG ancestor, a later/unbounded leaf and a shorter leaf. This does not claim ordinary grant creation permits overlong delegation: existing creation ceilings remain enforced. Four cases failed before repair, including an expired upstream source during D audit still completing handoff. Real TEN persistence plus the IAM admission engine now reject expiry at U/D audit equality before adapter/handoff; legitimate no-expiry reads and already accepted bounded transport retain their accepted behavior. Cycle, stale/missing source and expired ancestor cases stay denied; valid unbounded provenance stays valid. No real infrastructure/provider operation or additional AI review is performed.

## PR14 Linux timestamp fixture correction — 2026-10-23

The P1 provenance fixture now always injects a sub-microsecond input and reloads committed expiry values from PostgreSQL before computing its independent expected deadline. The prior pre-save .NET value differed by 100 ns ticks on Linux. All three variants reproduce that mismatch with forced input before the reload correction. Exact equality and equality-at-expiry assertions remain intact; no tolerance, test skip, production clock/grant semantics, schema or contract change is introduced.


### T07 bounded-read consumer acknowledgment — 2026-10-23

Current TEN membership and exact-scope grants remain authoritative at U/D. T07 synthetic fixture revocation/suspension under the common writer gate proves delayed results cannot disclose. No new management route, permission or tenant contract. Shared Host composition and the actual-owner HTTP fixture are mapped here; the new adapter/probe paths belong to [FEAT-INV-001](../../infrastructure-inventory/features/register-target.md). T07 keeps the existing seven-field metadata v1 wire shape.


### T08 shared implementation consumer — 2026-10-24

Accepted ADR-0015 adds the SEC owner runtime/Contracts/test graph and scoped Host/offline migration composition. The existing source/test mappings above cover the shared solution, authority/HTTP/audit/architecture consumers changed in the same increment. [FEAT-SEC-001](../../connection-secrets/features/secret-binding.md#t08-local-implementation--2026-10-24) owns exact new paths and local evidence. IAM neutral dependencies/current C intent do not add a circular SEC runtime edge; AUD keeps v1 stages/schema, metadata wire v1 and existing role permissions remain unchanged. Synthetic fixture/native proof does not authorize provider network access or change this module's business ownership.
