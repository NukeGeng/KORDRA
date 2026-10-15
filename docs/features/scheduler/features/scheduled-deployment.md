---
id: FEAT-SCH-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-SCH
feature_id: FEAT-SCH-001
phase: Phase 2
source_paths: []
test_paths: []
---

# FEAT-SCH-001 — Run scheduled deployments without bypassing policy

## User story / outcome
**Actor:** Release Manager / System. **Goal:** Run scheduled deployments without bypassing policy. **Primary owner:** `MOD-SCH`. **Phase:** Phase 2.

## Pre-conditions
Future schedule and approved/policy-valid spec selected.

## Main flow
Persist schedule(timezone) → trigger idempotently → recheck approval/policy/cutoff → enqueue deployment → record execution.

## Alternatives / exceptional flow
Missed run follow catch-up policy; approval expired creates PendingApproval, not auto deploy.

## Messages and integration contracts
`deployment.requested.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Duplicate fire, execution outside allowed window, policy changed after scheduling.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
At most one logical scheduled run per schedule occurrence; audit records skipped reasons.
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
