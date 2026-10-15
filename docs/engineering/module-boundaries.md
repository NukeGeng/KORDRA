---
id: ENG-BOUNDARIES
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Module boundary enforcement and dependency rules

## Dependency constraints
- `domain` không import từ `infrastructure/adapters`; `application` dùng ports, adapter implement ports.
- Cross-module write chỉ qua app/service contract hoặc command; không gọi trực tiếp `otherModule.DbContext` / Marten aggregate internals.
- Public contracts có namespace/package riêng và version policy; internal classes không được coi là public by accident.
- Không cho messaging handlers được tái sử dụng như query chứa quyền admin không scoped.
- `Platform` grants và `Tenant` permission store có thể dùng chung DB ban đầu nhưng logic policy phải tách.
- Không xây GenericRepository cho mọi domain để né ownership/aggregate invariants.

## Review gate cho dependency mới
Ghi producer, consumer, contracts, expected failure, retry semantics, access control và lý do dependency; nếu có cycle phải thiết kế event or orchestrated process thay vì hack service locator.

## Source mapping (future repository)
`src/Modules/<Module>/Domain`, `Application`, `Infrastructure`, `Contracts`; API composition và worker host riêng. Đây là **naming suggestion**, không phải source tree đã tồn tại. Chỉ cập nhật source_paths khi xác minh.
