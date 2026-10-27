---
id: FEAT-REL-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-REL
feature_id: FEAT-REL-001
phase: MVP
source_paths: []
test_paths: []
planned_source_paths: ["src/Modules/ReleaseManagement/Domain/", "src/Modules/ReleaseManagement/Contracts/", "src/Modules/ReleaseManagement/Kordra.Modules.ReleaseManagement.csproj", "src/Modules/ReleaseManagement/ReleaseStore.cs", "src/Modules/ReleaseManagement/Infrastructure/", "src/Host/Kordra.Api/Composition/Releases/", "src/Host/Kordra.Api/Endpoints/Releases/", "src/Host/Kordra.Api/OfflineAdministration/Releases/", "src/Host/Kordra.DbMigrator/ReleaseManagementMigrationCommand.cs"]
planned_test_paths: ["tests/ReleaseManagement.Domain.Tests/", "tests/ReleaseManagement.Tests/"]
---

# FEAT-REL-001 — Review and freeze an immutable release intent

## Goal / Actor

User creates a project release Draft, reviews the current artifact/catalog/policy selection, freezes exact bytes and can explicitly retire obsolete revisions. Exact T11.1 proposal under [ADR-0005](../../../decisions/0005-immutable-release.md), awaiting human acceptance. [Release and approval v1](../../../contracts/release-approval-v1.md) is the sole normative T11 rule/HTTP source; future execution remains in the separate proposed release/execution contract.

## User story / outcome

The Goal / Actor and main flow define the user-visible outcome; proposed status does not imply implemented behavior.

## Preconditions

Merged T10 ART registration; current IAM/TEN and active PRJ application/environment/INV target chain; real APR policy feature T11.0 before a positive freeze. Draft selection may be recorded while policy/qualification is ineligible. Existing target.read policy/permit is not release authority. NukeGeng is proposed slice custodian, effective only on exact design acceptance.

## Pre-conditions

The Preconditions above are mandatory and are revalidated at use, not replaced by cached test fixtures.

## Business rules and invariants

The contract defines lineage/revision IDs, one Draft per lineage, creator-only Draft edit/freeze, exact CAS/preview hash, Draft → Frozen → Retired, byte-exact canonical hash and fixed qualification. Copy only immutable ART facts; no claim that source provenance, layers, signature, live availability or an execution plan is verified. No tag resolution, registry/Keychain read, configuration bag or dispatch. Frozen bytes/hash never change. Newer revisions never redirect an old approval; explicit retirement invalidates its current eligibility.

## Main flow

Create lineage/Draft → review server preview → optionally replace with exact If-Match → freeze with expectedSelectionHash → read exact Frozen subject/specHash. A change creates another exact revision. Retire only with explicit current permission and reason code. API response always states executionAuthorized=false.

## Alternative & failure flow

Missing/foreign/ambiguous owner data denies; unavailable data is unavailable, not empty. Owner/policy change since preview returns conflict. Invalid qualification blocks freeze; Staging/Production is not silently downgraded. Stale CAS or changed idempotent input is conflict. Unknown real commit fences; confirmed M survives a denied D and exact stopped receipt reconciliation reports only known facts.

## Authorization + tenant-scope table

| Action | Exact current permission / constraint |
|---|---|
| Create/next Draft | release.create project; trusted same-project owner selection |
| Replace/freeze | release.create project and current Draft creator; If-Match; no role shortcut |
| Read/list | release.read project; tenant from owner |
| Retire | release.retire project; explicit CAS/reason; no resurrection |

The contract defines use-specific neutral owner reads; no implicit catalog/provider read grant.

## Authorization / tenant-scope review

The scoped authorization table above is mandatory. Tenant comes from trusted owner/session facts, never client body or a role label; missing/foreign/unavailable authority cannot produce success.

## Commands, events, APIs & schema versions

New release HTTP v1 routes, exact envelopes, body/ETag/hash rules and bounds are in contract section 7. No event/outbox/message is published in T11. release.created.v1/deployment.requested.v1 remain future candidates; no 202 workflow promise.

## Data ownership & migration

REL owns lineage/revision/canonical bytes/hash/command receipts in its new owner DbContext. Reuse exact approved-on-acceptance EF/Npgsql versions only; pure Domain/BCL Contracts and Host owner composition. Additive isolated migration/ACL/immutable-SQL tests precede route activation. No runtime DDL or destructive Down.

## Idempotency, timeouts, compensation

Use exact contract domains, subject/action/scope/key hashes, one owner/AUD/floor transaction, current authority and caller-bound D. Preserve original receipts and frozen content; no blind replay, rollback of historical approval, remote compensation or background job.

## Health, audit, metrics, tracing

AUD contains safe IDs, intent/result hashes and fixed reasons, never canonical spec/body/session/key. Existing monotonic trusted time and bounded admission apply. No new monitoring service or performance claim; default health-only host stays compatible.

## Acceptance criteria (Given/When/Then)

- REL-F1: given eligible current selection, freeze produces independently reproducible exact bytes/hash and immutable scope/artifact/policy binding.
- REL-F2: given changed/foreign/tag/malformed/oversized/duplicate selection or policy, no Frozen result or provider call occurs; a changed preview requires review again.
- REL-F3: concurrent creator edits/freeze/next-revision/retire respect exact CAS, one Draft and immutable history; new revision does not inherit approval.
- REL-F4: duplicate/ACK-loss/pre-commit failure has one known durable owner/AUD/floor outcome or fenced uncertainty; actual HTTPS D cannot disclose stale authority.
- REL-F5: current source-bound OpenAPI/fixtures, graph, migrations, T01–T10 regressions and gates pass before complete feature PR.

## Test cases (positive, negative, duplicate, crash, tenant isolation)

R11-01–05 and R11-10–16 in the contract, including true PostgreSQL ACK loss, actual session/current owner HTTPS, pre-header expiry, SQL immutability, supported rollback and no registry/secret requirement. A fake authorizer is never registered by production composition.

## Negative & recovery scenarios

The alternative/failure flow and referenced R11 cases cover wrong tenant, missing/expired/revoked authority, duplicates, interrupted writes and unknown commit; worker/dispatch cases remain later scope. No destructive rollback or blind replay is permitted.

## Source/test/evidence mapping

Source/test arrays remain empty because only READMEs exist at baseline bc91c2a. Planned paths identify future task boundaries, not existing code. T11.1a–b implement after exact design acceptance and APR policy dependency, with real mappings/evidence added by Developer. PR/tests/runtime remain pending.

## Source & test traceability

Use the empty actual mappings and planned-path distinction above. Implementation must attach real commands, source revisions and test results to this feature and the shared traceability log before completion.

## Open decisions, linked ADR and technical debt

The complete proposal resolves T11 choices for human review; approval of ADR-0005/contract/feature is still pending. Execution plan/driver/worker/live qualification are explicitly later gates, not missing defaults to implement. No debt is opened or closed merely by authoring this design.
