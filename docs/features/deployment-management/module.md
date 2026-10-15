---
id: MOD-DEP
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-DEP
---

# Deployment Management — module definition

**Module ID:** `MOD-DEP` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Deployment Management owns: **Deployment, RollbackRequest, DeploymentOutcome, DeploymentAttempt**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
StartDeployment, RequestRollback, VerifyDeployment.

## Outgoing facts / events
DeploymentStarted, DeploymentVerified, DeploymentFailed.

## Authorized capabilities
deployment.request, deployment.execute, deployment.rollback. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
workflow-orchestration; deployment-drivers; observability. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Success means observed verified health, not only worker ACK; rollback is new authorized workflow.

## Failure / risk scenarios
Partial update, bad DB migration, health unknown, rollback conflict. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-DEP-001 — Deploy and rollback with verified outcome](features/deploy-rollback.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **deploy approval, rollback, concurrency**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
