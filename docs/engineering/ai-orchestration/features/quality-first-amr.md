---
id: FEAT-AMR-001
status: implemented
owner: engineering-maintainers
last_reviewed: 2026-10-19
module_id: ENG-TOOLING
feature_id: FEAT-AMR-001
phase: developer-tooling
source_paths: ["docs/tooling/amr.py", "docs/tooling/amr_goal.py", "docs/tooling/amr_native.py", "docs/tooling/changeguard.py", "docs/tooling/doccheck.py", ".codex/config.toml", ".github/workflows/docs-ci.yml"]
test_paths: ["docs/tooling/tests/test_amr.py", "docs/tooling/tests/test_goal.py", "docs/tooling/tests/test_native_goal.py"]
---

# FEAT-AMR-001 — Quality-first AI engineering orchestration

## User story / outcome

The owner-approved [Clients repository split](../../../decisions/0013-clients-git-submodule.md) adds one bounded traversal exception: Product docs/context scans exclude the declared Clients worktree and bind content to its indexed gitlink instead. Local Clients dirt/commits are ignored by Product Git status identity until the pin is staged; Product index, HEAD and canonical source changes remain detected. The exact topology and pin are validated by [FEAT-CLIENTS-001](clients-repository.md). AMR model routing, task control and historical execution receipts are unchanged. Pre-split snapshots require an explicit refresh before unfinished work resumes; completed checkpoints are not rewritten or replayed. Boundary regressions are mapped by FEAT-CLIENTS-001.
Repository contributors use bounded Architect design when needed, Luna-led GOAL execution, deterministic quality gates and one default Codex Auto PR Review with a bounded follow-up budget. AMR v4 replaces the v3.1 policy in the existing engineering tooling and checkpoint flow; it does not create a competing orchestrator or change product architecture.

## Pre-conditions
Python 3.11+; Codex CLI for live operations. No new dependencies. Git and a connected provider are required for PR review, not assumed from the presence of `.github/`. Inspect native schema, effective config and model catalog before invocation. Missing/unsupported capability is explicit and fail-closed.

## Main flow
1. Discover runtime and source; use deterministic commands for mechanical lookup and validation.
2. Keep one coordinator and no collaboration sub-agents. Luna Max (`gpt-6-luna/max`) owns approved GOAL implementation, tests, ordinary fixes, docs and deterministic gates.
3. When accepted designs/contracts do not cover a required decision, route one bounded Markdown-only Architect task to Sol High (`gpt-6.1-sol/high`). The Architect returns after the design; implementation waits for explicit human approval tied to the exact design task and content.
4. Do not route ordinary implementation to Sol merely because it touches security, tenant isolation or state. When an approved design covers the change, Luna implements it and critical PR findings remain blocking.
5. After two evidenced failed Luna repairs, route only the reported defect to Sol Medium for non-critical debugging or Sol High for critical security, concurrency, tenant isolation, irreversible effects, state integrity or distributed recovery. Four failures block for maintainer investigation.
6. Preserve source hashes, Git identity, decisions, consulted paths, boundaries, tests and findings. Caller-reported model/effort stays `unverified` without native turn-bound evidence; unknown outcomes are reconciled, never replayed.
7. `goal validate`, `goal accept` and milestone transitions run deterministic gates and exact-content triage only; they do not invoke AI reviewers. The default independent reviewer runs on the feature PR after applicable CI.
8. Use one Codex Auto PR Review by default, at most three requests total. The first covers the full diff; follow-ups cover changes since the last verified reviewed SHA with context. Track SHAs and findings, avoid unchanged revisions/duplicate findings and stop after round 3. Blocking findings must be resolved before human merge.
9. Restrict manual GitHub PR review actions to Luna as GOAL coordinator or the human maintainer. Architect/debugger sessions return their bounded design/defect result without posting PR comments or initiating/managing BIR triggers.

