---
id: ARCH-MODULE-STRUCT
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Kordra — Module Internal Structure & Feature Ownership

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

## 1. Module is bounded ownership; feature is delivery unit
Một module sở hữu nghiệp vụ, invariant, persistence và public contracts của chính nó. **Không** tách mỗi endpoint thành một module độc lập. Không bắt buộc tầng Application riêng khi một vertical slice nhỏ là đủ; domain logic không được nằm trong controller.

## 2. Example: Deployment Management
```text
src/Modules/DeploymentManagement/
├── Kordra.Modules.DeploymentManagement/
│   ├── Domain/
│   │   ├── Aggregates/
│   │   ├── ValueObjects/
│   │   ├── Events/
│   │   └── Policies/
│   ├── Features/
│   │   ├── CreateDeployment/
│   │   │   ├── CreateDeploymentCommand.cs
│   │   │   ├── CreateDeploymentHandler.cs
│   │   │   ├── CreateDeploymentValidator.cs
│   │   │   └── CreateDeploymentEndpoint.cs
│   │   ├── ExecuteDeployment/
│   │   ├── ObserveDeployment/
│   │   ├── GetDeploymentStatus/
│   │   └── RequestRollback/
│   ├── Infrastructure/
│   │   ├── Persistence/
│   │   ├── Projections/
│   │   └── Integrations/
│   └── DependencyInjection.cs
└── Kordra.Modules.DeploymentManagement.Contracts/
    ├── Commands/
    ├── Events/
    └── DTOs/
```
**Ví dụ target**, không phải file đã tạo. Tên slice phải bám use case thực tế và feature spec, không chỉ tạo class theo checklist.

## 3. Responsibilities
| Layer/area | Được phép | Không được phép |
|---|---|---|
| `Domain` | aggregate, invariants, value objects, pure policies | `Marten`, `Wolverine`, HTTP, `Helm`, SQL, secret/network I/O |
| `Features/*` | use-case, command/query handler, validation, transaction boundary | gọi trực tiếp persistence của module khác, role hardcode |
| `Infrastructure` | Marten mapping, owned projections, typed external adapters | tái định nghĩa domain permission, tạo bypass approval |
| `.Contracts` | stable public DTO/messages, IDs and versioned schema | internal aggregate/marten model, `IDocumentSession`, arbitrary mutable state |
| `DependencyInjection` | module registration, endpoint/handler registration | biến thành global service locator |

## 4. Domain / CQRS choices
- Command: bắt buộc permission check và enforce invariant; output chỉ trạng thái/domain event đã được persistence xác nhận.
- Query: read models tối ưu read path, nhưng authorization luôn dựa trusted resource/permission store. Không leak resource existence qua cross-tenant query.
- Wolverine handler là transport/application integration, **không tự biến message thành domain invariant**; ghi message catalog.
- Marten aggregate/document/stream do module sở hữu. Inline/async projection cần tính nhất quán được ghi trong spec.
- CQRS tách read/write khi đem lại lợi ích thực; không cần hai database riêng mặc định.

## 5. Feature-to-code contract
Mỗi feature spec tại `docs/features/<owner>/features/<name>.md` cần điền:
- `feature_id`, `module_id`, actor/resource/permission/tenant scope.
- primary/alternative flow, state transitions, relevant commands/events, failure modes.
- `source_paths` chỉ sau khi file thực tồn tại; `test_paths` chỉ sau khi test thực tồn tại.
- implementation evidence: commit SHA + CI run/report; docs là spec, không phải chứng minh code đã chạy.

## 6. Inter-module interactions
- Module A giao tiếp B qua B's public application contract hoặc integration event, không đọc trực tiếp B's persistence/aggregate.
- Nếu giao dịch xuyên nhiều module, dùng process manager/workflow và consistency model đã duyệt; đừng tạo distributed DB transaction giả.
- Dữ liệu dùng chung qua ID/reference/snapshot; mọi event consumer kiểm lại tenant scope, origin, idempotency và schema version.
- Circular dependency cần sửa ở responsibility/contract, không né bằng static singleton/reflection/service locator.

## 7. Example feature lifecycle
`FEAT-DEP-001` → thiết kế request/approve/observe/rollback behaviors → implement feature slices theo ownership → unit/contract/integration tests → PR review → update spec + traceability → accepted evidence.

## 8. Mandatory acceptance
- No external SDK dependency in Domain; no cross-module internal imports; tests cover happy/negative/retry; docs and code mapped; API/Worker composition correctly registers handler once; explicit decision for any exception.

**See also:** [Dependency rules](dependency-rules.md), [Project conventions](../engineering/project-conventions.md), [Source-of-truth](../governance/source-of-truth.md), [Feature registry](../features/README.md).
