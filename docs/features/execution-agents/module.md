---
id: MOD-AGT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-AGT
---

# Agent Management & Gateway — module definition

**Module ID:** `MOD-AGT` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Agent Management & Gateway owns: **AgentIdentity, EnrollmentTicket, AgentSession, OperationJournal, Heartbeat**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
EnrollAgent, RevokeAgent, DispatchToAgent.

## Outgoing facts / events
AgentConnected, AgentOffline, OperationReceiptReceived.

## Authorized capabilities
agent.enroll, agent.revoke. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
connection-secrets; infrastructure-inventory; messaging. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Agent initiates outbound authenticated connection and only executes scoped operations.

## Failure / risk scenarios
Agent offline mid-run, revoked cert, stale lease epoch, duplicate receipt. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-AGT-001 — Enroll, route and recover agents](features/agent-enrollment.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **agent offline/reconcile tests**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
