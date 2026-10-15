---
id: MOD-REL
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-REL
---

# Release Management — module definition

**Module ID:** `MOD-REL` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Release Management owns: **Release, ReleaseSpec, ReleaseRevision, ReleaseIntent**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
CreateRelease, RequestDeployment, CancelRelease.

## Outgoing facts / events
ReleaseCreated, DeploymentRequested.

## Authorized capabilities
release.create, release.read, deployment.request. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
artifact-management; project-environment; approval-governance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Release spec canonical hash binds artifact + configuration + target + policy revision.

## Failure / risk scenarios
Spec changed after approval, release deleted during workflow, cross-tenant artifact. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-REL-001 — Create immutable release intent](features/create-release.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
