---
id: FEAT-APR-002
status: implemented
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-APR
feature_id: FEAT-APR-002
phase: MVP
source_paths: ["src/Host/Kordra.Api/OfflineAdministration/Approvals/", "src/Host/Kordra.DbMigrator/ApprovalGovernanceMigrationCommand.cs", "src/Modules/ApprovalGovernance/ApprovalPolicyStore.cs", "src/Modules/ApprovalGovernance/packages.lock.json", "src/Modules/ApprovalGovernance/Domain/", "src/Modules/ApprovalGovernance/Contracts/", "src/Modules/ApprovalGovernance/Infrastructure/", "src/Modules/ApprovalGovernance/Kordra.Modules.ApprovalGovernance.csproj"]
test_paths: ["tests/ApprovalGovernance.Domain.Tests/ApprovalGovernance.Domain.Tests.csproj", "tests/ApprovalGovernance.Domain.Tests/packages.lock.json", "tests/ApprovalGovernance.Tests/ApprovalGovernance.Tests.csproj", "tests/ApprovalGovernance.Tests/packages.lock.json", "tests/ProjectEnvironment.Tests/ApprovalMigrationCommandTests.cs", "tests/ProjectEnvironment.Tests/ApprovalPolicyCoordinatorTests.cs", "tests/ApprovalGovernance.Domain.Tests/ReleaseApprovalPolicyTests.cs", "tests/ApprovalGovernance.Domain.Tests/ApprovalPolicyManifestTests.cs", "tests/ApprovalGovernance.Tests/ApprovalPolicyPersistenceTests.cs"]
planned_source_paths: ["src/Modules/ApprovalGovernance/Domain/", "src/Modules/ApprovalGovernance/Contracts/", "src/Modules/ApprovalGovernance/Kordra.Modules.ApprovalGovernance.csproj", "src/Modules/ApprovalGovernance/Infrastructure/", "src/Modules/ApprovalGovernance/ApprovalPolicyStore.cs", "src/Host/Kordra.Api/OfflineAdministration/Approvals/", "src/Host/Kordra.DbMigrator/ApprovalGovernanceMigrationCommand.cs"]
planned_test_paths: ["tests/ApprovalGovernance.Domain.Tests/", "tests/ApprovalGovernance.Tests/", "tests/ProjectEnvironment.Tests/ApprovalMigrationCommandTests.cs", "tests/ProjectEnvironment.Tests/ApprovalPolicyCoordinatorTests.cs"]
planned_doc_paths: ["docs/features/approval-governance/features/approval-policy.md", "docs/operations/release-approval-policy-v1.md", "docs/engineering/traceability.md", "docs/engineering/change-history.md"]
---

# FEAT-APR-002 — Provision an exact release-approval policy

## Goal / Actor

Trusted local maintainer publishes/revises/deactivates an explicit approval business policy and binds it to actual project environments. This is the standalone complete T11.0 feature needed before REL freeze, accepted under [ADR-0005](../../../decisions/0005-immutable-release.md). NukeGeng is custodian; [Release and approval v1](../../../contracts/release-approval-v1.md) section 3 is normative for approved T11.0.

## User story / outcome

The Goal / Actor and main flow define the user-visible outcome; proposed status does not imply implemented behavior.

## Preconditions

Existing canonical installation identity, exclusive stopped API custody, private admin locator and reviewed exact manifest hash; current PRJ tenant/project/environment identity and classification. No startup seed or positive default. This is not a public policy-management API.

## Pre-conditions

The Preconditions above are mandatory and are revalidated at use, not replaced by cached test fixtures.

## Business rules and invariants

APR owns immutable policy revisions/current heads and exact environment-purpose bindings. Business policy restricts IAM grants and cannot confer a permission. It is separate from accepted Resource Policy v1 target.read and its PRJ binding. Exact payload/schema, deny priority, fixed UTC windows, explicit lifetimes, pilot qualification and independent/self_allowed constraints are defined once in the contract. No wildcard, inheritance, generic expression, recurrence or emergency override.

## Main flow

Prepare private closed manifest → inspect exact current heads/bindings and canonical hash → maintainer approves that content → apply under custody with owner/AUD/floor transaction → retain immutable policy/command/audit evidence. Revision/head/binding changes are monotonic.

## Alternative & failure flow

Unknown/duplicate fields, foreign/inactive/ambiguous environment, invalid time/classification/SoD or stale revision rejects. Same manifest/hash reconciles its exact durable result; changed input conflicts. Failed partial migration or missing schema supplies no policy fallback. Actual commit uncertainty fences until stopped evidence reconciliation. Deactivation never rewrites historical payload or restores an old grant.

