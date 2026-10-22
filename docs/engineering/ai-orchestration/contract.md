---
id: ENG-AMR-CONTRACT
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-22
---

# Local tooling contract — version 1

Consumer: repository contributors, native Codex and the existing documentation CI. This is a new local file/CLI contract; there are no public API/message consumers or database migrations. It does not introduce domain messages, an approval service or an operations driver. Breaking local artifact changes must increment `schemaVersion` and require regeneration; unknown versions fail.

The active engineering policy is [AMR v4](README.md). This file preserves the v1 local artifact schema and records versioned execution-policy behavior below; historical v3/v3.1 rules apply only when validating their existing receipts.

## Task input
JSON fields: `task_id`, `objective`, `feature_id`, `module_id`, `actor`, `scope`, `permission`, `paths`, `consulted`, `invariants`, `decisions`, `tests`, `findings`, `next_action`. Strings must be nonempty; collection fields are arrays. `paths` and `consulted` are existing repository-relative files. Optional `output_paths` names exact repository-relative file targets, including files that do not exist yet; every output must match an owner-predeclared `planned_source_paths`, `planned_test_paths` or `planned_doc_paths` entry. The single matching owner feature may also be an output so the same task can add mappings for source/tests it creates; no other input/output overlap is allowed. Output paths reject traversal, symlinks and excluded private/cache locations, and may be written only when also declared in `write_paths`. This is source ownership validation, not user authorization.

Optional classification inputs: `execution_role` (`architect|developer|debugger`), `kind` (lookup/docs/format/crud/dto/tests/source/feature/integration/review/bugfix/architecture/complex-debugging), booleans `bounded`, `spec_verified`, `tests_known`, `signals`, integer `failed_repairs`, `failure_evidence`, `reported_defect` (`finding_id`, `summary`, `evidence`, `affected_paths`) and optional `write_paths`. Unknown kinds/signals and malformed flags fail. Proposed specs cannot be promoted by `spec_verified=true`. Architect paths must be Markdown. Debugger dispatch requires two failed repairs and restricts writes to reported affected paths.

Signals: auth, tenancy, platform, secrets, state-integrity, concurrency, idempotency, rollback, production, remote-execution, migration, architecture, critical-design, complex-debugging, reliability, milestone-review; integration, messaging, contracts, source-uncertainty, stale-docs. Risk labels and sensitive paths remain recorded, but do not reroute Developer implementation to Sol. Architecture/critical-design selects Architect; explicit debugging after two evidenced failed repairs selects the bounded debugger route. Evidence remains subject to source inspection; no confidence score replaces it.

## Output and persistence
Model naming: “Sol” binds exclusively to `gpt-6.1-sol`; Developer requires `gpt-6-luna/max`; Architect and critical debugging require `gpt-6.1-sol/high`; non-critical debugging requires `gpt-6.1-sol/medium`. Exact catalog unavailability blocks without substitution. AMR v4 uses one coordinator session and project configuration disables sub-agent creation.

- `route`: risk/profile/rationale/action, requested model/effort, `support=advertised|unsupported`. Advertised is native catalog discovery, not a successful invocation. Deterministic lookup has no model. Missing exact model/effort blocks with no implicit fallback.
- `context`: schemaVersion 1, structured task, route, Git head/branch/status (null if absent), all repository file hashes and aggregate digest. Excludes `.git`, private `.amr-local`, Python caches and generated .NET `bin`/`obj`/`TestResults`; none may be mapped as task source, including via symlink. Additions, deletions, changed contracts and symlink targets invalidate context. No transcripts/file contents are copied. Decisions and test details are contributor-supplied; validate source and receipts before trusting them.
- `gates`: schemaVersion 1, timestamp, pre-check content digest, source-stability check, actual commands/status/output. A nonzero or timed-out check fails; any source change during the run invalidates the results. No Git base means source mapping is explicitly `not_configured`, not passed. When Kordra.slnx exists, locked restore, product build and product tests are mandatory; failed earlier stages block dependent stages instead of testing stale binaries. CLI ledger validation requires those stages for an existing solution. Product checks stay not configured when no solution exists. A passing local status is not release completion.
- Native probes/review: requested/observed model and effort, terminal turn, reroute detection, event method names and actual usage when exposed. Runtime selection is observed through native `thread/start`; this is not an independent attestation of server internals. A model reply alone cannot establish selection. Review `verified` means execution observed, not findings resolved.
- Current desktop model passed to a local `goal recommend` or `goal resume` is caller-reported and stored with `status=unverified`. Catalog discovery proves availability only. Do not promote a manual selection claim to a verified active-chat model without native evidence bound to that coordinator session/turn. A verified child `thread/start` proves only the child task runner selection.

