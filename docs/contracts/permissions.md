---
id: CTR-PERMISSIONS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Permission catalog, role templates & scope matrix

**Source of truth cho permission keys và scope.** Không authorize bằng tên role trong code. Role là cấu hình tổng hợp các permission. `platform/*` chỉ phục vụ quản trị nền tảng, không cấp implicit tenant resource access.

## Permission keys
| Permission | Scope(s) | Critical action | Notes |
|---|---|---|---|
| `org.read` / `org.manage` | organization | manage membership/settings | tenant boundary |
| `membership.invite` / `membership.assign-role` | organization | role escalation check | grant permission ≤ grantor authority |
| `project.read` / `project.manage` | organization, project | create project | ownership check |
| `target.read` / `target.manage` | project, environment | register target | credential reference only |
| `credential.bind` | target | bind secret ref | never read plaintext |
| `artifact.read` / `artifact.register` | project | pin digest | immutable metadata |
| `release.create` / `release.read` | project | create release | cannot approve by default |
| `deployment.request` | environment | request deploy | policy evaluate |
| `deployment.approve` | environment | approve pinned spec | SoD and TTL |
| `deployment.execute` | environment | dispatch | operator/system delegation |
| `deployment.rollback` | environment | explicit rollback | separate from execute |
| `deployment.override` | environment | emergency override | step-up + reason + audit |
| `audit.read` | organization | sensitive tenant audit | no cross-tenant |
| `agent.enroll` / `agent.revoke` | platform or organization target assignment | cert issuance | strict scope |
| `platform.tenant-directory.read` | platform | see tenant metadata | NO access to tenant payload |
| `platform.support-grant.issue` | platform | issue scoped support grant | requires approval/TTL |
| `platform.system.manage` | platform | manage broker/feature flags | not tenant deployment right |

## Example role templates (not hardcoded)
| Role template | Scope | Example permissions (review required) |
|---|---|---|
| Viewer | organization | read-only project/release status |
| Developer | project | artifact.register, release.create, deployment.request |
| ReleaseApprover | environment | deployment.approve |
| Operator | environment | deployment.execute, deployment.rollback |
| TenantAdmin | organization | org.manage, membership.invite, membership.assign-role; **not auto `deployment.override`** |
| PlatformOperator | platform | platform.tenant-directory.read, agent fleet ops |
| PlatformSuperAdmin | platform | platform.system.manage, platform.support-grant.issue; no implicit org membership |

## Authorization decision algorithm
1. Resolve subject identity + memberships + support grants from trusted store.
2. Resolve resource and its org/project/environment from trusted DB.
3. Reject missing/expired/suspended scope; deny wins; inherited role permission only if scope policy allows.
4. Evaluate `action` permission and contextual condition (freeze, SoD, MFA, approval, policy version).
5. Persist audit for privileged actions/denials as configured.

## Anti-escalation test cases
- TenantAdmin A adds platform.* permission → DENY.
- PlatformSuperAdmin without scoped support grant reads A deployment log → DENY.
- User in A and B switches active org; body tenant A on B target → DENY.
- Requester approves their own production request → DENY per default production policy.
- Role removal/expiry during pending approval → re-evaluate before execute.
- Support grant expires mid-workflow → suspend or require re-approval per policy; never silently continue.

## Migration for new role
Create data-backed role template, permission mapping, UI display and grant policy; backfill no implicit grants; review impact on cached claims, policy version and pending workflows; add deny/allow tests and audit. Production change must include owner signoff.
