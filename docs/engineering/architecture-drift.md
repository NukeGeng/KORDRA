---
id: ENG-DRIFT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Architecture drift register and resolution protocol

Dùng khi source, hạ tầng và các ADR/spec được duyệt không còn trùng nhau. Không hợp thức hóa implementation lệch chỉ bằng sửa tài liệu.

## Registry
| Drift ID | Baseline + ADR | Actual evidence (commit/path/behavior) | Impacted feature | Severity | Decision owner | Action | State |
|---|---|---|---|---|---|---|---|
| DRIFT-000 | Sample only | Replace with actual verified evidence | — | low | unassigned | remove sample | example |
| DRIFT-AMR-001 | v2.1 request expects an existing AMR; model defaults must be verified | Initial directory inventory: docs/tooling had only doccheck/changeguard/scopecheck; no AMR/.codex/.git/product code. Native config/read returned user Sol High, ignoring project defaults in this folder | FEAT-AMR-001 / ENG-TOOLING | medium | engineering-maintainers | Extend existing tooling; explicit native model/effort arguments and live probes; user must establish trusted checkout/provider identity | observed; product architecture unchanged |

## Workflow
1. Phát hiện từ PR review, AI review, test, incident hoặc ops. Bắt buộc ghi **expected vs actual** và bằng chứng định danh được.
2. Xác định severity: tenant leak/auth bypass/unsafe deploy → critical, dừng rollout; trường hợp khác theo blast radius.
3. Phân loại `code bug`, `docs stale`, `architectural change` hoặc `unclear`; mọi type phải được owner xác nhận.
4. Nếu muốn đổi baseline: ADR mới supersede ADR cũ; cập nhật feature/contracts/migrations/tests/runbooks. Nếu code sai: sửa source + regression test.
5. Đóng drift sau khi accepted docs và code được đối chiếu bởi reviewer, có test evidence.

## KPIs
Số drift đang mở, số ngày đến lúc phân loại, số drift tái phát, tỉ lệ PR source changes không có feature mapping. Không đo bằng lượng tài liệu viết ra.