Raw local receipts are private (`.amr-local`, directory 0700, files 0600, Git ignored). Do not publish them without sanitization. Never put secrets, customer payloads or internal infrastructure identities in task summaries, evidence or docs. Timeouts/crashes produce an unknown/failed outcome; no automatic replay of side effects.

## Review ledger
The opt-in `validate-review` command checks schemaVersion 1, current `content` digest, `source_stable=true`, `local_status=passed`, mandatory `checks` (docs/scope/unit, all passed), `review`, `findings` and human approval fields. It does not trigger a reviewer and is not part of v4 `goal validate`, `goal accept` or milestone transitions. New critical PRs use the separate BIR/provider review gate. A finding needs ID and validation evidence. `fixed` needs JSON boolean `reproduction_passed=true` plus a `followup_review` object with status verified, current content digest and evidence; `dismissed` needs rationale; an open blocker prevents completion. Unknown states block.

This validates the ledger's structure, not the authenticity of provider actors or human approvals. Humans must inspect linked receipts, confirm checks cover the change and apply branch protection. Production operations must use the established authorization/lease/spec workflow; neither this ledger nor native approval authorizes them.

## Goal execution extension — version 1, active policy v4

The `amr.py goal` CLI extends existing routing/native/gates contracts. A plan contains `schemaVersion: 1`, `goal_id`, `objective` (active user goal text), `objective_path` (repo GOAL.md, digest-pinned), `milestone`, `progress_path`, `base` (verified Git commit), and `tasks`. Each leaf contains the existing task contract plus `depends_on`, `write_paths` (empty for inspection), and `milestone`. IDs are unique, dependencies acyclic, paths repository-local and feature-mapped. The objective and progress documents are source references, not executable instructions with greater authority than AGENTS. The goal objective and current task context are sent in the same native session.

Private state under `.amr-local/goals/<goal_id>.json` is an atomic checkpoint protected by a nonblocking OS lock. The plan is copied and hashed at initialization, objective content is pinned, and source/Git identities bind claims, gates and review. The optional `control` object adds `status`, `model_switch`, `switch_history`, `pause`, `pause_history` and `runtime_selection`; old version-1 checkpoints without this object remain readable and infer their status from task receipts. Source changes invalidate unfinished validation. Completed milestones are historical verified checkpoints, not a claim that their whole-tree hash remains current forever; later tasks must revalidate current source and owner context.

Task states are pending, in_progress (current-session preparation), running (explicit native runner), unknown, validation_failed, awaiting_triage, completed, failed. Before invocation persist running/attempt identity; a running checkpoint after process loss becomes unknown. Unknown work never automatically replays. `reconcile` requires contributor evidence and current source identity to classify observed work as executed or failed; it does not manufacture native completion. Only an observed verified native execution can enter native-runner validation; current-session work uses the separate completion contract below. `validate` reruns deterministic gates without repeating execution, bounded to four attempts. `accept` binds contributor triage to exact current task/content and private native/gate receipts, requiring no open findings. It does not invoke an AI reviewer and is not human merge or production authorization.

Goal control states are `active`, `awaiting_model_switch`, `awaiting_approval`, `blocked` and `completed`. A model-switch recommendation is created only at a safe boundary where all task records are pending/completed and the next role justifies interruption. It stores current/recommended selection, rationale, task/route, content and Git identity, and next action. Luna Max→Architect Sol High is requested only for a bounded design task; after human approval, a Developer task requires an explicit switch back to Luna Max. Resume requires the exact recommended model/effort, unchanged source/Git/task identity and no unresolved operation. No automatic resume is allowed.