## Authorization + tenant-scope table

| Operation | Authority |
|---|---|
| Inspect/apply/reconcile | Trusted stopped local maintainer, exact installation/admin identity, OS custody and reviewed hash |
| Runtime policy read | Internal owner port under held shared admission, exact tenant/project/environment/purpose; no standalone permit |

There is no HTTP permission, role label or implicit SuperAdmin exemption. Real installation provisioning is separately authorized; design approval is not that authority.

## Authorization / tenant-scope review

The scoped authorization table above is mandatory. Tenant comes from trusted owner/session facts, never client body or a role label; missing/foreign/unavailable authority cannot produce success.

## Commands, events, APIs & schema versions

New closed offline policy manifest schemaVersion 1 and canonical domain are in the contract. Extend existing grouped Host inspect/apply/reconciliation commands; no network endpoint or integration event. T06 policy hashes and native credential protocols remain unchanged.

## Data ownership & migration

First APR owner migration adds only policy revisions/heads/bindings/history/receipts with exact tenant constraints and minimum role ACLs. Pure Domain/BCL Contracts, own runtime infrastructure and Host composition. No request/decision routes are claimed by this feature. No real DDL, new dependency or destructive rollback.

## Idempotency, timeouts, compensation

Exact manifest ID/hash plus owner/AUD receipt; all selected binding changes commit atomically. Setup failures roll back; true ACK uncertainty is not success. Disable feature for rollback and retain monotonic evidence, never restore an old head.

## Health, audit, metrics, tracing

Existing safe AUD hashes/IDs, floor and bounded offline operation; no raw manifest/configuration/secret logs. No provider/Keychain access or new monitoring service.

## Acceptance criteria (Given/When/Then)

- POL-F1: independent canonical vectors and every policy/rule/window bound are deterministic and reject malformed input.
- POL-F2: stopped exact current environment binding is required; stale/foreign/classification mismatch or released custody produces no change.
- POL-F3: policy plus all bindings/receipts/AUD/floor commit once; duplicate, concurrent stale writer and real lost ACK are reconciled without fallback.
- POL-F4: immutable SQL/history, exact migration/ACLs and partial nonempty migration rollback pass with existing T01–T10 compatibility.
- POL-F5: complete owner feature docs/gates/commit/PR and default Codex review; no release/approval-request implementation claim.

## Test cases (positive, negative, duplicate, crash, tenant isolation)

R11-02/03/06/08/09/10/12/14/15/16, synthetic owner/PostgreSQL fixtures only unless separately authorized. No fake positive policy in production; no change to existing LAB or business queues.

## Negative & recovery scenarios

The alternative/failure flow and referenced R11 cases cover wrong tenant, missing/expired/revoked authority, duplicates, interrupted writes and unknown commit; worker/dispatch cases remain later scope. No destructive rollback or blind replay is permitted.

## Source/test/evidence mapping

At the inspected baseline no APR runtime source/tests existed. The mapped Domain and Domain.Tests now contain a partial local policy parsing/canonicalization increment; persistence and offline execution remain unimplemented. Existing shared IAM/PRJ/Host paths remain owned by their own features and require matching updates.

## Source & test traceability

Use the actual Domain mappings and the planned-path distinction above. Implementation must attach real commands, source revisions and test results to this feature and the shared traceability log before completion.

## Open decisions, linked ADR and technical debt

Exact ADR-0005/contract/feature scope was approved by NukeGeng on 2026-10-27; implementation remains in progress. Future execution policy/override/quorum is outside this v1 decision. No debt added/closed or runtime verification is claimed.

## T11.0 domain increment — 2026-10-27

Approved ADR-0005 and contract: the first local increment introduces the pure APR policy payload parser and canonical hash tests only. No policy is persisted, bound, provisioned or exposed by this increment; remaining T11.0 migration, owner ports, stopped command, transactional audit and R11 gates remain outstanding. The existing target.read Resource Policy v1 remains unchanged.

Local validation: the focused APR Domain project passed 18 tests, including an independently computed SHA-256 reference vector, duplicate/unknown fields, time/TTL bounds, SoD restrictions, overlapping windows and deny precedence. An actual solution XML/restore architecture-graph test passed after adding the two projects. These results do not establish POL-F1–F5 in full or T11.0 completion. No offline command, current owner binding, SQL migration, HTTP action or authorization was added.

