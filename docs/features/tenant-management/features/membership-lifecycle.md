---
id: FEAT-TEN-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-TEN
feature_id: FEAT-TEN-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-TEN-001 — Organization membership lifecycle

## User story / outcome
**Actor:** Tenant Admin. **Goal:** Organization membership lifecycle. **Primary owner:** `MOD-TEN`. **Phase:** MVP.

## Pre-conditions
Organization active; actor được cấp permission tương ứng.

## Main flow
Create invitation → verify email/identity → accept once → membership scope → bind role template with grantor check → audit.

## Alternatives / exceptional flow
Invitation expired/revoked; org suspended; user already belongs org; role template updated.

## Messages and integration contracts
`tenant.membership.changed.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), MOD-TEN remains the source of organization status and membership lifecycle facts. Any future owner result must bind the exact subject and tenant lookup to authoritative status, revision, validity and revocation evidence; caller-supplied tenant/principal IDs or fixture flags are not authority. Membership in multiple tenants never merges scope. Missing, stale, suspended, revoked, conflicting or unavailable evidence remains unresolved and denies protected access without disclosing tenant existence.

Ownership of role-assignment persistence/resolution is still open between the relevant owners and MOD-IAM's `RoleDefinition` interpretation. MOD-TEN is the logical fact owner; the human feature custodian is unassigned. This proposal adds no contract, source/test mapping, API or runtime behavior.

## Negative & recovery scenarios
TenantAdmin A mời người vào B; tự cấp platform.*; replay invitation; suspend bypass.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Một user thuộc A+B nhưng dữ liệu vẫn tách; grant không vượt quyền người cấp.
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