Manual model selection in recommend/resume is recorded as unverified. Current-session completion separately checks referenced native local session metadata and latest turn-context entries; this is a model-context observation, not terminal execution or a server-side attestation of the caller. Native Goal pause/resume is controlled by the Codex runtime, not this CLI. `awaiting_approval` requires an evidence reference to continue but cannot authenticate or grant approval; `blocked` requires resolution evidence and cannot clear failed/unknown/untriaged task state. Neither state permits production work without the existing explicit approval/lease/spec process.

Progress is returned by `status` and reconciled into the existing roadmap/feature docs by the coordinator, avoiding a second product backlog. `step --context` prepares one approved local Developer task for this chat; `finish` records inspected current evidence after implementation. Explicit `step --native-runner` executes a serial native task and deterministic validation, then waits for `accept` triage. The coordinator inspects evidence and completes one task before preparing another. No completion advances on a model's success claim alone. Codex Auto PR Review is the default PR channel, requiring provider configuration and current-head completion evidence; it is never inferred from local review. Plan/local ledger schemas do not change existing task/context v1 artifacts.

### AMR v4 role, approval and checkpoint semantics

New dispatches write `policy_version: "4"`; the checkpoint schemaVersion remains 1. `kind=architecture` or explicit `execution_role=architect` routes to `gpt-6.1-sol/high` and permits only bounded Markdown task paths. The Architect returns after that one design task. Completion requires `triage.human_design_approval` with `decision=approved`, exact `task_id`, `task_digest`, `content`, `approved_by`, `scope` and `decision_evidence`. The local CLI records but cannot authenticate the human decision. `goal step --design-review` rejects old AI design-review receipts for v4.

Developer tasks route to `gpt-6-luna/max`, including security-, tenancy- or state-sensitive implementation when accepted designs/contracts cover it. A design gap blocks implementation pending an Architect task or a human-owned decision. Debugger tasks require at least two failed Luna repairs plus a defect ID, summary, reproduction evidence and affected paths. `gpt-6.1-sol/medium` handles non-critical debugging; `gpt-6.1-sol/high` handles critical security, concurrency, tenant isolation, irreversible effects, state integrity and distributed recovery. Four attempts block for maintainer investigation. Debugging may not become a broad GOAL or PR review.

V4 `goal validate`, `goal accept` and milestone completion never invoke an AI reviewer. The task receipt uses `review={status:"not_required",channel:"pr_deferred",independent:false,source_stable:true,content:<gate digest>,reason:...}`. This explicitly separates deterministic gates and contributor triage from the default provider PR review. BIR v4 defaults to one provider review, allows at most three, tracks request identities and reviewed SHAs, rejects unchanged source trees, and blocks merge for unresolved findings. Its validator does not trigger or enforce a provider hard cap.

Completed v3/v3.1 checkpoints keep their historical execution and review validation. When an unfinished old task is explicitly validated or accepted, it may add `review_policy_version:"4"` and a migration record while retaining its original `policy_version`, route and execution receipt. This migration drops only the repeated task-level AI-review obligation; deterministic gates, triage, design-receipt history, no-replay recovery and separate PR review remain. Running/unknown tasks are not replayed.

### Task outcome evidence and receipt compatibility

New native executions return exactly one `AMR_TASK_OUTCOME:` JSON line. Its `schemaVersion: 1`, `task_id`, `task_digest` and `input_content` bind it to the validated dispatch; `status` is `completed` or `blocked`, `summary` is nonempty, `artifacts` lists changed declared write paths, and `no_change_reason` is null unless a write task intentionally made no change. The adapter parses this explicit marker rather than inferring success from natural-language keywords or native turn completion.

The coordinator compares claimed artifacts with observed changed files and records their actual local identities in `task_outcome.artifact_evidence`. Missing, malformed, stale or blocked outcomes and unexplained write-task no-ops stop before gates/triage; acceptance rechecks the task binding and current artifact evidence. Read-only inspection can report an empty artifact list. An intentional write-task no-op needs its retained explanation and contributor inspection during exact-content triage. Repairs stay within declared paths and refresh artifact evidence through validation without replaying execution.

