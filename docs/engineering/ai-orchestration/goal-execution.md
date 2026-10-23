---
id: ENG-AMR-GOAL
status: implemented
owner: engineering-maintainers
last_reviewed: 2026-10-22
---

# Goal execution through AMR v4

Owner: [FEAT-AMR-001](features/quality-first-amr.md), ENG-TOOLING. The existing single-agent GOAL coordinator remains the only owner of continuous execution. AMR v4 separates bounded design, implementation, deterministic validation, PR review and defect escalation. It does not create collaboration sub-agents, product services, tenant permissions or production authority.

## Roles and routing

| Task role | Route | Boundary |
|---|---|---|
| Architect | `gpt-6.1-sol/high` | Design-only Markdown paths. Produces the requested bounded design, then returns. It does not implement, run routine tests, review a PR, post/edit PR comments, or initiate/manage BIR triggers. |
| Developer | `gpt-6-luna/max` | Owns normal GOAL work, source changes, tests, docs, gates, commits and feature PR preparation. Critical risk labels do not alone change the model route. |
| Escalation debugger | `gpt-6.1-sol/medium` for non-critical; `gpt-6.1-sol/high` for critical | Requires at least two recorded failed Luna repairs and a defect ID, summary, reproducible evidence and affected paths. Work remains limited to that defect and its contracts; it does not post/edit PR comments or initiate/manage BIR triggers. Four failed attempts block for maintainer investigation. |
| PR Reviewer | Codex Auto PR Review | Default independent PR review after applicable CI. Review rounds are controlled by the separate BIR ledger. |

`kind=architecture` or an explicit Architect role routes to Sol High and accepts only bounded Markdown task paths. An Architect task reaches `awaiting_triage` after its one native turn. Completion requires exact-content human approval containing the task ID/digest, content digest, approver, scope and decision evidence. Approval does not accept an ADR unless the decision record itself is explicitly approved through its governance process.

Implementation may skip a new design session when accepted design documents, the owner feature and accepted ADR/contracts fully cover it. Developer instructions prohibit silent changes to module boundaries, public contracts, permissions and state machines. If the design basis is incomplete, stop and create an Architect task or record `[NEEDS-DECISION]` with the owning document.

`goal validate`, `goal accept` and milestone transitions never invoke a reviewer under v4. They run deterministic checks and require current-content contributor triage. They do not claim an independent source review, PR review, merge or production approval. Critical implementation changes use the default independent PR review and blocking-finding gates; Sol is not triggered automatically by a PR finding or exhausted review budget.

Only Luna as the GOAL coordinator or the human maintainer may initiate a manual GitHub PR review request or manage BIR rounds. A Sol session must return its bounded design/debugging deliverable without external GitHub PR-thread or review-trigger actions; scoped debugger source edits remain governed by that defect task. GitHub may attribute a trigger comment to the connected account rather than the originating model session, so the handoff ledger must record the initiating role and available native evidence; this is a manual guard, not provider-enforced access control.

## GOAL coordinator flow

1. Read `GOAL.md`, the current roadmap, Git/working-tree state, accepted ADRs, owning feature, relevant contracts/tests and any existing local checkpoint. Identify the feature/module, actor, permission, scope, dependencies, writable paths and acceptance evidence.
2. Create or extend one dependency-ordered plan for the actual milestone. Pin the active objective, objective/progress files and verified Git base. Existing inputs must use actual feature mappings. For files a task must create or update, list exact `output_paths` and predeclare them under `planned_source_paths`, `planned_test_paths` or `planned_doc_paths` in the owner feature; the task may write them only through `write_paths`. The single matching owner feature may also be a declared output when the task needs to add mappings after creating source/tests; other input/output overlap is rejected. Do not scaffold source merely to make a plan validate or accept a proposed architecture.
3. If an architectural decision is missing, dispatch one bounded Architect task. Inspect its output and obtain exact-content human design approval before making a dependent implementation task eligible. A previously approved design may be cited in the task's consulted documents/decisions.
4. Luna Max executes approved local Developer work in the current chat. `goal step --context <private-json>` prepares one task without AI dispatch; after implementation and applicable gates, `goal finish --evidence <private-json>` records exact-content contributor evidence. Repairs may change only declared `write_paths`. Repeating step returns the existing prepared task without resetting it. Explicit `--native-runner` uses the existing serial native execution plus `goal validate`/`goal accept`. No task auto-start follows a recommendation.
5. At a safe boundary, `goal recommend` reports the exact required model. The user changes the active Codex selection and explicitly resumes when needed. A caller-reported selection is unverified. Missing exact model/effort blocks; there is no silent substitution.
6. Luna completes eligible tasks, updates feature/roadmap/change evidence, runs full applicable gates, creates commits and prepares a feature-scoped PR. After applicable CI, Codex Auto PR Review runs once by default. Resolve blocking findings; use BIR for any follow-up request. A difficult non-critical finding may become a bounded Sol Medium debugger task after two failed Luna fixes. Critical findings may become a bounded Sol High debugger task. The maintainer decides merge.

## Checkpoint and recovery contract

