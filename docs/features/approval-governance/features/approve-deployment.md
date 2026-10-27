---
id: FEAT-APR-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-APR
feature_id: FEAT-APR-001
phase: MVP
source_paths: []
test_paths: []
planned_source_paths: ["src/Modules/ApprovalGovernance/Domain/", "src/Modules/ApprovalGovernance/Contracts/", "src/Modules/ApprovalGovernance/Kordra.Modules.ApprovalGovernance.csproj", "src/Modules/ApprovalGovernance/Infrastructure/", "src/Modules/ApprovalGovernance/ApprovalRequestStore.cs", "src/Modules/ApprovalGovernance/ApprovalEligibility.cs", "src/Host/Kordra.Api/Composition/Approvals/", "src/Host/Kordra.Api/Endpoints/Approvals/", "src/Host/Kordra.Api/OfflineAdministration/Approvals/"]
planned_test_paths: ["tests/ApprovalGovernance.Domain.Tests/", "tests/ApprovalGovernance.Tests/"]
---

# FEAT-APR-001 — Decide an immutable release intent with separation of duties

## Goal / Actor

An authenticated requester queues one exact Frozen release revision; an authorized approver reviews its exact subject and grants/rejects; an authorized actor can revoke it. This proposed T11.2 slice under [ADR-0005](../../../decisions/0005-immutable-release.md) uses [Release and approval v1](../../../contracts/release-approval-v1.md), not the unaccepted earlier deployment.approve sketch. NukeGeng is proposed slice custodian, pending acceptance.

## User story / outcome

The Goal / Actor and main flow define the user-visible outcome; proposed status does not imply implemented behavior.

## Preconditions

Real FEAT-APR-002 policy/binding and FEAT-REL-001 Frozen source, current IAM/TEN/PRJ/INV/ART owner ports and qualified Development/Test pilot selection. No fake dependency or target.read permit.

## Pre-conditions

The Preconditions above are mandatory and are revalidated at use, not replaced by cached test fixtures.

## Business rules and invariants

The contract alone defines request/decision identity, one request ever per revision, exact frozen subject/hash/scope/policy, immutable decisions and continuing current eligibility. Independent means different principal IDs; explicit self_allowed is Development/Test only and requires all scoped capabilities. T11 uses release.approve; every result declares executionAuthorized=false. Original deadlines never extend or resurrect. Historical recorded state and fresh effective state differ explicitly; dependency unavailability is never a Granted fallback. Sessions are necessary for HTTP actions, not durable worker credentials.

## Main flow

Current request/subject checks → one Pending request + atomic AUD/floor → fresh exact-subject grant/reject with If-Match and reason → bounded authorized queue/detail. Revoke is a separate current-permission action. Approved content cannot be substituted; changed/retired/policy/authority revisions invalidate eligibility.

## Alternative & failure flow

Wrong tenant/scope, missing/denied/expired/revoked grant, unsupported qualification, self-approval under independent policy, blocked window and stale CAS do not grant. Concurrent decisions serialize, one Pending winner; later explicit revoke can invalidate a previously committed grant. Same key cannot grant twice. After terminal/expired/invalidated state, another request requires a new reviewed release revision. Unknown commit preserves exact history and fences, never guesses Granted/Rejected.

## Authorization + tenant-scope table

| Action | Current exact scope |
|---|---|
| Request | release.request-approval environment plus release.read project |
| Queue/detail | release.approval.read environment; bounded exact subject only |
| Grant/reject | release.approve plus release.approval.read environment; extra grant-only policy/requester checks |
| Revoke | release.approval.revoke plus release.approval.read environment; no actor-label bypass |

Policy cannot create any grant. Unknown/missing/conflicting owner facts deny, and complete deny rules apply independently per action.

## Authorization / tenant-scope review

The scoped authorization table above is mandatory. Tenant comes from trusted owner/session facts, never client body or a role label; missing/foreign/unavailable authority cannot produce success.

## Commands, events, APIs & schema versions

Exact HTTP v1 approval routes, fields/envelopes/ETags/error/body limits are in contract section 7. No approval.granted.v1 message, deployment endpoint, worker permit or workflow release is published. Later execution plan approval requires separately adopted semantics and immutable plan binding.

## Data ownership & migration

Second APR migration adds scoped requests, append-only decisions and command receipts to its own DbContext; REL/APR runtime references cannot form a cycle. Host maps neutral Contracts and controls one owner/AUD/floor commit. SQL enforces one request per revision, CAS/state/immutable decisions and tenant consistency.

## Idempotency, timeouts, compensation

Use contract-scoped canonical key/business hashes, exact receipt reconciliation and bounded current U/D. Confirmed M stays committed when D denies. Pre-commit setup failure leaves auth usable; actual unknown commit fences. No scheduler, automatic replay, grant restoration, rollback-to-old-policy or provider compensation.

## Health, audit, metrics, tracing

AUD stores safe decision/subject hashes and fixed codes only. No cookie, raw key, spec body, credential or private infrastructure detail. Expose honest effective eligibility and original deadlines, not deployable/healthy/productionApproved.

## Acceptance criteria (Given/When/Then)

- APR-F1: exact Frozen content, requester/approver current grants, policy/binding and original deadlines are bound to one durable request/decision.
- APR-F2: independent and explicit solo pilot paths behave as selected; Staging/Production qualification and execution stay blocked.
- APR-F3: changed owner/security revision, expiry equality, blocked window, restart and change-and-restore cannot resurrect eligibility.
- APR-F4: actual competing decisions, duplicate keys, partial rollback and true lost ACK have one authoritative history; no guessed success.
- APR-F5: real-session/current-owner HTTPS queue/detail and pre-header interleavings with exact schema/size/cursor/foreign-record checks pass; current gates and independent PR review precede human merge.

## Test cases (positive, negative, duplicate, crash, tenant isolation)

R11-02/03 and R11-05–16 in the contract, including stale assignment/role, retired release, unavailable policy, creator/requester/approver identity separation, clock rollback and both commit orderings. Future worker crash/delivery/dispatch tests remain T12/T13; do not simulate a fake worker success and claim them here.

## Negative & recovery scenarios

The alternative/failure flow and referenced R11 cases cover wrong tenant, missing/expired/revoked authority, duplicates, interrupted writes and unknown commit; worker/dispatch cases remain later scope. No destructive rollback or blind replay is permitted.

## Source/test/evidence mapping

No APR request/decision runtime exists at baseline. Future files/tests go into planned paths only after exact design acceptance and real policy/REL dependencies. Product/CI/review evidence remains pending, not supplied by this design.

## Source & test traceability

Use the empty actual mappings and planned-path distinction above. Implementation must attach real commands, source revisions and test results to this feature and the shared traceability log before completion.

## Open decisions, linked ADR and technical debt

The exact proposal is ready for human review; no acceptance is inferred. Deployment approval/override/worker authority remains a separate future decision. No debt opened/closed merely by documenting it.
