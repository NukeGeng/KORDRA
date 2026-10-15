---
id: FEAT-WFL-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-WFL
feature_id: FEAT-WFL-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-WFL-001 — Durable deployment workflow through failures

## User story / outcome
**Actor:** System / Operator Recovery. **Goal:** Durable deployment workflow through failures. **Primary owner:** `MOD-WFL`. **Phase:** MVP.

## Pre-conditions
Valid deployment request persisted with immutable correlation IDs.

## Main flow
Persist step → validate approval → acquire lease → write dispatch command to outbox → accept agent result → verify → finalize/release lease.

## Alternatives / exceptional flow
If agent outcome missing mark Unknown; inspect target/journal before redelivery; operator can resolve with evidence.

## Messages and integration contracts
`deployment.requested.v1; deployment.dispatch.v1; deployment.unknown.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Restart between DB write and send, stale lease epoch, duplicate command, expired approval.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Crash at each transition leads to one logical deployment operation and consistent audit.
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
