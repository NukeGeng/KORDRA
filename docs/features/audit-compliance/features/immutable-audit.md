---
id: FEAT-AUD-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-AUD
feature_id: FEAT-AUD-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-AUD-001 — Tamper-evident privileged action audit trail

## User story / outcome
**Actor:** System / Authorized Auditor. **Goal:** Tamper-evident privileged action audit trail. **Primary owner:** `MOD-AUD`. **Phase:** MVP.

## Pre-conditions
Authenticated source + authorized audit query scope.

## Main flow
Receive security/deploy fact → append with correlation + optional integrity chain → index read view → retain per policy.

## Alternatives / exceptional flow
Audit write unavailable on critical privileged action: fail closed or durable outbox per agreed risk policy.

## Messages and integration contracts
`approval.granted.v1; deployment.verified.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), an audit result can satisfy a required protected-use/disclosure stage only when AUD confirms a durable record bound to that exact intent, decision, stage and authorized scope. A health probe, in-memory enqueue or `Available` flag is not a durable receipt. Proposed append outcomes are `durable`, `rejected`, `unavailable` or `unknown`; only a verified `durable` result supplies a record reference. If policy requires durable audit, rejected, unavailable or unknown outcome blocks that stage. Public denial remains opaque when denial audit also fails. This section is the canonical owner of append-result and durable-receipt semantics; IAM and enforcement consumers link here rather than redefine them.

MOD-AUD is the logical owner; the human audit custodian is unassigned. Durability, idempotency, acknowledgement-loss lookup, outage behavior, retention and crash reconciliation remain decisions; no outbox, transaction, schema or runtime behavior is adopted here.

## Negative & recovery scenarios
Tenant A exports B records; erase production approval evidence; include secret in audit.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Search deployment timeline by correlation and show principal/scope/outcome reliably.
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