Checkpoint schemaVersion remains 1. Completed v3/v3.1 and pre-fix v4 records without the additive outcome remain readable under their original checks. An unfinished record lacking a structured outcome needs explicit reconciliation or a fresh task that inspects retained work; it cannot be accepted from a completed transport turn. Never invent or retrofit a native marker into an older execution receipt. Original records remain intact and execution is not replayed. An unfinished legacy Architect task also retains its original design receipt requirement, validated against the dispatch-normalized task and original input content before validation/acceptance, even after review-policy migration to v4.

### Current-session Developer completion

For already-approved local Developer work, `goal step --context <private-json>` is the default. It does not invoke an AI turn. The context contains nonempty `scope_basis` citing accepted feature/design authority and `native_context` with absolute local session-log `source`, exact `thread_id`, `turn_id`, `model`, `effort`. Real session metadata and the latest turn-context must match the advertised Developer route (`gpt-6-luna/max`); catalog support alone is insufficient. No model fallback or new model exception is introduced.

Preparation stores `execution_mode: current_session`, task/digest, input content/Git identity, declared write-path before-hashes, protected outside-write content digest, scope basis, native context and `preparation_digest`. State is `in_progress`, attempts remain zero. Status/repeated step preserves the prepared task and baseline; it never dispatches or restarts work. A persisted claim blocks another current-session/native task in the checkout until completion or explicit reconciliation. A later chat may inspect/resume existing work; completion must reference the original thread, its latest observed turn, and the same exact Developer selection. Do not bypass a changed-session requirement by copying an old turn reference.

New preparations also save `protected_file_identities`, bound into the existing protected digest and preparation digest. `goal status` reports added/changed/deleted protected files separately from Git HEAD/branch drift. Historical preparations omit the additive field and keep their original digest; their diagnostic explicitly says that a complete protected-file baseline is unavailable. Status preserves checkpoint bytes unless an actual state transition or legacy control migration is necessary.

For stopped `running`, `unknown` or `in_progress` work, `goal reconcile --id <goal> --task-id <task> --evidence <nonempty-reference>` saves an exact private pre-reconciliation snapshot with SHA-256, then records the observed identity and failure/validation state without replay or completion. Evidence is a string, matching the CLI; a `.amr-local/...json` reference is read through the existing private-path/symlink checks and may include `findings` with unique nonempty IDs. Those known findings must survive subsequent recovery triage; omitted or unresolved blocking findings cannot become completed work. This is contributor evidence, not authenticated maintainer approval. New or changed write scope needs a fresh owner-mapped plan to inspect retained artifacts, not a reset of the old baseline. A reconciled failed checkpoint no longer holds an active current-session claim; it remains historical and does not satisfy dependencies by itself.

After implementing only `write_paths` and running applicable gates, `goal finish --id <goal> --task-id <task> --evidence <private-json>` requires:

| Field | Binding |
|---|---|
| `task_id`, `task_digest` | Prepared dispatched task |
| `identity` | Exact current content and Git identity; HEAD/branch must match preparation |
| `native_context` | Latest actual turn-context in the same coordinator thread and exact Developer model/effort |
| `artifacts` | Exact changed declared write-path → current hash mapping; deletion is `null` |
| `summary`, `no_change_reason` | Nonempty actual outcome summary; write-task no-op requires a reason |
| `gates` | Private current successful applicable receipt, including mapping on the exact plan base |
| `triage` | Exact task/content, contributor, rationale, explicit findings; known findings cannot disappear and blockers fail |

Protected outside-write content must remain unchanged. Completion stores `completion_kind: coordinator_execution`, `coordinator_receipt` plus digest, bound gates/triage, validated identity and complete task/context/output hashes. It reaches completed without invoking AI, rerunning tests, or creating `execution` / `task_outcome` native-terminal evidence. Reads revalidate this completion kind separately. Local evidence is contributor-controlled and is not an authenticated human decision or cryptographic attestation. Native receipt, provider review, CI and human approval remain distinct.

### Inspected local continuation

`goal continue --id <failed-goal> --task-id <task> --as-goal <new-goal> --evidence <private-json>` records retained work under existing approved repository-only scope. It creates a successor with `completion_kind: coordinator_continuation` and zero execution attempts; the failed origin stays byte-identical. This corrects checkpoint bookkeeping without repeat business/design approval or AI execution.

