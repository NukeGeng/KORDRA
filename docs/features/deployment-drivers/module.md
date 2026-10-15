---
id: MOD-DRV
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-DRV
---

# Deployment Driver Integrations — module definition

**Module ID:** `MOD-DRV` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Deployment Driver Integrations owns: **DriverDescriptor, DriverPlan, CapabilityDescriptor, DriverReceipt**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
PlanDriverOperation, ExecuteDriverOperation, ObserveTarget.

## Outgoing facts / events
DriverOperationFinished, DriverOperationFailed.

## Authorized capabilities
deployment.execute, deployment.rollback. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
execution-agents; infrastructure-inventory; deployment-management. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Driver does not authorize approval; different targets have different semantics; typed plan only.

## Failure / risk scenarios
Helm partial rollout, Compose unhealthy, target provider API different versions. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-DRV-001 — Execute Helm and Docker Compose via capability-based drivers](features/helm-compose-drivers.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **Helm and Docker Compose contract in disposable target**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
