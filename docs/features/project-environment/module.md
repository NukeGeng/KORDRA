---
id: MOD-PRJ
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-PRJ
---

# Project, Application & Environment — module definition

**Module ID:** `MOD-PRJ` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Project, Application & Environment owns: **Project, Application, Environment, EnvironmentClassification**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
CreateProject, DefineEnvironment, BindTarget.

## Outgoing facts / events
EnvironmentCreated, TargetBound.

## Authorized capabilities
project.manage, project.read, deployment.request. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
tenant-management; infrastructure-inventory. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Project/app/environment thuộc đúng một org; policy dựa classification không dựa display name.

## Failure / risk scenarios
Rename prod thành dev để bypass policy, environment mapping tenant mismatch. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-PRJ-001 — Create and govern deployment environments](features/environment-catalog.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
