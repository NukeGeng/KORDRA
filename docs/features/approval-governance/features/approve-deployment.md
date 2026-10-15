---
id: FEAT-APR-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-APR
feature_id: FEAT-APR-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-APR-001 — Approve production deployment with separation of duties

## User story / outcome
**Actor:** Release Approver. **Goal:** Approve production deployment with separation of duties. **Primary owner:** `MOD-APR`. **Phase:** MVP.

## Pre-conditions
Freeze window and policy evaluated; signed release revision available.

## Main flow
Create approval request → independent approver verifies immutable diff → approve/reject with reason → persist decision + audit → release workflow.

## Alternatives / exceptional flow
Expired/revoked approval re-evaluated before dispatch; emergency override separately authorized.

## Messages and integration contracts
`approval.requested.v1; approval.granted.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Requester self-approve, changed hash, approval for other environment, expired grant.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Every production dispatch references valid approval matching frozen spec.
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
