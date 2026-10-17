# AGENTS.md — Mandatory operating rules for humans & AI agents

**Áp dụng cho toàn bộ repository.** Mục tiêu: chống tài liệu lỗi thời, lệch kiến trúc, nợ kỹ thuật vô hình, permission hardcode và thiếu traceability. Đọc theo đúng thứ tự:

1. `GOAL.md` → `docs/README.md` → `docs/governance/source-of-truth.md` → `WORKFLOW.md`.
2. `docs/architecture/bounded-contexts.md`, `docs/architecture/security-multitenancy.md`, `docs/architecture/messaging.md`.
3. `docs/features/<owner-module>/module.md` và **feature spec đang xử lý**.
4. Contracts liên quan, ADR đã accepted, test plan, runbook khi vận hành.

## Reference-environment rule (LAB-01)
- Trước mọi thao tác trên hạ tầng tham chiếu đọc `docs/reference-environments/README.md` và `docs/reference-environments/lab-01/experiments/safety-and-change-control.md`.
- Mặc định **observe-only** cho cluster đang chạy ứng dụng thật. Không được tạo/sửa/xóa resource, consume/ACK/purge business queues, publish device MQTT, thay backup flag, Talos reboot, CNPG failover hoặc làm chaos trên shared cluster.
- Không coi reported inventory là verified. Mọi thay đổi trạng thái trong sandbox cần target identity, scope, approval, immutable spec, rollback plan, case ID, evidence.
- Không đưa địa chỉ nội bộ, kubeconfig, token, serial thiết bị, dữ liệu người dùng hoặc payload giải mã vào repo/evidence công khai.
- Lab-specific names, 13 queue nghiệp vụ, MQTT payload và topology hiện tại **không được hard-code** vào generic module/driver.

## MUST
- Trước khi sửa code: xác định **feature_id, module owner, use case, tenant scope, actor, permission, source paths, tests**. Nếu feature chưa có, tạo từ `docs/templates/feature.md` trước.
- Không suy luận tài liệu `proposed` hoặc code chưa kiểm tra là đã triển khai. Ghi rõ `Proposed / Implemented / Verified` và bằng chứng commit/test.
- Không thay đổi module boundary, data ownership, messaging semantics, technology, state machine hay permission model khi chưa có ADR được duyệt.
- Không hard-code role names (`if role == "Admin"`, `SuperAdmin`) để authorize. Dùng permission + resource scope + policy; deny-by-default.
- Tenant context lấy từ danh tính/authorization đáng tin cậy; không tin `tenantId` trong body/message. Platform scope và tenant scope **phải tách biệt**.
- Không cho một agent/driver tự vượt approval, target lease, policy hoặc artifact hash đã khóa.
- Mọi message side-effect phải xét: idempotency, order, timeout, retries, duplicated delivery, outbox/inbox, DLQ, audit, schema evolution.
- Sửa code phải cập nhật **feature spec + contract + decision/ADR (nếu có) + tests + traceability + change log** trong cùng PR; không đóng task chỉ vì build xanh.
- Không xóa/chỉnh test để né lỗi. Không thêm dependency hoặc kiến trúc mới khi chưa có lý do, so sánh phương án và ADR.
- Không lưu mật khẩu/token/kubeconfig/secret hoặc dữ liệu khách hàng trong docs, sample, logs.
- Trước kết thúc: chạy `python3 docs/tooling/doccheck.py` và kiểm thử liên quan; ghi kết quả thực, không giả mạo.

## Change discipline
- Sửa đúng module sở hữu. Thay đổi contract công khai phải tìm consumer, nâng `schemaVersion`, lên kế hoạch tương thích/migration.
- Mọi feature cần negative tests: wrong tenant, missing permission, expired approval, duplicate message, worker crash, unknown agent outcome.
- Có lỗi production: cập nhật incident và ghi technical debt / ADR / runbook nếu cần; không chỉ sửa code.
- Không biết source hoặc yêu cầu còn mâu thuẫn: ghi `[NEEDS-DECISION]` ở file đúng owner, báo rõ rủi ro, **không tự bịa baseline**.