Eligibility is one stopped, reconciled **failed**, policy-v4 local Developer task, covered by existing accepted design. Architect/debugger/legacy/multi-task origins, running/unknown work, production/secrets/remote-operation scope, uninspected or external effects, and new decisions are excluded. A contributor declaration cannot authorize an external operation. Reconcile interrupted execution first; do not replay it.

Input is schemaVersion **2**, sharing schemaVersion-1 recovery fields `origin`, `identity`, `gates`, `triage`, `native_context` below. `artifacts` must cover **all paths + consulted + write_paths**, including created outputs. Replace `authorization` with:

```json
{
  "continuation": {
    "scope_basis": "Accepted feature/design and prior implementation authorization",
    "inspection_evidence": "Reference to inspected retained diff, outputs and stopped-run evidence",
    "execution_stopped": true,
    "effects": "repository_files_only",
    "external_operations": []
  }
}
```

Exact default Developer native model context, current complete artifact hashes, successful applicable gates/base mapping and contributor triage remain mandatory. Scope/inspection references are local declarations, not mechanically verified process or infrastructure state. If effects or process status cannot be established, fail closed and investigate. No non-default model override is allowed on this narrow path; historical explicitly authorized recovery below supports task-local exceptions. Installing the optimization never auto-promotes old checkpoints.

### Deterministic evidence reuse

`amr.py gates --base <verified-SHA> --reuse <private-receipt> --validation-context <nonsecret-fixture-epoch>` explicitly requests reuse. Without `--reuse`, checks execute normally. The fresh whole-tree receipt still binds current content and source stability. Each check records `execution: executed|reused`, `executed_at` and an `input_digest`; reused checks retain `reuse.executed_at` and `reuse.content` from the original execution. The original execution must be successful, source-stable and no more than **one hour** old; reuse does not extend that window. Old receipts lacking these inputs cannot be reused. Legacy schemaVersion-1 `product_tests: executed` means executed evidence is available; inspect per-check `execution` to distinguish a new run from reuse.

- Docs, scope and exact-base source mapping always execute. Tooling tests fingerprint the entire maintained tree because tests read metadata/docs.
- Product stages fingerprint maintained inputs conservatively, excluding documentation Markdown and README changes, plus Python identity, environment hash (excluding only per-call transport correlation `CODEX_TOOL_CALL_ID`), `dotnet --info`, known user NuGet configuration hashes and declared validation context. Unreadable/probe-failed inputs disable affected reuse. Raw environment/config values are never stored in the fingerprint receipt.
- Integration-test reuse additionally requires the same nonempty caller-managed fixture epoch. Rotate it after database resets, fixture/service changes, unknown external effects, or changes to external build/dependency inputs. Fingerprints do not attest database state, external imports or service health; if those cannot be established, omit reuse and run the checks.
- Failed/missing/malformed/stale/command-mismatched evidence executes checks afresh; failed restore/build blocks dependent stages even when old successful evidence exists. Input/environment changes during the run invalidate completion. Blocking findings, human approvals, provider PR review and CI cannot be cached by this mechanism.

### Explicit evidence recovery after direct continuation

`goal recover --id <failed-goal> --task-id <task> --as-goal <new-goal> --evidence <private-json>` adopts inspected current work into a **new** checkpoint. It does not change/replay the failed record or retrofit a native marker. Scope is deliberately limited to a single-task, failed, policy-v4 local Developer origin; Architect, debugger, legacy, running/unknown and sensitive-operation tasks are not eligible. Use existing interrupted-work reconciliation for running/unknown execution first.

The private input has `schemaVersion: 1` and these required fields:

| Field | Binding |
|---|---|
| `origin` | Exact `goal_id`, `task_id`, original checkpoint SHA-256 and dispatched `task_digest` |
| `identity` | Current `current_identity()` content and Git identity |
| `gates` | Repository-relative JSON receipt beneath `.amr-local`; successful, current, source-stable applicable checks, including mapping on the origin's exact base |
| `artifacts` | Exact current file-hash mapping for every dispatched `paths` and `consulted` input |
| `authorization` | `purpose: checkpoint_recovery`, `decision: approved`, exact `task_id`, `task_digest`, current `content`, nonempty `approved_by`, `scope`, `decision_evidence` |
| `triage` | Exact task/content, contributor `reviewed_by`, `rationale` and explicit `findings`; known findings cannot disappear and blockers prevent recovery |
| `native_context` | Absolute local session-log `source`, exact `thread_id`, `turn_id`, `model` and `effort`; checked against real session metadata/turn-context entries |

