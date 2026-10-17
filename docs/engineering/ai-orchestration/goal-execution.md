---
id: ENG-AMR-GOAL
status: implemented
owner: engineering-maintainers
last_reviewed: 2026-10-17
---

# Goal execution through AMR v3

Owner: [FEAT-AMR-001](features/quality-first-amr.md), ENG-TOOLING. AMR v3 uses one coordinator session, makes risk-based model recommendations, and preserves the existing local checkpoint, execution, gates and review adapters. It does not create collaboration sub-agents. No tenant resource or production permission is introduced.

## Session and routing policy

The default substantive engineering model is GPT-6.1 Sol Medium (`gpt-6.1-sol/medium`). Use GPT-6.1 Sol High (`gpt-6.1-sol/high`) for security, tenant/platform scope, distributed concurrency, consistency, recovery, state integrity and significant production risk. ROUTINE remains deterministic where possible; Luna High is recommended only for isolated low-risk work when the switch is worth its cost. Never switch for a simple DTO/CRUD task while a capable session is active. There are no model-use quotas.

Only consider a High-to-Medium switch at a safe boundary when at least two pending tasks are standard engineering and no critical task is queued. Resolve every proposed switch target against the native catalog. If it is unavailable, continue only when the already selected, runtime-advertised Kordra profile meets the task's quality floor: Sol Medium or High for routine work, Sol High for standard work, and Sol High for critical work. This retains the exact current selection; it is not a model or effort substitution. Otherwise block. A local CLI argument describing the current desktop selection is recorded as caller-reported and unverified. A model/effort observed from a native `thread/start` and matching terminal turn proves that native task session only, not the desktop coordinator session.

## Coordinator loop

1. Read GOAL.md, roadmap, Git/working-tree state, accepted ADRs, owner feature, tests and the prior checkpoint. Identify milestone, module, feature, actor, permission, local scope, dependencies and acceptance evidence.
2. Register one plan extending existing milestone tracking. Include the active objective, pinned `GOAL.md`, milestone, progress path and verified Git base. Tasks reuse the AMR task contract with dependencies and exact existing writable paths. New files must first be scaffolded and feature-mapped by the coordinator.
3. Initialize and inspect the private local checkpoint. At each task boundary, let the single coordinator classify the next task and run `goal recommend` with its current selection. If no switch is recommended, continue without pausing. If a switch is justified, AMR persists `awaiting_model_switch`, shows the recommended model/effort and reason, and blocks `goal step`.
4. When the native Codex Goal runtime exposes pause for the active GOAL, pause it after the local checkpoint is durable. Otherwise stop and report that native pause was unavailable. The user changes the model and explicitly resumes GOAL. The coordinator reads the local checkpoint, rechecks source, Git, documentation, task and unresolved operations, then records the manual selection through `goal resume`. The local command labels this selection unverified; the coordinator must not claim runtime verification without native evidence.
5. The coordinator remains in its active session as the sole planner and owner of context, gates, review triage and progress. For a task to enter checkpoint validation or acceptance, execute its source changes serially through `goal step`; that runner binds the task to the requested and observed model/effort, native thread/turn and terminal result. It is never auto-started by a recommendation. Direct coordinator edits lack a verifiable execution receipt and cannot be accepted as completed AMR work; do not synthesize a receipt from prose. If the runner is unavailable, leave the task pending or blocked and report the limitation.
6. Run `goal validate` for gates and independent review. Inspect current review output, reproduce/fix or evidence-dismiss findings, and submit exact-content contributor triage via `goal accept`. Repairs may change only the task's declared `write_paths`; other source drift blocks the checkpoint. This is not merge approval or production authorization. Reconcile verified progress into the existing roadmap/feature, then run applicable gates again.

## Local checkpoint states and commands

Private atomic state remains under `.amr-local/goals/<goal_id>.json`; it is an extension of the existing AMR checkpoint, not a second backlog or product state machine. New control states are:

| State | Meaning | Resume condition |
|---|---|---|
| `active` | Work may continue | A valid next task exists |
| `awaiting_model_switch` | The next task justifies a model/effort change | User explicitly switches and supplies the exact recommended selection; source/Git/task identity must still match |
| `awaiting_approval` | A human decision or review triage is required | Evidence reference is recorded; it does not authenticate or grant permission |
| `blocked` | A technical or uncertain execution state prevents progress | Evidence resolves the block; failed/unknown/untriaged tasks still require reconciliation or a fresh plan |
| `completed` | All planned tasks have verified execution, gates, review and triage | Terminal |

```bash
python3 docs/tooling/amr.py goal init --plan .amr-local/goal-plan.json
python3 docs/tooling/amr.py goal status --id KORDRA-FOUNDATION
python3 docs/tooling/amr.py goal recommend --id KORDRA-FOUNDATION \
  --current-model gpt-6.1-sol --current-effort medium
# Change model manually in Codex, then explicitly continue GOAL.
python3 docs/tooling/amr.py goal resume --id KORDRA-FOUNDATION \
  --model gpt-6.1-sol --effort high
python3 docs/tooling/amr.py goal step --id KORDRA-FOUNDATION
python3 docs/tooling/amr.py goal validate --id KORDRA-FOUNDATION --task-id F0-task
python3 docs/tooling/amr.py goal accept --id KORDRA-FOUNDATION --task-id F0-task \
  --triage .amr-local/triage.json
python3 docs/tooling/amr.py gates --base <verified-base-SHA>
```

`goal pause --status awaiting_approval|blocked --reason ...` records those distinct local states. Resume from `awaiting_approval` requires an evidence reference; resume from `blocked` requires resolution evidence. The local CLI has no connection to desktop GOAL controls. Local pause is not proof that the native GOAL itself paused, and `goal resume` does not resume that native GOAL. Use the Codex native runtime when available and require an explicit user continue action.

## Recovery, validation and approval boundaries

Checkpoints are mode 0700/0600, atomically written and protected by a nonblocking OS lock. The plan and objective are hashed. Model-switch pause stores content digest, Git head/branch/status/index, current task, route, completed/pending task records and next action. Resume rejects changed source, Git state, plan, task order, missing exact model/effort and unresolved task operations. Checkpoint reads validate the pinned plan without requiring every historical task input to remain present; dispatch revalidates all live inputs. The execution snapshot hashes all non-writable files so later validation rejects out-of-scope drift while allowing declared-path repairs. It never automatically replays.

A process lost while `running` becomes `unknown` and the goal becomes `blocked`. Inspect native identity and source, then `goal reconcile --evidence ...`; prose cannot invent a successful execution receipt. `goal validate` reruns gates/review only. New source/owner-context changes invalidate the task and require revalidation or a fresh plan.

Native local review and provider PR Code Review remain separate channels. Provider review requires a configured integration plus terminal evidence on the exact current PR head. Auto-review only decides eligible sandbox permission requests. Neither can authorize company resources, merge a PR or approve production work. Human approval, target lease, immutable spec and rollback/evidence requirements remain explicit for sensitive operations. LAB-01 restrictions remain unchanged.