## Handoff / completion report
Báo cáo theo mẫu: `Feature ID | Module | Files changed | Behavior | Contracts | Authorization scope | Tests & results | Docs updated | Debt added/closed | ADR | Remaining risks`.

## AI engineering and review
- Goal runs use the existing [goal execution workflow](docs/engineering/ai-orchestration/goal-execution.md) in single-agent mode. Do not spawn or delegate to native collaboration sub-agents for Kordra tasks. Keep dependent work in the active coordinator session; let AMR recommend a model change only at a safe checkpoint, then wait for the user to switch model and explicitly resume.
- Continue the next eligible milestone task only after current gates, risk-based independent review and finding triage. Persist local execution state, reconcile interrupted work without automatic replay, and update the existing roadmap/feature evidence. A local checkpoint never proves the desktop GOAL was paused or resumed; use the native runtime when it is available and report when it is not.
- “Sol” always means GPT-6.1 Sol (`gpt-6.1-sol`): ENGINEERING uses medium reasoning, CRITICAL uses high. Never silently substitute GPT-6 Sol (`gpt-6-sol`) or another model. If a required model/effort is unavailable, block and report it. An optional switch may be omitted only when the already selected, runtime-advertised Kordra profile meets the task's quality floor; record the reason and mark unobserved desktop selection as unverified.
- Follow [FEAT-AMR-001](docs/engineering/ai-orchestration/features/quality-first-amr.md) and the [AMR guide](docs/engineering/ai-orchestration/README.md). Default substantive source work to ENGINEERING; security, tenant/platform scope, state integrity and production risk to CRITICAL. Verify native model/effort selection; configuration alone is not execution evidence.
- Preserve structured, source-validated context across real model/session switches. The local checkpoint records manually reported active model/effort as unverified unless native runtime evidence binds it to a turn. Never claim a switch succeeded from a user-supplied model name or configuration alone. Run `python3 docs/tooling/amr.py gates`; on a Git PR also supply `--base <verified-base-SHA>`. Failed/missing applicable checks and blocking findings prevent completion. CRITICAL requires independent review; self-check does not count.
- Auto-review decides eligible sandbox requests only. Native Code Review is advisory. Neither is human production approval. Record unsupported/unconfigured capabilities honestly and retain all existing LAB-01 restrictions.

### Code review focus
Prioritize actionable tenant/permission leaks; duplicate side effects after retries/ACK loss; breaking contracts without consumer migration; mutable approved spec/artifact at dispatch; secret disclosure; and source/docs ownership drift. Validate findings against the current content/revision, rerun reproductions after fixes and document evidence for dismissals. Mechanical checks belong in CI.

## Source & solution architecture
- Before creating or moving source, read the proposed [repository structure](docs/architecture/repository-structure.md), [solution architecture](docs/architecture/solution-architecture.md), [module structure](docs/architecture/module-internal-structure.md), [dependency rules](docs/architecture/dependency-rules.md), [host composition](docs/architecture/host-composition.md) and [project conventions](docs/engineering/project-conventions.md).
- Those v1.2 documents and [ADR-0009](docs/decisions/0009-source-architecture.md) remain proposed. FullStackHero is a structural reference only, never a required dependency. Do not refactor existing source to match a proposed tree or add speculative projects/packages before explicit ADR acceptance.
- Keep the existing AMR implementation in its current owner paths; do not create a second tool under `tools/amr`. Agent must remain isolated from Control Plane persistence and domain if/when that host is approved.
- Map every new source/test path to its owning feature. Architecture-test rules are design guidance until executable tests and CI evidence exist.

## Rule hierarchy
Security invariants > Accepted ADR & contracts > Module/feature specs > Existing code observations > Proposed design. Nếu xung đột giữa accepted docs và code, **không âm thầm chọn một phía**: mở architecture drift record và đối chiếu bằng chứng.