When completion fails because source, scope or Git identity changed, inspect `goal status` diagnostics once. Do not rerun tests to repair a hash mismatch. For stopped work, use `goal reconcile --id <goal> --task-id <task> --evidence .amr-local/reconciliation-evidence.json`; it snapshots the exact old checkpoint and records the interrupted result without invoking a model. Include known provider findings in the private evidence's `findings` array. Historical checkpoints lacking a protected-file manifest cannot prove exact outside-write attribution; retain that limitation. If scope changed, prepare a fresh bounded owner-mapped inspection/repair task under existing accepted design. Use existing continuation/recovery only when their own scope, evidence and blocker checks pass; no task replay or repeat architecture approval is implied by bookkeeping.

Private state remains `.amr-local/goals/<goal_id>.json`, atomically written under a nonblocking checkout lock. Plan/checkpoint schemaVersion 1 is retained. Current-session preparation saves scope and input identities before editing; `in_progress` survives status/restart without claiming a lost native execution. One persisted coordinator task owns the checkout between CLI calls. Explicit native dispatch intent is saved before invocation; native execution receipts bind requested/observed model and effort, task, thread, turn and terminal outcome. Unknown work never replays automatically. `goal reconcile` records observed evidence but cannot synthesize successful execution. Validation retries are bounded and rerun checks without rerunning task side effects.

New dispatch records use `policy_version: "4"`. Completed v3/v3.1 records remain readable under the exact review and model rules that validated them. For an unfinished historical task, `goal validate` or `goal accept` may record `review_policy_version: "4"` while keeping `policy_version`, route and execution receipt intact; it removes the repeated task-level AI-review obligation and leaves provider PR review separate. This compatibility migration does not accept failed work, change source, replay execution or waive gates. Unknown versions fail. Tasks with missing objective identity or changed pinned content need a fresh reconciled plan.

`goal step --design-review <receipt>` is rejected under v4. New design approval is recorded only after Architect output through the exact human approval fields in contributor triage. Historical v3/v3.1 design-review receipts remain part of historical completed-checkpoint validation.

```bash
python3 docs/tooling/amr.py discover
python3 docs/tooling/amr.py goal status --id <goal-id>
python3 docs/tooling/amr.py goal recommend --id <goal-id> \
  --current-model gpt-6-luna --current-effort max
python3 docs/tooling/amr.py goal step --id <goal-id> --context .amr-local/context.json
# Implement only the prepared write paths in this chat; run applicable gates.
python3 docs/tooling/amr.py goal finish --id <goal-id> --task-id <task-id> \
  --evidence .amr-local/completion.json
python3 docs/tooling/amr.py gates --base <verified-base-SHA>
python3 docs/tooling/amr.py validate-bir --receipt .amr-local/pr-review.json \
  --head <verified-PR-head-SHA>
```

The native task runner receives the overall GOAL objective only as background, never as authority to manage GOAL controls or launch another task. It may not invoke native review, provider review or nested agents. The parent owns lock, routing, acceptance, milestone progression and all protected operations. See the [tooling contract](contract.md).

## Approval boundaries

Approved current-session Developer work uses the [completion contract](contract.md#current-session-developer-completion). A failed **single-task v4 local Developer** attempt may use [inspected local continuation](contract.md#inspected-local-continuation) after execution has stopped and reconciliation marked it failed. Cite the existing approved scope; inspect retained source and all outputs, actual model context, successful current gates and known findings. This records already-authorized repository-only work, so no repeat business/design approval is required merely for checkpoint bookkeeping.

```bash
python3 docs/tooling/amr.py goal continue --id <failed-goal> --task-id <task-id> \
  --as-goal <new-successor-goal> --evidence .amr-local/continuation.json
python3 docs/tooling/amr.py goal status --id <new-successor-goal>
```

The successor uses `completion_kind: coordinator_continuation`; original failed checkpoints remain byte-identical. It invokes neither AI nor gates and must receive valid current evidence. Running/unknown operations, uninspected or external effects, new architecture decisions, Architect/debugger/legacy/multi-task recovery are excluded. A disconnected runner must first be stopped and reconciled; never replay it. Historical human-authorized `goal recover` schemaVersion 1 remains unchanged, including explicit task-local model exceptions. No existing checkpoint is auto-migrated or promoted by installing this change.

The optional native runner saves its returned thread and turn IDs before waiting and has no default ten-minute turn deadline. RPC setup calls still time out. Explicit finite timeouts/transport loss retain unknown outcomes and never replay work. Automatic reconnection to an interrupted ephemeral native session is unsupported; inspect/reconcile instead. This is not a durable background execution service.

Use [explicit deterministic gate reuse](contract.md#deterministic-evidence-reuse) only for matching, fresh inputs. No successful suite is rerun merely to update a private checkpoint; documentation/scope/mapping still validate the current tree. A mixed working tree is handled by scoped, dependency-aware staging; unfinished unrelated features must not be swept into a commit.

Local design approval means a human explicitly reviewed one exact design artifact; the CLI cannot authenticate approver identity. Codex Auto PR Review is advisory and provider evidence must match the exact current PR SHA. The local BIR validator does not call the provider or enforce its round cap. Only the repository owner/authorized maintainer merges. Sensitive production operations still require the established explicit human approval, target lease, immutable spec, rollback and audit evidence. Neither model, review channel, local checkpoint nor CI grants company or cluster access. LAB-01 remains observe-only by default.

Native Goal pause/resume is controlled by Codex, not the local CLI. A local model-switch record never proves the active desktop thread changed or resumed. If an expected native capability is unavailable, report that limitation and keep execution evidence tied to the native task session actually observed.