A non-default model additionally requires `authorization.model_override` with the exact observed `model`, `effort` and explicit maintainer `decision_evidence`. This is a task-local exception and does not change normal routing. Native turn context observes model selection only; it is never described as terminal task execution. The CLI records local maintainer/contributor declarations but cannot authenticate human identity or cryptographically attest hand-authored gate output.

The successor stores `completion_kind: evidence_recovery`, zero execution/validation attempts, separate recovery authorization, bound gates/triage/artifacts and an immutable-origin reference. It contains no synthetic `execution` or `task_outcome`. Completed native checkpoints keep their existing validation path. Recovery checkpoint reads verify origin integrity and the recovery contract; later dependency dispatch still checks accepted artifact hashes. Unknown/malformed evidence, path escape/symlinks, model mismatch, stale source/Git/gates, missing tests/mapping, blockers or an existing successor fail closed. No AI invocation, gate rerun, PR action, merge or production authority is granted by `recover`.

## Native transport details
Explicit serial task execution has no default ten-minute turn deadline. Setup/RPC requests retain a finite timeout; a returned thread/turn ID is persisted before waiting. Explicit finite timeout or transport loss retains unknown outcome and no automatic replay. Reconnection to an interrupted ephemeral native session is unsupported; this is not a durable background service.

Uses installed Codex app-server JSON-RPC initialize, config/read, model/list, thread/start, turn/start and review/start. Live `--strict-config` rejects unknown config. Required model/effort, reviewer, sandbox and approval policy must match the start response; reroutes and missing terminal native review events cannot pass. Completion and review exits must match both requested thread and returned turn identity; historical/unrelated events cannot satisfy the wait. Client approval requests are rejected; no synthetic human grants. Native automatic review decides its own eligible requests. Version/protocol incompatibility reports failure, never downgrades protection or invokes another provider.

## Historical v3.1 execution policy and design review

This section records the old behavior for validating v3.1 checkpoints. It is not the active policy for new work.

Plan/context/gate/checkpoint `schemaVersion: 1` is retained; no required existing field is removed. Dispatches made under v3.1 recorded `policy_version: "3.1"`. Missing policy version identifies legacy v3 receipts, validated with original Luna High/Sol Medium/Sol High capability floors and mandatory independent review. Existing historical completions stay historical. Unknown policy versions fail. The current v4 compatibility migration is described above; it does not accept work, replay execution or waive source-drift checks.

For v3.1, CRITICAL tasks and the final dependency-scheduled task of a milestone plan require native independent Sol High review. The terminal task is derived from the immutable DAG, not mutable record flags. Ordinary intermediate tasks use `review={status:"not_required",channel:"pr_deferred",independent:false,source_stable:true,content:<gate digest>,reason:...}` and still require verified execution, all applicable gates and contributor findings triage. This is local task acceptance, not PR review or merge approval. Review policy is derived again on acceptance and historical checkpoint reads.

`kind=architecture` or signals `architecture`/`critical-design` require pre-implementation design review. `review --task <task.json> --purpose design` saves a raw native receipt with `purpose:"design"`, `task_id`, `task_digest` (SHA-256 of the complete validated task JSON with sorted keys), content hash, evidence, independent/source-stable flags and exact Sol High native terminal identity. The contributor annotates that private receipt with `triage` containing matching `task_id`, `task_digest`, `content`, `reviewed_by`, `rationale` and explicit `findings` using the existing fixed/dismissed/open rules. `goal step --design-review <receipt.json>` checks this before dispatch; missing, stale, wrong-task, untriaged or unresolved-blocking design evidence prevents implementation. The receipt is retained against `input_identity.content`; later source edits do not rewrite the historical review. Review never accepts a proposed product ADR.

