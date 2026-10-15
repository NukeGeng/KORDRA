---
id: MOD-APR
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-APR
---

# Approval & Policy Governance — module definition

**Module ID:** `MOD-APR` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Approval & Policy Governance owns: **ApprovalRequest, ApprovalDecision, PolicySnapshot, FreezeWindow**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
RequestApproval, ApproveRelease, RejectRelease.

## Outgoing facts / events
ApprovalRequested, ApprovalGranted, ApprovalExpired.

## Authorized capabilities
deployment.approve, deployment.override. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
release-management; identity-access; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Approval tied to spec hash+target+policy version+TTL; requester cannot self-approve when policy requires separation.

## Failure / risk scenarios
Permission revoked after approval, approval expiry, approver conflict-of-interest. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-APR-001 — Approve production deployment with separation of duties](features/approve-deployment.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **immutable approval tests**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
