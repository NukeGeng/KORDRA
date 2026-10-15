---
id: FEAT-CI-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-CI
feature_id: FEAT-CI-001
phase: Phase 2
source_paths: []
test_paths: []
---

# FEAT-CI-001 — Ingest signed CI webhooks and discover artifact

## User story / outcome
**Actor:** CI Provider / Developer. **Goal:** Ingest signed CI webhooks and discover artifact. **Primary owner:** `MOD-CI`. **Phase:** Phase 2.

## Pre-conditions
Provider connection configured with secure signature secret ref.

## Main flow
Verify request signature/timestamp → dedup provider ID → map repo/project → discover artifact digest → publish event.

## Alternatives / exceptional flow
Provider unavailable retries discovery; do not accept mutable tag as final release identity.

## Messages and integration contracts
`artifact.discovered.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Forged webhook, replay of old event, wrong repo tenant mapping, duplicate completion.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Only verified webhook event results in scoped artifact ingestion once.
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
