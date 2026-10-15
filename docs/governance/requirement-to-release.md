---
id: GOV-TRACE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Requirement-to-release traceability protocol

## Definition of controlled change
Mọi thay đổi hành vi, state transition, endpoint, message, permission, driver behavior hoặc data rule có `Change-ID`/Issue → `Feature ID` → `Module ID` → `Contract` → `Tests` → `Code Commit` → `Release Version` → `Operations Evidence`.

## Implementation evidence rules
- Ghi real `source_paths`/`test_paths` trong frontmatter sau khi xác nhận git-tracked file.
- Tạo `Evidence` trong feature (commit SHA, CI run link, scenario IDs, observed target). Generated docs mới `proposed` không được gắn false evidence.
- Map tác động cross-module: thay đổi public event/permission tối thiểu producer, consumer và affected end-to-end use case.
- Sau release, production hotfix và rollback phải giữ trace qua `deploymentId`/`operationId` để postmortem không mất ngữ cảnh.

## Automated code-to-doc guard
[`docs/tooling/changeguard.py`](../tooling/changeguard.py) sử dụng Git diff và `source_paths` trong feature files để tìm module owner. Trong PR, nếu sửa `src/` mà không map feature hoặc không cập nhật file spec của feature tương ứng, CI phải từ chối. Khi chưa có source, guard chỉ kiểm tra được các thay đổi hiện diện.

## Exceptions
Generated code không sửa bằng tay phải ghi ownership khác nếu thuộc `src/`; cập nhật allowlist chỉ sau ADR/review. Hotfix phải link incident và tạo follow-up docs trước hạn, không trở thành ngoại lệ vĩnh viễn.
