---
id: MOD-IAM
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-IAM
---

# Identity & Access — module definition

**Module ID:** `MOD-IAM` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Identity & Access owns: **Principal, IdentityLink, Session, RoleDefinition, PolicyDecision**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
AuthenticatePrincipal, EvaluateAuthorization, RevokeSession.

## Outgoing facts / events
PrincipalAuthenticated, AccessDenied.

## Authorized capabilities
org.read, org.manage, platform.system.manage. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
tenant-management; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Không quyết định quyền chỉ bằng tên role; deny-by-default; principal token được xác minh.

## Failure / risk scenarios
Token hết hạn, cache permission stale, permission escalation. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-IAM-001 — Authorize scoped resource access](features/authorize-resource.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **cross-tenant test cases**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
