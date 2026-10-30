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

## T09 provider priority change — 2026-10-24

DRIFT-T09-001: accepted ADR-0016 specifies Kubernetes while the owner now reports Nomad as the relevant runtime and requests a new personal lab. This is a requested provider design change, not an existing Product code defect. FEAT-INV-001, owner NukeGeng, medium design risk. Preserve ADR-0016; ADR-0017 and Nomad workloads v1, exactly accepted by NukeGeng on 2026-10-24 for T09.2a–d, capture adoption and identity/TLS/header differences before Product implementation. Lab creation is operationally authorized and Product provider design is accepted; implementation/conformance remain pending. Design disposition is resolved; no runtime-drift closure is claimed. No owner reassignment or accepted-contract rewrite.

## Workflow
1. Phát hiện từ PR review, AI review, test, incident hoặc ops. Bắt buộc ghi **expected vs actual** và bằng chứng định danh được.
2. Xác định severity: tenant leak/auth bypass/unsafe deploy → critical, dừng rollout; trường hợp khác theo blast radius.
3. Phân loại `code bug`, `docs stale`, `architectural change` hoặc `unclear`; mọi type phải được owner xác nhận.
4. Nếu muốn đổi baseline: ADR mới supersede ADR cũ; cập nhật feature/contracts/migrations/tests/runbooks. Nếu code sai: sửa source + regression test.
5. Đóng drift sau khi accepted docs và code được đối chiếu bởi reviewer, có test evidence.

## KPIs
Số drift đang mở, số ngày đến lúc phân loại, số drift tái phát, tỉ lệ PR source changes không có feature mapping. Không đo bằng lượng tài liệu viết ra.

## T11 proposal refinement — 2026-10-27

DRIFT-T11-001, design ambiguity / medium / NukeGeng: the unaccepted 2026-10-20 release-execution sketch included unadopted driver/configuration/health and deployment.approve in T11; actual main bc91c2a has ART manifest/config verification but no REL/APR runtime or worker/execution plan. Calling that partial subject deployment approval would risk accepting later fields without review. Proposed ADR-0005/Release and approval v1 distinguishes exact release-intent-v1 from later separately approved execution, adds APR policy before REL, and defines acyclic neutral owner ports rather than the old proposed REL/APR logical dependency cycle. Awaiting exact owner acceptance. No accepted-contract/code drift is legitimized, no runtime fix/closure or production authority claimed.


## DRIFT-T12-001 — test transport schema escape (2026-10-30)

Owner: NukeGeng / FEAT-MSG-001. Expected: all fixture persistence and queue objects are confined to the newly allocated UUID schema. Observed: the first probes configured only envelope persistence; Wolverine native transport used its default `wolverine_queues` schema. The restricted-role run exposed the mismatch with SQLSTATE 42501. Earlier passing delivery results establish behavior but do not establish the approved schema isolation. This was a local test-fixture scope defect, not a Product runtime change.

Containment: stopped those runs, removed the exact authorized temporary role/schema/HBA block and verified the original HBA hash. ConfigureStorage now explicitly binds both transport and envelope storage to the case schema, with a regression assertion. Restricted startup/delivery must be rerun. Existing default-schema objects are retained for read-only attribution; do not drop or modify them without establishing ownership and separate cleanup authority. No claim is made that earlier UUID cleanup removed the default transport objects. Private logs and restricted-runtime receipts retain the failed attempt. Closure requires successful bounded rerun and disposition of residual objects.


### DRIFT-T12-001 cleanup disposition — 2026-10-30

NukeGeng separately authorized deletion of exactly the two T12 native queue tables and their dependent indexes. Read-only attribution found one technical DeliveryProbe matching the approved fixed case and an empty scheduled queue. With no active fixture children, cleanup acquired table locks in a bounded transaction and dropped the two exact tables without CASCADE. Verification confirms the schema was retained and has zero relations. No HBA change or other object deletion occurred. Private receipt: `residual-default-transport-cleanup-receipt.json`. Residual-object DEBT-T12-001 is closed for cleanup; schema isolation regression and corrected restricted-runtime evidence remain part of final PoC validation, not automatic T12/runtime acceptance.
