---
id: FEAT-IAM-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-IAM
feature_id: FEAT-IAM-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-IAM-001 — Authorize scoped resource access

## User story / outcome
**Actor:** User / ServiceAccount / Agent. **Goal:** Authorize scoped resource access. **Primary owner:** `MOD-IAM`. **Phase:** MVP.

## Pre-conditions
Principal được xác thực; resource tồn tại và có owner rõ ràng.

## Main flow
Resolve verified identity → resolve owned resource → resolve valid grants → evaluate permission+scope+policy context → allow/deny + audit.

## Alternatives / exceptional flow
Grant revoked mid-operation: re-evaluate at execution. Platform role không mặc định được mở dữ liệu tenant.

## Messages and integration contracts
`membership.changed.v1; security.access.denied.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Role name Admin nhưng không có permission; body tenant bị đổi; expired token/grant; tự cấp quyền cao hơn.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Cross-tenant access luôn bị chặn; permission mới được thêm qua data-backed catalog không sửa if-role.
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
