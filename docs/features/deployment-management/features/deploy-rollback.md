---
id: FEAT-DEP-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-DEP
feature_id: FEAT-DEP-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-DEP-001 — Deploy and rollback with verified outcome

## User story / outcome
**Actor:** Operator / Workflow service. **Goal:** Deploy and rollback with verified outcome. **Primary owner:** `MOD-DEP`. **Phase:** MVP.

## Pre-conditions
Release approved; target lease acquired; driver capability verified.

## Main flow
Preflight → execute typed driver plan → observe rollout → business health probes → record verified outcome + audit.

## Alternatives / exceptional flow
Failed verification triggers policy decision; rollback uses prior pinned digest/config and fresh authorization.

## Messages and integration contracts
`deployment.dispatch.v1; deployment.verified.v1; deployment.rollback.requested.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Blind retry after unknown, rollback unsupported driver, missing current target ownership.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
No deployment succeeds without observation; rollback result independently verified.
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
