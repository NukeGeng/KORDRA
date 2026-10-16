---
id: ENG-AMR-GOAL
status: implemented
owner: engineering-maintainers
last_reviewed: 2026-10-16
---

# Goal execution through existing AMR

Owner: [FEAT-AMR-001](features/quality-first-amr.md), ENG-TOOLING. Local contributor use case: progress a dependency-ordered engineering goal while retaining source, selection, execution and review evidence. No tenant resource or production permission is introduced. The [contract](contract.md) defines local plan/checkpoint version 1.

## Three mechanisms

AGENTS/WORKFLOW are coordinator policy. Native collaboration tools actually spawn agents, using explicit model and effort with bounded context; their result may not expose authoritative model metadata. `amr.py goal` is executable serial orchestration using native app-server sessions, with observed model/effort and task/thread/turn receipts. A native session is not described as a collaboration sub-agent or a swap of the main chat. The coordinator can work directly when delegation would lose context, but must retain the same gates/review obligations.

The desktop's GOAL status is independent. Read the goal objective attachment, inspect native goal state and use the supported UI to resume if paused; no local command here can alter that state. Runtime activation must be reported from actual native state. The current user explicitly authorizes adaptive delegation for this goal and subsequent goal runs inherit the repository policy.

## Coordinator loop

1. Inspect GOAL.md, the existing roadmap, current Git/dirty state, accepted ADRs, owner feature and prior checkpoint. Identify milestone, module, feature, actor, permission, local scope, dependencies and acceptance evidence.
2. Register one plan extending existing milestone tracking. Include the active user goal in `objective`; `objective_path` pins GOAL.md; `progress_path` points to the owning roadmap. Each task reuses the AMR task contract, adds milestone, dependencies and exact existing writable paths. Empty write_paths means read-only inspection. New files must first be explicitly scaffolded and feature-mapped by the coordinator; workers cannot silently broaden their scope.
3. `goal init --plan <private-plan.json>` validates the DAG, owner mappings, local scope and Git base; freezes a private copy of the plan and objective hash. No arbitrary command template or shell interpolation is accepted.
4. `goal step --id <goal-id>` chooses one eligible task, resolves the exact supported model/effort, saves dispatch intent, invokes native execution, checks actual selection and allowed changes, runs all gates and independent native Code Review of uncommitted changes. Workers must not commit; changed Git HEAD/branch blocks validation, so task changes cannot disappear from review.
5. Inspect private checkpoint messages, findings and review output. Supply `goal accept --id <goal-id> --task-id <task-id> --triage <private-triage.json>` only after reproducing/fixing or evidence-backed dismissal. Triage has task_id, current content digest, reviewed_by, rationale and explicit findings array; it is contributor judgment, not human production/merge approval. No automatic text heuristic labels a review clean.
6. `goal status` projects progress. Update the existing roadmap and owner feature with sanitized evidence, then run gates/review again for that new content. Continue the next eligible task. When accepted architecture limits further implementation, record the exact decision required and prepare a reviewable proposal rather than inventing it.

Use ROUTINE gpt-6-luna/high only for bounded tasks with current implemented/accepted/verified owner specs and known tests; substantive engineering defaults gpt-6.1-sol/medium. Sensitive paths and architecture/state/security concerns require gpt-6.1-sol/high. Reclassification may increase effort; unavailable exact selection blocks. `.codex/config.toml` supplies native delegation defaults only when trusted/loaded. Explicit execution arguments remain authoritative, with start-response validation.

## Recovery and limits

One OS lock serializes writers across goal IDs in a checkout. Atomic fsynced private JSON preserves dispatch intent and receipts across sessions without a database/service dependency. This extends existing local receipts; it does not adopt a product workflow state machine, broker or technology. The tradeoff versus SQLite is simple single-writer checkpoints rather than concurrent task scheduling. Workers receive a sandbox and no network request authority; the adapter never grants human approvals. Declared write-path checking detects scope violations after execution; it is not a filesystem authorization boundary. Native workspace sandbox remains the boundary.

After a process exits while a task was running, `status` records unknown. Do not replay: inspect native identity and source, then `reconcile --evidence <explanation>`. Missing authoritative terminal evidence becomes failed; observed completed execution may proceed to validation without repeating the work. Failed work requires an explicit investigated new task/plan. `validate` reruns gates/review only, up to four attempts; it never repeats execution. Two failed repair attempts of a root cause require critical analysis; the executor already uses independent Sol High review, while the coordinator supplies root-cause evidence and an explicit repair task. No unbounded automatic repair loop exists.

Local native review proves local execution only. Provider PR Code Review requires the configured connector/repository and exact current PR head evidence. If absent, report requires-user-configuration; do not treat an empty review list as a completed review. Auto-review concerns eligible sandbox permission requests and remains separate from code review and human approval.

## Commands

```bash
python3 docs/tooling/amr.py discover
python3 docs/tooling/amr.py goal init --plan .amr-local/goal-plan.json
python3 docs/tooling/amr.py goal status --id KORDRA-FOUNDATION
python3 docs/tooling/amr.py goal step --id KORDRA-FOUNDATION
python3 docs/tooling/amr.py goal accept --id KORDRA-FOUNDATION --task-id F0-task --triage .amr-local/triage.json
python3 docs/tooling/amr.py goal validate --id KORDRA-FOUNDATION --task-id F0-task
python3 docs/tooling/amr.py gates --base origin/main
```

Plans and receipts stay private. The committed roadmap/feature is the product progress source; checkpoints are execution evidence, not a competing backlog. Never automatically merge, deploy, rollback, mutate infrastructure or access LAB-01 while executing this workflow.

The [safe example plan](goal.example.json) illustrates the input shape. Copy it into `.amr-local/goal-plan.json`, inspect the objective and use a fresh goal ID; its one-shot documentation marker must still be pending. Do not replay it after completion or replace a genuine product task with a smoke test.
