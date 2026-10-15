---
id: FEAT-MSG-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-MSG
feature_id: FEAT-MSG-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-MSG-001 — Durable command delivery and recovery

## User story / outcome
**Actor:** System / Platform Operator. **Goal:** Durable command delivery and recovery. **Primary owner:** `MOD-MSG`. **Phase:** MVP.

## Pre-conditions
Envelope validated and target consumer registered.

## Main flow
Persist business write + outbox → relay → consumer inbox → handler → store result → ack → metrics.

## Alternatives / exceptional flow
Transient retry bounded; terminal failure DLQ with redrive authorization and observation before re-execution.

## Messages and integration contracts
`deployment.dispatch.v1; message.dead-lettered.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Receive duplicated message; worker crash before ACK; message from wrong tenant; replay with invalid policy.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
No lost committed command in tested crashes; duplicate transport delivery does not duplicate external mutation.
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
