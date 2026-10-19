---
id: MOD-IAM
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
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

## Local-account design preference — 2026-10-18

The owner chose Kordra-managed local accounts. [FEAT-IAM-001/I0](features/authorize-resource.md) records proposal-only principal/session and scoped-authorization readiness, alternatives and unresolved contracts. The documentation task passed native High execution/review and existing gates; there is no IAM source/test implementation. Authentication enrollment/login/recovery requires its own approved feature contract. The preference does not accept ADR-0003, credential/session storage or implicit platform-to-tenant access.

## Current synthetic policy evidence — 2026-10-18

[FEAT-IAM-001/I1](features/authorize-resource.md#i1--synthetic-policy-proof-authorized-test-only-candidate) now maps one test-only file in the existing bootstrap test assembly: 66 candidate permit/deny cases passed locally, with native Sol 6.1 High execution/design/post-review and contributor triage. The earlier empty mappings/no-tests statements describe I0 history. There is still no IAM product project, authentication/session provider or production authorizer; module status and owner remain proposed/unassigned. Real login/enrollment/recovery and trusted runtime authority contracts require their own approved scope.

## Narrow policy decision accepted — 2026-10-18

The maintainer has accepted [ADR-0003](../../decisions/0003-tenant-authorization.md)'s permission/trusted-resource-scope baseline. Earlier proposal-only statements are dated history. This does not accept this module, assign its runtime owner, approve authentication/storage or adopt the fixture as product authorization. FEAT-IAM-001 next prepares concrete owner-contract proposals; source and test mappings are unchanged.