Further local increment: closed manifest parsing, canonical command hashing and exact environment binding/revision validation were added to the APR Domain; focused Domain suite now passes 27 cases. Scoped IAM catalog (14 focused cases) and neutral PRJ environment reader (2 isolated PostgreSQL tests) are implemented in their owner paths; they do not create grants or publish policy. Offline command/store/migration/audit/custody and full T11.0 gates remain outstanding.

## T11.0 local storage and offline composition — in progress

APR now owns the additive local policy/head/binding/history/receipt model and migration, with immutable SQL triggers, exact monotonic head/binding revisions, runtime read-only policy privileges, own Domain/BCL Contracts/runtime projects and guarded offline DbMigrator command. The Host offline inspect/apply coordinator composes current TEN/PRJ facts, APR changes, AUD and IAM clock floor on one connection/transaction; it validates exact manifest/hash and reconciles exact stored policy/audit receipts. It verifies an APR command receipt even when AUD reports durable, so an audit-only duplicate after owner rollback cannot be reported as policy success. Unknown attempted commit fences; pre-commit setup failure does not. The minimal command path holds the existing installation lock and does not load TLS/Keychain/provider configuration. [Operations guide](../../../operations/release-approval-policy-v1.md) describes the separate real-operation boundary.

Focused local evidence: 28 APR Domain cases; 11 isolated PostgreSQL APR storage/SQL/role cases, including rollback of a partial migration in a transaction-owned synthetic schema; 12 PRJ/TEN/AUD offline coordination cases including exact audit/owner receipt checks; migration role guard; 6 XML/assembly architecture tests. The shared migration was applied only to isolated kordra_test; no real installation policy, schema or grant was created. Full feature gates, Code Review and human merge remain pending, as do owner/catalog HTTP release and decision features.

## T11.0 coordinator and SQL evidence — 2026-10-27

Targeted real PostgreSQL tests exercised atomic policy/binding history plus AUD/floor, duplicate and competing manifests, multiple bindings with a late stale owner, binding revision history, missing/contradictory audit, loss of commit acknowledgement, an audit-only durable result following owner rollback, and pre-commit failure that leaves admission open. A test-only UUID installation uses the actual private OS lock and confirms policy work needs no TLS key or runtime config. APR receipt reconciliation verifies original policy bytes/hash and all exact binding-history rows. These are source-bound local test results; actual Code Review, complete gate and merge remain separate.

## T11.0 local implementation and gates — 2026-10-27

The approved policy feature is implemented locally: closed canonical policy/manifest, seven exact IAM permission/scope pairs without grants, neutral PRJ/TEN owner reads, isolated APR persistence/migration and guarded offline inspect/apply using atomic APR/AUD/clock-floor transactions. Source mappings above refer to actual paths. On the complete current source, `python3 docs/tooling/amr.py gates --base bc91c2a66abcdb2156363f5fdbd3dc7f89046e67` passed docs/scope/mapping, 218 tooling tests, locked restore, Release build and 1,220 Product tests. One separately scoped live Nomad test was skipped; it is not part of T11 policy verification. Earlier one native synthetic Keychain test failed once at roughly two seconds and subsequently passed both in isolation and the full feature gate; its intermittent cause was not established. Hosted CI, independent Codex PR review and human merge remain pending. No real installation migration, policy provisioning, existing secret or provider access was performed. Release creation and approval decisions T11.1–T11.3 remain unimplemented.


## T11.2 shared-owner compatibility — 2026-10-30

FEAT-APR-001 adds requests, immutable decisions and command receipts to this module's shared ApprovalDbContext, factory, migration snapshot and offline Approvals directory. The new decision/deadline/effective-state domain helpers belong to the request workflow, while FEAT-APR-002 remains the owner of immutable policy publications and environment bindings. Both features document these overlapping source mappings; neither mapping nor validation is relaxed.

The additive request/decision and deferred consistency migrations preserve policy tables, revision heads, command receipts and the existing policy inspect/apply contract. Runtime/offline factories retain exact target/role checks and installation custody. The new request recovery commands do not publish/rebind policy, replay a decision, create grants or authorize deployment. Policy canonical payload/binding hashes, permission semantics and accepted ADR-0005 are unchanged. Shared migration/runtime compatibility is covered by the existing policy tests together with request persistence/recovery and full Product validation; operational startup/restart acceptance is recorded in FEAT-REL-001/FEAT-APR-001.

The first hosted PR #21 docs check exposed this missing owner update after squash-history reconciliation; the committed-range changeguard check is required in addition to the working-tree gate. The correction documents actual shared-owner impact and does not remove tests, bypass source mapping or claim hosted review approval.
