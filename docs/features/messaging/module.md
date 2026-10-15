---
id: MOD-MSG
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-MSG
---

# Messaging Infrastructure — module definition

**Module ID:** `MOD-MSG` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Messaging Infrastructure owns: **MessageEnvelope, RoutingPolicy, InboxReceipt, OutboxRecord, DeadLetterRecord**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
RouteMessage, ReplayDeadLetter, ScheduleMessage.

## Outgoing facts / events
MessageRejected, MessageDeadLettered.

## Authorized capabilities
platform.system.manage (DLQ replay). Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
workflow-orchestration; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Durable commands + inbox/outbox; side effects idempotent; telemetry channel separated.

## Failure / risk scenarios
Poison message, consumer schema unknown, broker unavailable, unordered duplicate. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-MSG-001 — Durable command delivery and recovery](features/durable-delivery.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **VerneMQ and RabbitMQ topology observation; transport isolation**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
