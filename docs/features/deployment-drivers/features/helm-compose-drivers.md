---
id: FEAT-DRV-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-DRV
feature_id: FEAT-DRV-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-DRV-001 — Execute Helm and Docker Compose via capability-based drivers

## User story / outcome
**Actor:** Deployment Worker / Agent. **Goal:** Execute Helm and Docker Compose via capability-based drivers. **Primary owner:** `MOD-DRV`. **Phase:** MVP.

## Pre-conditions
Target verified; driver capabilities and approved plan match.

## Main flow
Validate capability → generate dry-run plan if supported → execute immutable version → return operation receipt → observe target.

## Alternatives / exceptional flow
Helm rollback by revision if valid; Compose redeploy prior digest/config; Argo CD handled by external ownership.

## Messages and integration contracts
`deployment.dispatch.v1; deployment.finished.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Unsupported rollback; unscoped SSH command; provider API timeout returns Unknown.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Driver conformance suite passes plan/execute/observe, duplication, credential and rollback cases.
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
