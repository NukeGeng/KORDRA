---
id: ARCH-CONTEXTS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Bounded Context ownership map

## Domain boundaries
| Context | Modules | Owns | Depends on |
|---|---|---|---|
| Organization & Access | identity-access, tenant-management, platform-admin | Principal, Membership, RoleBinding, permission decisions | audit |
| Application Catalog | project-environment, infrastructure-inventory, connection-secrets | Application, Environment, Target, capabilities, secret ref | access |
| Supply & Release | artifact-management, release-management, approval-governance | immutable artifact, release intent, approval | catalog, access |
| Deployment Operations | deployment-management, workflow-orchestration, scheduler | deployment run, steps, recovery & lease | release, agent/driver |
| Integration & Execution | execution-agents, deployment-drivers, ci-integrations | agent, driver operation, external integration | deployment |
| Platform & Signals | messaging, reconciliation, observability, audit-compliance, notifications | delivery envelopes, observations, audit, notifications | others as published contracts |

### Rules
- Module dùng IDs/snapshots/contracts; không đọc thẳng bảng dữ liệu owner khác để quyết định business rule.
- Query read model cross-module được dùng cho dashboard, **không làm source of truth khi authorize/deploy**.
- Không có `SharedBusinessService` chứa logic hỗn hợp tenant/approval/deployment.
- SharedKernel chỉ cho primitive `EntityId`, `Clock`, `Result`, `CorrelationId` và interface qua ADR.
- Event subscription side-effect cần đăng ký trong [message catalog](../contracts/message-catalog.md).

### Cross-module orchestration
`release-management` tạo intent; `approval-governance` xác nhận approval; `workflow-orchestration` điều phối; `deployment-management` owns deployment outcome; `execution-agents` quản lý enrollment/connection; `deployment-drivers` thực thi adapter; `reconciliation` quan sát và reconcile nếu controller ownership cho phép.
