---
id: FEAT-REC-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-REC
feature_id: FEAT-REC-001
phase: Phase 2
source_paths: []
test_paths: []
---

# FEAT-REC-001 — Detect drift and reconcile only authorized targets

## User story / outcome
**Actor:** System / Operator. **Goal:** Detect drift and reconcile only authorized targets. **Primary owner:** `MOD-REC`. **Phase:** Phase 2.

## Pre-conditions
Target ownership mode and expected immutable state resolved.

## Main flow
Read observed snapshot timestamp → compare digest/config/health → classify InSync/Drift/Unknown → if allowed request serialized reconcile → audit.

## Alternatives / exceptional flow
GitOps-managed target: alert or open Git change, no direct kubectl override.

## Messages and integration contracts
`target.observed.v1; drift.detected.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Auto-reconcile observe-only target, act on stale status, two reconcilers race.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
No controller fights; staleness and unknown observable; actions guarded by owner mode.
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
