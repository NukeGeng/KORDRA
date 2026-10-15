---
id: FEAT-OBS-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-OBS
feature_id: FEAT-OBS-001
phase: MVP core / Phase 2 full
source_paths: []
test_paths: []
---

# FEAT-OBS-001 — Verify deployment health and publish scoped status

## User story / outcome
**Actor:** System / Operator. **Goal:** Verify deployment health and publish scoped status. **Primary owner:** `MOD-OBS`. **Phase:** MVP core / Phase 2 full.

## Pre-conditions
Health policy set per application and target capability.

## Main flow
Collect rollout status → readiness checks → optional HTTP/smoke checks → publish bounded result and observability projection.

## Alternatives / exceptional flow
Probe timeout produces Unknown/Failed per policy with evidence; do not fabricate healthy state.

## Messages and integration contracts
`target.observed.v1; deployment.verified.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Cross-org websocket subscription, raw credentials in logs, stale observation counted as success.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Status includes freshness/correlation; tenant-scoped dashboard shows only allowed resources.
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
