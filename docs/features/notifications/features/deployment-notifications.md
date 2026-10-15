---
id: FEAT-NOT-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-NOT
feature_id: FEAT-NOT-001
phase: Phase 2
source_paths: []
test_paths: []
---

# FEAT-NOT-001 — Notify authorized subscribers of release events

## User story / outcome
**Actor:** Developer / Operator / System. **Goal:** Notify authorized subscribers of release events. **Primary owner:** `MOD-NOT`. **Phase:** Phase 2.

## Pre-conditions
Subscription tenant-scope and user communication preference validated.

## Main flow
Consume deployment event → evaluate subscription → redact payload → dedup delivery → send → record outcome.

## Alternatives / exceptional flow
Transient provider error retry bounded/DLQ; do not block deployment completion.

## Messages and integration contracts
`notification.send.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Notify users in wrong org, expose access token, unbounded duplicate spam.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Only scoped subscribers notified; retries auditable without affecting deployment workflow.
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
