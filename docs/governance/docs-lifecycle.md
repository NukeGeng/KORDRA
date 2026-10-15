---
id: GOV-LIFECYCLE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Vòng đời tài liệu, review và migration

## Lifecycle
`proposed → accepted → implemented → verified → deprecated/superseded`.
- `accepted` chỉ sau architecture/domain owner review.
- `implemented` đòi bằng chứng paths + commit/PR; giữ `status: accepted` nếu chưa thể chỉ ra.
- `verified` đòi test evidence, audit tại runtime khi áp dụng; mỗi lần nâng status phải ghi link evidence.
- `superseded`: ghi đường dẫn tài liệu thay thế và lý do; lịch sử không được xóa.

## Rename/move docs
(1) Update owning module registry; (2) move file; (3) update in-repo links; (4) keep short redirect note one release nếu link bên ngoài; (5) update traceability; (6) run doccheck.

## Scheduled review policy (proposal)
Critical (tenant/security/messaging/deploy): 30 ngày; Core module: 60 ngày; noncritical: 90 ngày. Không tự động đánh dấu một feature "verified" vì file vừa được sửa.

## Evidence examples
`source_paths: ["src/.../Handler.cs"]`, `test_paths: ["tests/...Tests.cs"]`; attachment PR/commit/test link trong phần Evidence của feature. Paths phải được người quản lý duyệt sau khi repository có source.