## Acceptance criteria
- Ordinary and critical Developer GOAL tasks use exact Luna Max (`gpt-6-luna/max`) when advertised; risk labels alone never invoke Sol.
- A bounded Architect task uses exact Sol High (`gpt-6.1-sol/high`), writes only declared Markdown design paths, ends after one task and cannot be accepted without human approval bound to that task and design content.
- Debugger tasks are unavailable before two evidenced failed Luna repairs, require a reported defect, and cannot write outside its affected paths. Medium handles non-critical defects; High handles the defined critical defect classes; four failures stop for maintainer investigation.
- `goal validate`, `goal accept` and milestone completion execute deterministic gates and contributor triage without invoking an AI reviewer. No review/test/model-switch loop can auto-replay or auto-advance work.
- Existing completed v3/v3.1 checkpoints retain their original evidence and rules. An unfinished historical task migrates only its review-policy obligation with an auditable record; unknown work is never replayed.
- PR review defaults to one Codex Auto PR Review and is limited to three manually accounted rounds. Repeated SHA/unchanged source-tree review, missing/disallowed initiators, reposted findings and unresolved blocking findings fail validation; exhaustion is not approval and does not route to Sol. Historical v3.1 schemaVersion 1 ledgers remain readable.
- Architect/debugger sessions do not initiate or manage PR reviews. Manual trigger authority belongs to Luna as GOAL coordinator or the human maintainer. AMR v4 BIR schemaVersion 2 requires a declared trigger, allowed initiator and evidence per round; Luna requests include session evidence and maintainer requests include authorization evidence. Validation rejects missing/disallowed declarations but cannot authenticate claims or block direct provider triggers.
- Native model catalog support, actual turn selection, provider PR review execution, native hard-cap support, CI results and human approval are reported as separate evidence. No feature or production authority is inferred from config or tests.

## Authorization / tenant-scope review
| Actor | Permission | Scope |
|---|---|---|
| Contributor | Existing repository/runtime access | Local development files |
| Native reviewer | Advisory review only | Exact content/revision reviewed |
| Authorized human operator | Existing explicit approval, target lease, immutable spec | Sensitive production operation |

No new product permission, role checks or tenant input. Tooling never dispatches to a cluster, queue or device. The native harness denies client approval requests; it never manufactures a human approval. Auto-review is probed only in a disposable local directory and must emit a native terminal decision to count as verified. Root instructions remain mandatory even in Full Access.

## Negative & recovery scenarios
Missing capability/catalog model when a switch is required, unsupported effort, silent model reroute, stale handoff, file escape/symlink, duplicate/stale review, missing terminal review, failed/timed-out check, unknown outcome and missing Git/provider all prevent a false passing claim. Expired/missing production approval and wrong tenant remain product test obligations; no backend exists here to execute them. Native probe crashes/timeouts are failures; never retry an unknown side effect automatically.

- New dispatches use policyVersion 4 while retaining checkpoint schemaVersion 1, immutable dependency plans, atomic execution receipts, source protection and no-replay recovery.
- Architect tasks use exact `gpt-6.1-sol/high`, write bounded Markdown design only, return after one task and cannot complete without a current human approval bound to the task/content digests.
- Developer tasks use exact `gpt-6-luna/max`, including critical implementation covered by accepted design/contracts. No exact model/effort substitution is allowed.
- Debugger escalation requires two evidenced unsuccessful Luna repairs, a stable finding ID, reproduction, and affected paths. Medium handles non-critical defects; High handles the specified critical defect classes; writes cannot escape the reported paths; four failures block.
- `goal validate`, `goal accept` and milestones invoke no AI reviewer. Deterministic gates, current-content contributor triage, failed-check blocking and sensitive-operation human approvals remain mandatory.
- Codex Auto PR Review is the default independent PR review after applicable CI. One round is default, maximum three per PR, full diff first, then incremental changes from the last verified SHA with surrounding context. The BIR ledger tracks counts, run IDs, SHAs, root-cause findings, deduplication and maintainer escalation. Unchanged source trees/SHAs cannot consume another round; exhaustion never approves.
- Blocking PR findings prevent merge. Sol debugging is never an automatic PR-review escalation. Human maintainer retains merge authority; sensitive production approval remains separate.
- Completed v3/v3.1 checkpoints retain original model/review requirements. Unfinished historical work can migrate review policy without changing its execution receipt; unknown work is not replayed.
- Native model/effort evidence, active desktop selection, provider review completion, provider hard-cap enforcement and human identity are reported separately; unsupported/unconfigured capabilities remain explicit.
- Existing protected write paths, source/test ownership mapping, LAB observe-only rules and architecture/permission boundaries remain unchanged.
- Product bootstrap follow-up: when Kordra.slnx exists, existing gates run locked restore, build and product tests; failed earlier stages block dependent tests. Product test/bootstrap paths retain owner-feature mappings.
- Product bootstrap follow-up: when Kordra.slnx exists, the existing gates also run locked restore, build and product tests; failed restore/build blocks dependent stages to prevent stale-binary passes. Build outputs are excluded from source snapshots and cannot be mapped as source. Product test/bootstrap file changes require owner feature updates through changeguard.
- AMR-01–09: deterministic lookup, risk routing, explicit unavailable result, bounded escalation and source-validated handoff covered offline; enabled profiles receive real native invocation tests.
- AMR-10–13: distinguish native sandbox decision, local native review and provider automatic review; absent provider/approval support reported honestly.
- AMR-14–15: fixes require reproduction rerun and a current review; dismissals require evidence; unresolved blockers prevent completion.
- AMR-16–18: source/doc mapping checked, LAB observe-only preserved, unavailable usage stays unknown.
- Run strict docs, scope registry, unit/contract tests and native integration checks. Do not infer runtime support from a configuration file.
- Documentation discovery excludes only the reserved local directories `.git`, `.temp` and `.amr-local`, pruning them without traversing directory symlinks. Imported package drafts and private checkpoint/backup copies cannot create duplicate IDs or broken-link failures in canonical checks. Maintained and untracked docs elsewhere, including other hidden directories, remain validated regardless of `.gitignore`; real duplicate IDs and broken links must still fail.

