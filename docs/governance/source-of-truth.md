---
id: GOV-SOURCE
status: proposed
owner: unassigned
last_reviewed: 2026-10-19
---

# Nguồn chân lý và chống tài liệu lạc hậu

## Ownership map
- `clients/` is a gitlink into [KORDRA-Clients](https://github.com/NukeGeng/KORDRA-Clients) under accepted [ADR-0012](../decisions/0012-clients-git-submodule.md). Clients owns frontend implementation/docs/tests; Product retains canonical business/API contracts and the integration pin. The [operating guide](../engineering/clients-development.md) owns cross-repository procedure. The separate Website is not migrated. This narrow Git split does not accept the proposed source/solution baseline.
- `GOAL.md`: product vision; `docs/product/scope.md` owns Beta scope; `docs/product/technology-landscape.md` catalogs A–F candidates (not accepted dependencies); `AGENTS.md`/`WORKFLOW.md` govern development.
- `docs/product/operator-workstation.md` owns the proposed OP-01–OP-10 requirements; `operator-pilot-plan.md` owns the private OP-P0–OP-P4 sequencing overlay. They do not replace module/feature ownership, accepted contracts or operational approval. Product/licensing/architecture decisions remain proposed unless explicitly accepted by their authorized owner.
- `docs/architecture/`: các invariant và liên kết giữa modules; không chứa chi tiết button/screen. Source/solution specs v1.2 đang `proposed` và chưa là baseline.
- `docs/decisions/`: quyết định kiến trúc, immutable history (supersede thay vì xóa); `proposed` ADR không cấp quyền thay đổi baseline.
- `docs/features/<module>/module.md`: module owner, capability, inbound/outbound dependencies.
- `docs/features/<module>/features/<feature>.md`: hành vi, actor, permission, flow, acceptance của từng feature.
- `docs/contracts/`: giao diện công khai và message schema liên module.
- `docs/operations/`: runbooks thực thi/khôi phục; [local PostgreSQL setup](../operations/local-postgresql-setup.md) records this machine’s local-only database and Keychain references, not product persistence.
- `docs/engineering/technical-debt.md`: debt register, không ghi TODO rải rác làm nguồn duy nhất.
- `docs/engineering/project-conventions.md` and `architecture-test-rules.md`: proposed code conventions and architecture-test design; actual source/CI behavior remains established by implementation and test evidence.

## Metadata bắt buộc
YAML frontmatter: `id`, `status`, `owner`, `last_reviewed`; module còn có `module_id`; feature có `feature_id`, `module_id`, `phase`, `source_paths`, `test_paths` (đường dẫn chưa có có thể là `[]`).

`status`: `proposed` (đề xuất), `accepted` (baseline được duyệt), `implemented` (code tồn tại có commit), `verified` (test/production evidence được xác nhận), `deprecated` (còn để tra cứu), `superseded` (thay bởi tài liệu khác). Không tự nhảy lên `implemented` khi code chưa kiểm chứng.

## Review freshness
- Mọi thay đổi code/contract/permission phải cập nhật owner docs cùng PR.
- Sau incident, update runbook/debt liên quan và `last_reviewed` khi kiểm tra thật.
- Định kỳ review theo độ rủi ro: security/deployment/messaging 30 ngày; feature khác 90 ngày (ngày là **policy đề xuất**).
- Khi code khác tài liệu: tạo drift issue; ghi evidence, freeze sửa chéo nếu có nguy cơ bảo mật, chọn giải pháp qua review; không overwrite accepted spec bằng suy đoán.

## Chống duplicate
Một business rule chỉ có **một trang sở hữu**. Trang khác chỉ liên kết. Nếu chuyển module, để redirect/relocation note ngắn và sửa link nội bộ; không tạo hai bản mô tả song song.

Các thay đổi đã phát hành ghi ở [change history](../engineering/change-history.md); code/docs divergence quản lý ở [architecture drift](../engineering/architecture-drift.md).
