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
| DRIFT-BOOT-001 | Proposed module dependency summaries are not an accepted acyclic source dependency graph | identity-access ↔ tenant-management; artifact-management ↔ ci-integrations; release-management ↔ approval-governance in module docs; zero product projects at baseline d0eff75 | Foundation / IAM, TEN, ART, CI, REL, APR | medium | architecture owner (unassigned) | Use pure module-local I1; resolve contract/task seams and approve cross-module wiring before project references | proposed design ambiguity; no accepted baseline/code conflict asserted |
| DRIFT-SOURCE-001 | v1.2 source architecture package reused ADR-0008, already accepted for the scoped domain/test bootstrap | Root `Kordra.slnx` has one Domain and one test project; target relocation would affect README/CI/AMR; one narrow Domain assembly dependency test runs in product CI, while broader graph/Contracts/host/Agent checks are absent; customer broker inventory ownership is unresolved | Foundation / FEAT-AMR-001, MOD-MSG and future module/host owners | medium | architecture owner (unassigned), engineering-maintainers | Preserve ADR-0008; retain source proposal as ADR-0009; keep current root/source/tooling paths until a complete migration and owner decision; keep MOD-MSG internal and external-broker ownership `[NEEDS-DECISION]` | ADR numbering and documentation findings reconciled after native GPT-6.1 Sol High review; acceptance evidence/owner review and migration remain open; no source move |

## Workflow
1. Phát hiện từ PR review, AI review, test, incident hoặc ops. Bắt buộc ghi **expected vs actual** và bằng chứng định danh được.
2. Xác định severity: tenant leak/auth bypass/unsafe deploy → critical, dừng rollout; trường hợp khác theo blast radius.
3. Phân loại `code bug`, `docs stale`, `architectural change` hoặc `unclear`; mọi type phải được owner xác nhận.
4. Nếu muốn đổi baseline: ADR mới supersede ADR cũ; cập nhật feature/contracts/migrations/tests/runbooks. Nếu code sai: sửa source + regression test.
5. Đóng drift sau khi accepted docs và code được đối chiếu bởi reviewer, có test evidence.

## KPIs
Số drift đang mở, số ngày đến lúc phân loại, số drift tái phát, tỉ lệ PR source changes không có feature mapping. Không đo bằng lượng tài liệu viết ra.
