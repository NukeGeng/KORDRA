---
id: MOD-NOT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-NOT
---

# Notifications — module definition

**Module ID:** `MOD-NOT` · **Phase:** Phase 2 · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Notifications owns: **NotificationRule, DeliveryAttempt, Subscription**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
SendNotification, RetryNotification.

## Outgoing facts / events
NotificationSent, NotificationFailed.

## Authorized capabilities
project.read, org.manage. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
messaging; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Notification failure never silently changes core deployment status; no secrets in delivery.

## Failure / risk scenarios
Provider outage, notification storm, unsubscribed recipient gets message. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-NOT-001 — Notify authorized subscribers of release events](features/deployment-notifications.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
