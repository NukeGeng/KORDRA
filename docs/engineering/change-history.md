---
id: ENG-HISTORY
status: proposed
owner: unassigned
last_reviewed: 2026-10-19
---

# Feature change log and release-note discipline

## Policy
Thay đổi nghiệp vụ được ghi ở chính feature doc với evidence (PR/commit/version), **không tạo bản mô tả song song theo sprint**. Trang này chỉ giữ index timeline cho thay đổi cross-cutting/major.

## Changes
Sol clarification is tracked by FEAT-AMR-001 (ENG-TOOLING); existing router/config mapping is unchanged. No architecture decision, product authorization change or migration is required.

| Date | Change ID | Module / Feature | Source PR/Commit | ADR / contract | Migration impact | Owner |
|---|---|---|---|---|---|---|
| 2026-10-19 | PR4-REVIEW-REPAIR | ENG-TOOLING / FEAT-AMR-001; local operations documentation | [PR #4 native review](https://github.com/NukeGeng/KORDRA/pull/4#pullrequestreview-5566543009) on `705c622` | Preserve legacy Architect design evidence before acceptance; require explicit task outcome and observed artifacts; align active v4 docs; clarify PostgreSQL teardown | Completed historical checkpoints stay readable; unfinished receipts lacking an outcome need explicit finalization without replay. No product architecture, ADR, permission or database change | engineering-maintainers |
| 2026-10-18 | OP-P0-ARCH-EVIDENCE | MOD-INV / FEAT-INV-001; Foundation architecture governance | Local experiment, verified native High task/re-review, full gates and contributor triage | Retained sandbox timeout; repaired local fixture reference omission; two forbidden-edge checks and 14 HTTP assertions; proposed ADR-0010 for actual local bootstrap | No product source adoption; API-local preference recorded; explicit ADR approval and IAM/provider/rights decisions remain pending | Maintainer/architecture/security acceptance required |
| 2026-10-18 | OP-P0-FOUNDATION-PREP | MOD-INV / FEAT-INV-001, MOD-PRJ / FEAT-PRJ-001 and MOD-SEC / FEAT-SEC-001; ENG-TOOLING evidence | Local continuation `KORDRA-OP-P0-FOUNDATION-001` from `2aa156f4`; verified Medium inventory/environment and High credential executions, full gates, stable native High reviews and contributor triage | Proposed I0 inventory/environment/credential seams; MOD-SEC ConnectionProfile ownership ambiguity fixed and re-reviewed; product source/mappings and ADR acceptance unchanged | No runtime/infrastructure operation; old failed checkpoints preserved without replay; DEBT-AMR-002 stable native-review gap resolved; High credential task completed after explicit resume; owner selected Kubernetes/Helm read-only design target; runtime decisions remain open | engineering-maintainers for task evidence; product owner remains unassigned |
| 2026-10-18 | DOCSYNC-V1.5 | Product planning / existing module registry / ENG-TOOLING governance | Local `codex/docs-sync-v1-5` from verified `2aa156f4`; [integration evidence](doc-health.md) | 12 proposed documents; OP-01–OP-10, OP-P0–OP-P4 and all 43 A–F groups; ADR-0008/0009 and product contracts unchanged | Documentation sync plus checker-only draft discovery fix; preserve banner/domain and original GOAL outcomes; reconcile merged PR #1/CI and owner-only repository policy; no runtime, LAB change, license selection or release acceptance | engineering-maintainers for integration; product/architecture/rights owners remain explicit decisions |
| 2026-10-17 | AMR-GOAL-P2-REMEDIATION | ENG-TOOLING / FEAT-AMR-001 | PR #1 follow-up; 110 AMR tests and full local gates passed on working tree | Existing private goal checkpoint; no public API, product contract, ADR or permission change | Local tooling only; scope drift blocks completion; direct coordinator receipts remain unsupported and documented; no production operation | engineering-maintainers |
| 2026-10-16 | AMR-V3-MANUAL-SWITCH | ENG-TOOLING / FEAT-AMR-001 | Current working tree; checkpoint/model-switch regression tests and post-change native config discovery | Backward-compatible goal checkpoint control fields; statuses distinguish model switch, approval and technical block | Local-only; single-agent default; manual selection stays unverified without coordinator-session evidence; no product/production operation | engineering-maintainers |
| 2026-10-16 | AMR-GOAL-EXECUTION | ENG-TOOLING / FEAT-AMR-001 | Current branch; native smoke and E2E receipts recorded after validation | Existing local task contract extended with plan/checkpoint v1; no product ADR change | Private local checkpoints only; no production operations or auto-merge | engineering-maintainers |
| 2026-10-16 | AMR-SOL-6.1 | ENG-TOOLING / FEAT-AMR-001 | User clarification; working-tree regression in test_amr.py | Local contract v1 clarified: Sol exclusively means gpt-6.1-sol | None; no model fallback or product behavior change | engineering-maintainers |
| 2026-10-15 | BOOTSTRAP-DOCS | All proposed docs | initial scaffold | proposed ADRs | none | unassigned |
| 2026-10-15 | AMR-2.1 | ENG-TOOLING / FEAT-AMR-001 | Local working tree, no Git metadata; [evidence](ai-orchestration/testing-and-evidence.md) | New local artifact schema v1; no product ADR/contract changes | None; no public API/data migration | engineering-maintainers |
| 2026-10-15 | GIT-BOOTSTRAP | Repository / ENG-TOOLING | User-supplied [GitHub repository](https://github.com/NukeGeng/KORDRA); initial `main` history and `origin` setup | No product contract or ADR change | Local files preserved; private receipts excluded | engineering-maintainers |
| 2026-10-15 | F0-ART-I1 | MOD-ART / FEAT-ART-001 increment I1 | Branch feat/artifact-identity-foundation; local Release build/format, 48 domain tests, 47 Python tests, native diff review completed | ADR-0008 scoped bootstrap; new local digest contract; public APIs/messages unchanged | No storage, registration, infrastructure or data migration | engineering-maintainers pending product owner assignment |
| 2026-10-16 | ADR-0008-ACCEPTED | Foundation / MOD-ART / FEAT-ART-001 I1 | Explicit user approval: "Approve the scoped ADR-0008 bootstrap" | Accept .NET 10 and pinned test-only dependencies only; other ADRs remain proposed | No runtime deployment or automatic merge authorized | User / engineering-maintainers |
| 2026-10-16 | KORDRA-DOCS-V1.2-SOURCE | Architecture / ENG-TOOLING | Integrated ten source-architecture documents; native GPT-6.1 Sol High review findings reconciled; no source migration | Proposal assigned ADR-0009 because ADR-0008 is already accepted for the scoped bootstrap | No project, package, host, contract or product behavior changed; proposal and broad architecture checks remain proposed; existing Domain boundary test remains in CI | engineering-maintainers |

## Commit convention suggestion
`feat(module): ... [FEAT-XXX]`, `fix(module): ... [FEAT-XXX]`, `docs(module): ... [FEAT-XXX]` (workflow hỗ trợ GitHub/CI, không bắt buộc conventional commits nếu team có quy tắc khác).

When implementing, require linkage via PR fields and code mapping; this timeline is *not* an implementation evidence source by itself.

## Local continuation — 2026-10-18 (unpublished)

- ADR-0010 explicitly accepted by the maintainer for the exact local bootstrap scope. FEAT-INV-001/I1 now implements Inventory Contracts/runtime, loopback API and bootstrap tests; shared solution/CI ownership is recorded with FEAT-ART-001. No runtime packages, positive inventory ingress, IAM/provider or production behavior added.
- Retained initial failed checkpoint; repaired equivalent xUnit assertion, bounded macOS negative loopback probe and existing shared-owner tooling fixture without weakening guards. Fresh continuation passed 114 tooling/67 product tests, independent native Sol 6.1 High review and contributor triage. No debt added/closed by bootstrap; existing provider/native GOAL-control gaps remain open.
- Only after bootstrap acceptance, reserved 45 folders with proposed README files; no source moved or future projects created. Full source architecture ADR-0009 remains proposed. Local-account preference is a design input for FEAT-IAM-001, not ADR-0003 acceptance. No commit/push/merge, new remote CI or company operation performed.

- F0-OPP0-IAM-DESIGN / FEAT-IAM-001 / MOD-IAM: proposed local-account trust/authorization seams and decision readiness completed with verified native Sol 6.1 High execution, full existing gates and independent High review/triage. Only the owning feature changed during execution; no authentication/policy implementation, new schema/dependency, ADR acceptance or debt change. Local-account preference is recorded without extending production authority.

## AMR v3.1 — owner-approved engineering policy (2026-10-18)

FEAT-AMR-001 / ENG-TOOLING extends existing routing, native adapter, goal checkpoints and tests. Luna Max (`gpt-6-luna/max`) becomes the default normal developer; Sol High (`gpt-6.1-sol/high`) covers architecture/critical work, complex debugging and independent milestone review. Ordinary intermediate tasks defer PR review explicitly, with deterministic gates and contributor triage retained. Marked critical design requires pre-implementation native review/triage. Historical v3 receipts preserve their original selection/review obligations; no replay or product ADR acceptance.

[Bounded Incremental Review](ai-orchestration/bounded-incremental-review.md) defines manual three-request PR accounting, full/incremental SHA ranges, canonical grouped findings and escalation. Existing `amr.py validate-bir` is a deterministic validator, not a native hard cap or trigger controller. Default provider Code Review execution/enforcement remains unverified and DEBT-AMR-001/003 stays open. No remote setting, product runtime architecture or production approval changed. Actual check/review results are recorded in [runtime evidence](ai-orchestration/testing-and-evidence.md).

AMR v3.1 implementation review follow-up reproduced and repaired three findings: complete design-task digest binding, post-budget Sol/human resolution on a new head, and fresh-catalog enforcement of Sol retention exceptions. Source/test/contract/feature evidence stays in FEAT-AMR-001; current gates and independent follow-up review are required before handoff.

## Synthetic IAM proof and bounded-runner repair — 2026-10-18

- FEAT-IAM-001 / MOD-IAM: implemented only the authorized synthetic, test-private candidate policy and 66 permit/deny cases in the existing bootstrap suite. Full local gates pass 141 tooling/133 product tests; formatting and native High design/execution/post-review plus contributor triage completed. No product IAM project, API, dependency, database or schema adoption; ADR-0003/0009 remain proposed.
- FEAT-AMR-001 / ENG-TOOLING: corrected bounded-runner context so background GOAL coordination commands cannot be mistaken for the scoped implementation task. Native developer instructions and compact parent-validated task/source/design facts preserve locks and receipt/approval guards. Regression tests and live IAM continuation validate recovery. Old no-op/failed attempts remain unaccepted history.
- Reconciled feature/module/permission/ADR/roadmap/traceability and engineering contracts. Added open DEBT-AMR-004 for safely retaining terminal native-failure diagnostics; no existing provider/native-GOAL-control debt closed. No commit/push/merge, remote CI claim or company operation.

## 2026-10-18 — Maintainer acceptance of narrow ADR-0003

- FEAT-IAM-001 / MOD-IAM: recorded the explicit human acceptance of the reviewed permission/trusted-scope baseline, with exact pre-acceptance proposal hash and native review identity. Updated decision index, feature/module, permission evidence, roadmap and traceability; preserved historical proposal/PoC evidence.
- No policy code/test behavior, product source, schema, dependency, authentication/storage or API-access change. ADR-0009 stays proposed. No debt added/closed, merge, remote publication or infrastructure operation. Next: reviewed concrete authority-contract proposals before any runtime adoption.

## 2026-10-19 — AMR v4 Architect / Developer / Reviewer

FEAT-AMR-001 / ENG-TOOLING replaces the v3.1 Sol-driven GOAL review loop in the existing router and checkpoint implementation. Sol High is bounded to architecture design and critical defect debugging; Luna Max owns normal GOAL execution, tests, deterministic gates and feature PR preparation; Codex Auto PR Review is the separate default PR reviewer. `goal validate`, `goal accept` and milestone completion no longer run AI reviews. The BIR ledger defaults to one review, permits at most three, rejects duplicate/unchanged revisions and escalates exhausted blockers to the human maintainer.

Checkpoint schema remains v1. Completed v3/v3.1 receipts retain their historical evidence and obligations; an unfinished old task records a narrowly scoped review-policy migration when explicitly validated/accepted, without replay or alteration of execution/model receipts. Updated FEAT-AMR-001, AMR contract/guide/workflow, AGENTS.md, WORKFLOW.md, tests and this traceability/change log. Product architecture, accepted ADRs, permissions, provider settings, merge and production authority are unchanged. This migration deliberately ran no AI review; exact deterministic gates and current runtime capability evidence are recorded in [AMR v4 validation](ai-orchestration/testing-and-evidence.md#amr-v4-policy-migration--2026-10-19).

## 2026-10-19 — FEAT-INV-001 Linux fixture recovery

Corrected the compiled architecture fixture's sanitized Linux .NET environment after the first PR CI run failed during NuGet migration-mutex initialization. The child process now receives existing isolated HOME/CLI-home directories and a private Linux `XDG_RUNTIME_DIR`; the forbidden assembly-edge checks and empty package-source boundary are unchanged. Targeted macOS and full sanitized Linux SDK 10.0.400 tests passed locally; hosted docs and product CI passed on implementation SHA `38da931edae3c36182c46712502b242598f9e0da` ([docs](https://github.com/NukeGeng/KORDRA/actions/runs/39188847531), [product](https://github.com/NukeGeng/KORDRA/actions/runs/39188847534)). This does not complete FEAT-INV-001 registration or authorize provider/cluster access. No contract, ADR, dependency or product runtime change.
