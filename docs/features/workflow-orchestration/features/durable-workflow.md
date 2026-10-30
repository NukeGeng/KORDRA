---
id: FEAT-WFL-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
module_id: MOD-WFL
feature_id: FEAT-WFL-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-WFL-001 — Durable deployment workflow through failures

## Versioned durable workflow slice — proposed 2026-10-20

Luna T12 WFL task follows durable transport, frozen approval and the cross-process worker authority decision. WFL owns instance/step/timer/target lease and recovery, DEP owns deployment outcome. First workflow is an immutable reviewed template version with typed allowed steps, not arbitrary shell or a visual editor. Bind each instance to exact template/spec/operation/tenant/target. Independent ControlWorker hosting must keep execution progressing when the Mac client/API is closed where the approved topology permits; a worker on the same sleeping Mac cannot pass this criterion.

Adopt [release-execution v1](../../../contracts/release-execution-v1.md), the concrete persisted transition/lease protocol, worker identity/current authorization and retry/timeout limits before implementation. Persist dispatch before delivery; delayed authorization/approval is revalidated. Expired lease with possibly active provider work becomes Unknown, not permission for a competing worker. Recovery reconciles journal/target evidence and never replays an irreversible step from a UI retry.

Acceptance WFL-D1: restart at every transition/inbox/outbox boundary produces one logical operation and consistent history. WFL-D2: duplicate/stale epoch/result, expired/revoked approval, wrong tenant/target and unsupported workflow version reject. WFL-D3: unknown execution remains Unknown until verified evidence; writer handoff cannot overlap unfenced provider work. WFL-D4: real independent-host restart/Mac-close evidence, finite deadlines, crash-safe timer and cancellation behavior are recorded. No process/container is launched by this proposed task description.


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


## T12 bounded design — 2026-10-30

[Thiết kế T12 tiếng Việt](../../../engineering/t12-design.vi.md) và [ADR-0019](../../../decisions/0019-durable-transport-workflow-bootstrap.md) đề xuất compatibility/fault PoC trước runtime adoption. T12.0 chỉ kiểm chứng MSG bằng test-only PostgreSQL fixture; không hoàn tất WFL, distributed authorization hoặc Mac-close. Source/test mappings vẫn chưa implemented. Chưa có package, process, permission hoặc technology adoption nào được cấp quyền bởi ghi chú này.