## Source & test traceability
Sources/tests above map the existing implementation committed in d0eff75951d1d587b1d9689c189a8a62bae18d58 and the current goal-execution extension. [Contract](../contract.md), [operating guide](../README.md), [runtime evidence](../testing-and-evidence.md). Implemented describes existing tooling, not owner acceptance, provider integration or verification of every extension. New extension evidence is recorded separately after actual checks.

Documentation Sync v1.5 follow-up maps the discovery correction to `docs/tooling/doccheck.py` and `DocumentationInputs` regressions in `docs/tooling/tests/test_amr.py`. Actor: repository contributor; permission: repository filesystem access; scope: local documentation validation, no tenant or production access. A duplicate-ID/broken-link import fixture failed before the fix; the tests also retain rejection of invalid untracked/ignored canonical docs and real duplicate IDs, and avoid traversal of external directory symlinks. The local checker input policy adds no package dependency, product contract, permission change or ADR.

Shared bootstrap fixture adaptation (2026-10-18), task `F0-OPP0-SHARED-OWNERSHIP-FIX`: `Mapping.test_product_tests_and_bootstrap_files_need_feature_ownership` now derives all applicable owners from current source/test metadata instead of assuming Artifact-only ownership. Accepted ADR-0010 adds Inventory co-ownership of `Kordra.slnx` and product CI; assertions explicitly require both FEAT-ART-001 and FEAT-INV-001 for these shared files, require each omitted owner's exact `STALE` diagnostics, and retain unowned test-path rejection. Changeguard production behavior and ownership mappings are unchanged. This local contributor fixture correction preserves feature status and existing evidence; no product capability, contract, permission or ADR changes. Tests, coordinator gates, task-bound native receipt verification, independent review and exact-content contributor triage remain pending after this task returns; no passing result is claimed here.

## Open decisions, linked ADR and technical debt
## Local goal execution exercise

Goal execution exercise: worker completed; gates and review recorded separately.
Goal execution exercise 2: worker completed; gates passed for its recorded snapshot; native Code Review timed out and remains unverified.

The exercise is a local documentation change only. Its worker statement is not a passing gate, independent review, production authorization or product completion claim.

## Open decisions and boundaries
No product architecture, technology, state machine or permission model changes. Engineering glue uses existing Python stdlib and native Codex. GitHub repository identity and `main` branch protection are verified for NukeGeng/KORDRA. `[NEEDS-DECISION]`: organization-approved Auto-review defaults, provider PR Code Review on the current head, and independent owner acceptance. See DEBT-AMR-001, DEBT-AMR-002 and DRIFT-AMR-001 in the existing registers.

## Historical AMR v3.1 increment — superseded for new execution

