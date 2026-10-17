---
id: ENG-BOUNDARIES
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Module boundary enforcement and dependency rules

## Existing conceptual constraints
- `domain` không import từ `infrastructure/adapters`; `application` dùng ports, adapter implement ports.
- Cross-module write chỉ qua app/service contract hoặc command; không gọi trực tiếp `otherModule.DbContext` / Marten aggregate internals.
- Public contracts có namespace/package riêng và version policy; internal classes không được coi là public by accident.
- Không cho messaging handlers được tái sử dụng như query chứa quyền admin không scoped.
- `Platform` grants và `Tenant` permission store có thể dùng chung DB ban đầu nhưng logic policy phải tách.
- Không xây GenericRepository cho mọi domain để né ownership/aggregate invariants.

## Review gate cho dependency mới
Ghi producer, consumer, contracts, expected failure, retry semantics, access control và lý do dependency; nếu có cycle phải thiết kế event or orchestrated process thay vì hack service locator.

## Proposed source mapping
The detailed target graph and assembly constraints live in [dependency rules](../architecture/dependency-rules.md) and [project conventions](project-conventions.md). They remain proposed under [ADR-0009](../decisions/0009-source-architecture.md); this page does not authorize moving existing projects. Current verified paths must be read from the owning feature's `source_paths` and the checked-out solution.