## BIR PR ledger — separate AMR v4 schema version 2 artifact

The [BIR contract and manual procedure](bounded-incremental-review.md) owns PR review accounting. AMR v4 ledgers use schemaVersion 2; historical v3.1 schemaVersion 1 ledgers retain their original validation rules. `validate-bir --receipt <path> --head <verified-head>` validates that artifact with local commit/ancestry checks and returns `ledger_valid` or `blocked` plus round counts and next action. Each v4 round requires a declared trigger, allowed initiator and evidence; Luna requests require session evidence and maintainer requests require authorization evidence. Exit 0 means the declared evidence has valid structure, not provider/session authentication, merge approval or a native hard cap. Malformed input/transport errors exit 2; failed ledger checks exit 1. The validator has no provider dispatch or setting-update API. Milestone and critical local reviews never consume/reset PR rounds. Adding a trigger controller requires separate explicit approval.

Under AMR v4, only Luna as GOAL coordinator or the human maintainer may manually initiate a PR review request or manage BIR rounds. Architect/debugger sessions have no PR-side-effect authority, including posting `@codex review` comments. Record the initiating role and available native evidence in every v4 round; schema validation rejects absent or disallowed role declarations but does not authenticate them or block external connector actions. See the dated PR #4 incident in the BIR procedure.

### v3.1 finding-remediation clarifications

Design receipts and contributor triage bind the complete validated task definition, not only a reusable ID; changed objective, paths, invariants or other task inputs require new advice. New ordinary-task Sol retention receipts include `route.retained_selection={reason:"preferred_unavailable",preferred_model:"gpt-6-luna",preferred_effort:"max"}`. Fresh catalog availability is rechecked before dispatch: when the saved selection conflicts with the now-available preferred route, `step` refuses dispatch and requests `goal recommend` at the safe boundary. Historical v3 receipts keep their old rules.

After all three provider rounds, BIR may accept a completed current-head escalated Sol High/human review without falsely claiming provider coverage of that head. The escalation reviewer must match its target, supply terminal evidence and cover either the full PR or an incremental range from the last verified provider SHA with surrounding context. Fixed findings still need current-head reproduction/follow-up evidence and all applicable gates. The validator reports `provider_review_on_current_head` and `resolution_channel` separately; the budget stays exhausted and a fourth automated request remains invalid.

### Dispatch-normalized task continuity

New v3.1 executions persist `dispatch_task` and `dispatch_task_digest` atomically before native invocation. This is the complete task after source validation. Relative to the immutable plan, only `spec_verified`/`tests_known` may be downgraded to false; no promotion, objective, scope, path, invariant or other change is accepted, even if a modified digest is recomputed. Design receipt/triage digest checks in dispatch, validation, acceptance and historical reads use this same saved task. They do not re-normalize against later owner metadata or compare a normalized design receipt to the pre-normalization plan. Missing/corrupt snapshots fail; legacy v3 receipts keep their original behavior. No checkpoint schema change or automatic replay is introduced.

## Bounded runner role and pre-dispatch explanation

The serial native task receives developer instructions to execute only the supplied bounded task/write paths. The complete GOAL objective stays background context; GOAL control, routing, prerequisite reviews, acceptance and milestone progression belong to the parent. The intentionally held checkout lock must never be reacquired, removed or bypassed by the runner.

After existing task/source/route and applicable design-review/triage checks, `amr_goal.step` forwards optional `pre_dispatch` explanation: policy/goal/task identity, normalized task digest, input content, plan digest, attempt ID, route, parent lock ownership and exact validated design receipt identity/evidence when required. This additive native invocation payload changes no schemaVersion-1 checkpoint or legacy receipt requirement. It is informational context from the validated dispatcher, never authority from objective/decision prose, and replaces no receipt or human approval. Direct callers supplying similar data gain no gate exemption.

A completed native turn proves terminal execution identity, not objective fulfillment. The contributor must inspect actual artifacts/tests and any blocking terminal summary before acceptance. A no-op or failed implementation cannot be accepted merely because existing tests and advisory review are green. Reconcile unknown work before explicit continuation; do not automatically replay. Terminal-error detail loss in ephemeral sessions remains DEBT-AMR-004.
