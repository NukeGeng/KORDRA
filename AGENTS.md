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

## AI engineering and review — AMR v4 Architect / Developer / Reviewer
- Follow the existing [goal workflow](docs/engineering/ai-orchestration/goal-execution.md), [FEAT-AMR-001](docs/engineering/ai-orchestration/features/quality-first-amr.md) and [AMR guide](docs/engineering/ai-orchestration/README.md). Keep single-agent orchestration; never spawn or delegate to collaboration sub-agents.
- Sol High (`gpt-6.1-sol/high`) is the bounded Architect: design milestones, features, module boundaries, contracts, rules, acceptance criteria and critical failure scenarios, then return for exact-content human design approval. Architect sessions must not perform GitHub PR side effects: do not post/edit PR comments, invoke `@codex review` / `@codex security review`, or start/manage BIR rounds. A review-trigger comment is a PR action even when Codex is the reviewer. Hand the design back to Luna or the human maintainer; Sol does not own continuous GOAL execution, routine tests or default PR review.
- Luna Max (`gpt-6-luna/max`) is the Developer and GOAL coordinator for approved design, implementation, tests, ordinary fixes and docs. Deterministic CLI/CI performs build, tests, formatting and validation. Critical risk signals do not by themselves reroute implementation to Sol; when a new design decision is needed, stop and create a bounded Architect task. Never silently change accepted architecture.
- Sol Medium/High is an escalation debugger only after at least two evidenced failed Luna repairs. Require a defect ID, summary, reproducible evidence and affected paths; Medium handles difficult non-critical defects, High handles critical security, concurrency, tenant isolation, irreversible effects, state integrity and distributed recovery. Keep the fix scoped to the defect and its contracts; debugger sessions also must not post PR comments or initiate/manage BIR review triggers. Four failed repairs block for maintainer investigation.
- Codex Auto PR Review is the default independent PR reviewer after applicable CI. Only the Luna Developer/GOAL coordinator or the human maintainer may initiate a manual PR review request or manage BIR rounds. Run one review by default, at most three automated rounds total per PR. Round 1 is the full diff; rounds 2–3 start at the last verified reviewed SHA and include surrounding context. AMR v4 BIR receipts use schemaVersion 2 and require each round's declared trigger, allowed initiator and evidence; manual Luna requests include session evidence and maintainer requests include authorization evidence. The validator rejects missing or disallowed declarations but cannot authenticate them. Track run IDs, SHAs, canonical findings and escalation. Do not review an unchanged SHA/source tree, repost prior bugs, or merge with blocking findings. Round exhaustion never approves and never triggers Sol automatically.
- Native hard-cap support is unverified. BIR is manually enforced with a deterministic ledger validator; repository instructions alone cannot stop provider triggers. Do not add a review-trigger controller or change provider settings in this migration. Keep PR review evidence separate from design approval, deterministic gates, debugger work and human merge authority.
- `goal validate`, `goal accept` and milestone transitions do not invoke AI reviewers under v4. Preserve source-validated context, private atomic checkpoints, execution receipts and interrupted-work reconciliation without automatic replay. New dispatches use policy v4; completed v3/v3.1 checkpoints retain their historical obligations. Unfinished historical tasks may migrate only their review policy while preserving original execution evidence. A local pause/resume never proves native desktop GOAL control.
- Approved local Developer work runs in the current chat by default: `goal step --context <private-json>` prepares scope without an AI dispatch; `goal finish` records actual turn-context, changed artifacts, current applicable gates and contributor triage as coordinator evidence. Use `--native-runner` only for an explicitly chosen serial native execution. Do not create another session merely to obtain a receipt.
- For a stopped, reconciled failed single-task v4 local Developer attempt with inspected repository-only effects, use `goal continue` with the existing approval basis and current evidence; do not request repeat business/design approval just to record progress. Keep the original failure unchanged. Running/unknown operations, external effects, new decisions and Architect/debugger tasks are excluded; historical explicit `goal recover` remains available.
- Reuse successful deterministic evidence only through explicit `gates --reuse`, unchanged relevant input/environment/toolchain fingerprints and its original one-hour freshness window. Product-test reuse also needs the same nonsecret fixture epoch (`--validation-context`); rotate it after fixture/service changes or unknown effects. Docs/scope/mapping always rerun. Approval, CI/PR review and merge gates are never replaced by cached local evidence.
- Verify exact model/effort against the native catalog and invocation evidence. Caller-reported desktop selections remain unverified without session/turn-bound native evidence. Missing exact selection blocks; never silently substitute another model or effort.
- Run `python3 docs/tooling/amr.py gates`; on a Git PR also supply `--base <verified-base-SHA>`. Failed/missing applicable checks and blocking findings prevent completion. Preserve human approval for sensitive production operations, and all LAB-01 restrictions. Neither Auto-review nor PR Code Review grants merge, company or production authority.