The following dated v3.1 record preserves the former policy and its verification history. AMR v4 above governs new tasks and reviewer routing; completed v3.1 checkpoint receipts retain their historical validation rules.

Owner-approved engineering policy: [guide](../README.md), [BIR](../bounded-incremental-review.md), [local contracts](../contract.md). Actor: repository contributor; permission: existing repository filesystem access; tenant scope: none; use case: cost-aware local engineering and PR review. Existing `amr.py`, `amr_goal.py`, `amr_native.py`, configuration and mapped tests remain the only implementation owner paths. No product runtime architecture, permission, dependency or proposed ADR changes.

The existing router uses Luna Max for ordinary work and Sol High for critical/architecture/complex-debugging routes. Native model probing deduplicates equal ROUTINE/ENGINEERING selections. Normal intermediate GOAL tasks run gates and contributor triage without per-task Sol review; the deterministic final task and CRITICAL tasks retain independent Sol High review. Design-review receipt/triage is required before marked critical-design dispatch. New records add policy v3.1; original v3 receipts/review obligations remain readable and are never replayed.

BIR is manually enforced, not a native capability claim: three provider requests total, full then incremental from last verified reviewed SHA with context, canonical grouped/deduplicated findings, exact-head fix/dismissal evidence and escalation after exhaustion. `amr.py validate-bir` deterministically validates the separate manual ledger and Git history without triggering/stopping reviews, approving merges or resetting milestone review. Pending/failed/unknown requests consume budget; uncertain review history must reconcile before a follow-up. DEBT-AMR-001/003 remain open for provider enforcement/active coordinator integration.

Regression coverage includes exact Luna Max routing/config, critical escalation, normal-task versus terminal-milestone review, pre-implementation design binding/triage, legacy checkpoint receipts, full/incremental BIR baselines, all requested rounds consuming budget, fourth-round rejection, dedup/tracking, current-head fixes/dismissals, exhaustion escalation and real Git ancestry rejection. Actual check counts and native evidence are recorded after execution in [testing and evidence](../testing-and-evidence.md).

### Independent implementation review remediation

Native Sol High review identified three actionable defects, each reproduced by a failing regression before repair: reusable task ID insufficiently bound design advice; completed escalated review could not resolve post-budget fixes on a new head; saved ordinary-task Sol retention ignored newly available Luna Max. The fixes bind full task digests in design/triage, support an explicitly recorded current-head escalated review while preserving stale-provider reporting and exhausted budget, and recheck preferred availability before dispatch with recorded retention exceptions. Existing v3 receipts remain unchanged. New/negative regressions cover each path; current gates and native follow-up receipts must be checked before completion.

A follow-up review confirmed the three direct fixes and found a source-normalization mismatch in later design acceptance/history. A new regression reproduced rejection of a legitimately dispatched design task after owner metadata cleared a source claim. The fix persists the complete normalized dispatch task/digest, permits only conservative claim downgrades relative to the immutable plan, and reuses that snapshot throughout validation/acceptance/history. Negative tests reject altered objectives/scopes, promoted claims and unplanned fields even with recomputed digests. Legacy v3 receipt handling is preserved.

### Bounded runner context repair — F0-AMR-RUNNER-CONTEXT-FIX

Acceptance scope: distinguish the serial implementation runner from its parent coordinator in native developer instructions. Preserve the complete GOAL objective as background data and forward the exact bounded task, write paths and invariants. The parent retains GOAL controls, the live checkout execution lock, prerequisite review, acceptance and milestone progression. The runner reads AGENTS/WORKFLOW and consulted sources, implements only the dispatched slice, runs permitted local tests and reports real results; it must not reacquire coordinator controls or invoke nested reviews. Missing authority, source uncertainty and sandbox denials still block the affected action.

Implemented locally in the working tree: `amr_native.execute_task` supplies native developer instructions and retains the GOAL objective as background data. `amr_goal.step` constructs compact pre-dispatch facts only after existing source/task validation, advertised route checks and applicable task/content-bound design review plus contributor triage. These bind the normalized task digest, input content, plan and dispatch attempt, with parent lock ownership and exact native design-review identity/evidence when required. Caller objective/decision prose is never prerequisite proof. These facts do not grant approval or replace execution/gate/review receipts. SchemaVersion 1, historical v3 receipts, lock/dispatch/unknown-outcome guards and current design validation remain unchanged. Source owners: `amr_native.py`, `amr_goal.py`; regressions: `test_native_goal.py`, `test_goal.py`. Actor: repository contributor; permission: repository.write; scope: local development, no tenant or product runtime change.


