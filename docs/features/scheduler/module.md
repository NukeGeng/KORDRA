---
id: MOD-SCH
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-SCH
---

# Scheduler & Change Windows — module definition

**Module ID:** `MOD-SCH` · **Phase:** Phase 2 · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Scheduler & Change Windows owns: **Schedule, ScheduleRun, MaintenanceWindow, TimezonePolicy**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
CreateSchedule, FireSchedule, CancelSchedule.

## Outgoing facts / events
ScheduleTriggered, ScheduleSkipped.

## Authorized capabilities
deployment.request, deployment.approve. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
workflow-orchestration; approval-governance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Schedule trigger does not bypass runtime policy; timezone/DST explicit.

## Failure / risk scenarios
Missed scheduler tick, duplicate cron fire, approval expired before run. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-SCH-001 — Run scheduled deployments without bypassing policy](features/scheduled-deployment.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
