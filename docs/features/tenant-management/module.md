---
id: MOD-TEN
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-15
module_id: MOD-TEN
---

# Tenant & Membership — module definition

**Module ID:** `MOD-TEN` · **Phase:** MVP · **Owner:** NukeGeng (accountable for ADR-0012 Foundation T01–T07; wider module scope remains proposed).

## Responsibility and boundary
Tenant & Membership owns: **Organization, Membership, Invitation, TenantStatus**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
CreateOrganization, InviteMember, AssignRole, SuspendOrganization.

## Outgoing facts / events
OrganizationCreated, MembershipChanged, OrganizationSuspended.

## Authorized capabilities
org.manage, membership.invite, membership.assign-role. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
identity-access; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
User có thể thuộc nhiều org; grant theo scope; suspended tenant không được deploy.

## Failure / risk scenarios
Invite replay, role escalation, tenant status race. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-TEN-001 — Organization membership lifecycle](features/membership-lifecycle.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