### Code review focus
Prioritize actionable tenant/permission leaks; duplicate side effects after retries/ACK loss; breaking contracts without consumer migration; mutable approved spec/artifact at dispatch; secret disclosure; and source/docs ownership drift. Validate findings against the current content/revision, rerun reproductions after fixes and document evidence for dismissals. Mechanical checks belong in CI.

## Source & solution architecture
- [ADR-0013](docs/decisions/0013-clients-git-submodule.md) is accepted only for the owner's explicit Clients repository split. `clients/` is an independent Git submodule, not Product-owned frontend source. Product owns `.gitmodules` and deliberate integration-pin PRs; its canonical checks must not recursively inspect or test frontend work. The independent frontend session opens `KORDRA/clients`, follows its own AGENTS and never writes Product checkpoints/source/services. Neither session resets or recursively updates the other working tree. See [the operating guide](docs/engineering/clients-development.md). This setup does not resume Product GOAL, migrate Website or approve UI/runtime technology.
- Before creating or moving source, read the proposed [repository structure](docs/architecture/repository-structure.md), [solution architecture](docs/architecture/solution-architecture.md), [module structure](docs/architecture/module-internal-structure.md), [dependency rules](docs/architecture/dependency-rules.md), [host composition](docs/architecture/host-composition.md) and [project conventions](docs/engineering/project-conventions.md).
- Those v1.2 documents and [ADR-0009](docs/decisions/0009-source-architecture.md) remain proposed. FullStackHero is a structural reference only, never a required dependency. Do not refactor existing source to match a proposed tree or add speculative projects/packages before explicit ADR acceptance.
- Keep the existing AMR implementation in its current owner paths; do not create a second tool under `tools/amr`. Agent must remain isolated from Control Plane persistence and domain if/when that host is approved.
- Map every new source/test path to its owning feature. Architecture-test rules are design guidance until executable tests and CI evidence exist.

## OSS product phase and optional AI tooling
- Distinguish private Operator Pilot, current implementation evidence, proposed OSS Beta capability, production-readiness, Enterprise and Cloud. Green documentation CI does not mean a deployable product.
- See `docs/product/oss-beta-acceptance.md`, `docs/product/positioning.md` and the **43-group A–F reference** at `docs/product/technology-landscape.md` (all proposed). Do not implement speculative Enterprise billing/licensing or infra provider integrations merely because they are listed in vision.
- `docs/tooling/amr.py` supports coding workflow, not Kordra production runtime. Product CI/tests/security gates must remain independently executable without model provider credentials.
- For Mac-first operation requirements, review `docs/product/operator-workstation.md` and `docs/product/operator-pilot-plan.md`; **read-only first**, separate operations queue from business broker, and require independently hosted worker for Mac-close survival. No authorization is conferred by a document.
- Do not publish real LAB-01 company infrastructure details, credentials, queue identities or operational weaknesses without explicit rights-owner approval. License choices and OSS publication approvals are human decisions.
- Reconcile GitHub PR and CI status to exact SHA before updating roadmap, traceability or closing debt.

## Rule hierarchy
Security invariants > Accepted ADR & contracts > Module/feature specs > Existing code observations > Proposed design. Nếu xung đột giữa accepted docs và code, **không âm thầm chọn một phía**: mở architecture drift record và đối chiếu bằng chứng.