Local evidence (2026-10-18): new regressions first failed on absent native developer context and pre-dispatch payload; after repair, `python3 -m unittest discover -s docs/tooling/tests -v` passed all 141 offline tests. Native transport/model calls are mocked; no live execution or independent review is established by these tests. `python3 docs/tooling/doccheck.py` passed (220 Markdown files, zero errors/warnings); scoped `git diff --check` passed. Parent full gates, native execution verification, independent review, finding triage and shared contract/workflow reconciliation remain pending acceptance. Full `amr.py gates --base 2aa156f4e6e3afe3cc648186ac6a3242781dc71b` was not invoked by this bounded runner; it includes product package restore and belongs to the coordinator under the task's no-network restriction. No GOAL controls or nested reviews were invoked, and no new dependency, ADR or debt closure is claimed.

### Coordinator verification of runner recovery — 2026-10-18

F0-AMR-RUNNER-CONTEXT-FIX was accepted with exact native Sol 6.1 High terminal execution, current full gates (141 tooling/67 product at that checkpoint), stable independent High review and contributor triage. Regression tests exercised the real thread/start configuration encoder with mocked transport, source/task-bound pre-dispatch metadata after actual parent guards, and retained lock/model/design/legacy/unknown-outcome tests. Reviewer sandbox temporary-directory failures were not counted as passed; coordinator full-suite results were independently recorded.

The subsequent FEAT-IAM-001 continuation exercised the updated dispatcher live: it consumed validated design facts, changed only its two allowed paths, and completed with native `gpt-6.1-sol/high`; local coordinator gates then passed 141 tooling/133 product tests, including 66 new policy cases. This verifies the bounded-context repair on actual work, not only configuration. Prior no-op/failed attempts remain incomplete; DEBT-AMR-004 records residual ephemeral failure-detail diagnostics. Provider automatic PR review/hard cap and native desktop GOAL controls remain separate unverified capabilities. No product architecture or ADR acceptance occurred.

## AMR v4 PR #4 repair — 2026-10-19

Native turn completion is transport evidence only. New task executions must return exactly one `AMR_TASK_OUTCOME:` JSON line with `schemaVersion: 1`, the dispatched `task_id`, `task_digest` and `input_content`, a structured `status` (`completed` or `blocked`), a nonempty summary, declared changed artifact paths, and a `no_change_reason` value (`null` unless explaining a write-task no-op). AMR binds that outcome to the saved dispatch and records the actual local file identities for its declared artifacts. Claimed paths must match the observed changes and stay within `write_paths`; a blocked result, missing/stale/forged marker, or unexplained write-task no-op blocks before gates and triage. Read-only inspection may complete with an empty artifact list. A no-change explanation is retained for an intentional write-task no-op. Bounded repair validation refreshes artifact identities without replaying native execution.

The additive outcome receipt does not change checkpoint `schemaVersion` or historical execution receipts. Completed v3/v3.1 checkpoints and pre-fix v4 completed checkpoints without the new marker remain readable under their existing validation rules. An unfinished checkpoint without a structured outcome cannot be newly accepted from turn completion alone; the CLI will not synthesize success, so maintainer reconciliation or a fresh task plan is required. No execution is replayed automatically. For an unfinished v3/v3.1 Architect task, validation and acceptance continue to require the original design-review receipt, checked against the dispatch-normalized task and original input content, even if its review-policy obligation has migrated to v4. This compatibility check does not request another Sol review.

## T04 shared PostgreSQL fixture scheduling — 2026-10-22

The canonical Product CI and existing amr.py deterministic gate now pass MSBuild -m:1 to the solution test command, so separate Product test projects run sequentially. AUD saturation tests deliberately acquire ACCESS EXCLUSIVE on the shared isolated aud_intents table; running new TEN fixture provisioning concurrently produced legitimate bounded unavailable results. Project serialization preserves every assertion, timeout, failure fixture and test count, and changes no product runtime or AMR routing/review/checkpoint policy. No Website process, service or test is involved.
