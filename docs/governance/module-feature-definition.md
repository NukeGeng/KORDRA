---
id: GOV-MODULES
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Chuẩn phân cấp Module → Feature → Use Case → Contract

## Definitions
- **Bounded Context:** ranh giới ngôn ngữ nghiệp vụ và ownership dữ liệu, có thể chứa nhiều module.
- **Module:** tập năng lực có owner rõ, giao tiếp qua contracts; không đồng nghĩa một service/process.
- **Feature:** khả năng người dùng hoặc hệ thống có thể nghiệm thu độc lập, thuộc **đúng một module owner**.
- **Use Case:** tương tác đầu-cuối của actor/system để đạt mục tiêu; có thể phối hợp nhiều module nhưng cần orchestrator owner.
- **Capability:** nhóm chức năng cấp sản phẩm; một capability có thể gồm nhiều features.
- **Driver/Adapter:** adapter của execution layer (Helm/Compose) không là module nghiệp vụ chủ sở hữu release.

## Quy tắc phân bố
1. Cross-cutting policy đặt trong `architecture/` hoặc `contracts/`, feature thực thi rule phải **link** tới đó.
2. Feature docs nằm `features/<module>/features/<feature>.md`; module registry nằm `module.md`.
3. Feature liên quan nhiều module có **1 primary owner**, liệt kê consumers và message/API contract.
4. Không đặt feature business vào `shared`, `common`, `utils`; `shared` chỉ chứa primitive/abstractions có quy tắc import.
5. Module tên theo responsibility/ownership, không tên theo framework (`MartenModule`, `RabbitMqModule`).

## Minimal module contract
`module_id`, purpose, owned entities, inbound command/query, outbound events, allowed dependencies, permission keys, invariants, failure cases, features list, source/test mapping.

## Minimal feature contract
`feature_id`, actors, tenant scope, prerequisites, main/alternative/error flow, state transitions, commands/events/API, auth matrix, NFR, test plan, observability, acceptance criteria, implementation evidence.

## Rule for new module
Chỉ tách module nếu có ownership độc lập và trade-off rõ; thay đổi boundaries → ADR + migration + contract impact review. Xem [bounded contexts](../architecture/bounded-contexts.md).
