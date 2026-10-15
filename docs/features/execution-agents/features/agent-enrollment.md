---
id: FEAT-AGT-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-AGT
feature_id: FEAT-AGT-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-AGT-001 — Enroll, route and recover agents

## User story / outcome
**Actor:** Platform Operator / Tenant Operator / Agent. **Goal:** Enroll, route and recover agents. **Primary owner:** `MOD-AGT`. **Phase:** MVP.

## Pre-conditions
Target association approved and enrollment credential short-lived.

## Main flow
Issue scoped enrollment ticket → agent enrolls → rotate identity cert → heartbeat → dispatch typed operation → journal → receipt replay.

## Alternatives / exceptional flow
On disconnect mark Unknown, do not blind retry; revoke agent immediately denies new commands.

## Messages and integration contracts
`agent.lifecycle.changed.v1; deployment.accepted.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Agent mapped to wrong org, expired token, spoofed heartbeat, stale command after reconnect.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Every agent command bound to verified identity, target, specHash, operationId and lease epoch.
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
