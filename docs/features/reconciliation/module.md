---
id: MOD-REC
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-REC
---

# Desired/Observed Reconciliation — module definition

**Module ID:** `MOD-REC` · **Phase:** Phase 2 · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Desired/Observed Reconciliation owns: **DesiredSnapshot, ObservedSnapshot, DriftRecord, ReconcilePolicy**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
CompareTargetState, RequestReconcile, DismissDrift.

## Outgoing facts / events
DriftDetected, ReconcileRequested.

## Authorized capabilities
target.read, deployment.execute (for approved auto repair). Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
infrastructure-inventory; observability; deployment-management. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Single desired-state owner; external GitOps is observe/report by default.

## Failure / risk scenarios
Stale observation, controller war with Argo CD, auto-remediation storm. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-REC-001 — Detect drift and reconcile only authorized targets](features/drift-detection.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
