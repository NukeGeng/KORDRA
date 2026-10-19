---
id: GOV-AI
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Quy trình AI review chống đọc nhầm tài liệu cũ

## Bắt buộc cho coding agents
1. Xác định task cụ thể và file thay đổi (ưu tiên `git diff --name-only`), không đọc tất cả docs rồi giả định đều đúng.
2. Map file/code → module → feature_id → accepted ADR → contracts. Nếu không map được, đánh dấu `UNMAPPED_CHANGE`.
3. Kiểm tra `status`, `last_reviewed`, source/test mapping; dùng `git log` và code thực tế để so sánh. `proposed` chỉ là giả thuyết.
4. Lập danh sách divergence giữa docs và source; ghi bằng chứng đường dẫn/dòng hoặc commit, phân loại `CODE_BUG`, `DOC_STALE`, `ARCH_CHANGE`, `UNKNOWN`.
5. Đọc các API/events/data consumers bị ảnh hưởng, kiểm tra quyền và tenant scope.
6. Update chính xác tài liệu owner, không tạo `notes-v2-final.md` không quản trị.
7. Kết thúc với: `What changed`, `Evidence`, `Tests`, `Docs updated`, `Open decisions`, `Technical debt`.

## Chống hallucination
- Không giả định thư mục source có thật; `source_paths: []` nghĩa là chưa map.
- Không khẳng định đã chạy lệnh/test nếu chưa có output.
- Không coi acceptance criteria là bằng chứng implementation.
- Không tự sửa ADR accepted, security invariant hoặc quyền production mà thiếu approval.

## Review prompt để copy
> Đọc AGENTS.md, GOAL.md, WORKFLOW.md, docs/README.md; xác định owning module/feature, chỉ dùng accepted docs làm baseline; so sánh với source và tests thực tế. Xuất bảng drift, permission/tenant risks, impact contracts, tests thiếu, debt, và đề xuất file docs cần cập nhật. Không implement khi chưa nêu những vấn đề cần quyết định.

## Native runtime and review evidence
The [AMR operating guide](../engineering/ai-orchestration/README.md) implements routing/context/check mechanics for this protocol; [local artifact contract](../engineering/ai-orchestration/contract.md). Proposed documentation stays proposed until reviewed; a successful native invocation does not accept an architecture decision. Separate selected model evidence, native sandbox decisions, local independent review and provider automatic PR/MR review. Revalidate reviews and context after source/contract changes. Findings require reproduction or counterevidence, feature ownership, severity and a bounded repair history. Production approval remains explicitly human. AMR v3.1 uses Luna Max for normal development, Sol High for critical/architecture work and independent milestone review, and Codex Auto Code Review for PRs. Follow the [BIR policy](../engineering/ai-orchestration/bounded-incremental-review.md) for manual request accounting, full/incremental scopes, canonical findings and escalation; three rounds never imply approval.
