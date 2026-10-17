---
id: ENG-AMR-GUIDE
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-16
---

# Kordra AMR v3 operating guide

Owner: [developer engineering tooling](module.md), [FEAT-AMR-001](features/quality-first-amr.md). The existing [AI review protocol](../../governance/ai-review-protocol.md) remains authoritative for source/docs reconciliation. Correctness, architecture, security, reliability and maintainability precede efficiency and tokens. This checkout had no previous AMR implementation; this extends `docs/tooling`, AGENTS, WORKFLOW and existing CI rather than adding a product service.

## Single-agent routing and context continuity
For persistent GOAL execution, use the [coordinator and executable workflow](goal-execution.md); `amr.py goal` extends the existing implementation with dependency scheduling, actual execution, current gates/review and private checkpoints. AMR v3 runs the coordinator in one session and does not spawn native collaboration sub-agents. `.codex/config.toml` disables sub-agent tools for new trusted project sessions; `amr.py discover` reports the effective setting. Repository instructions remain in force if project configuration is not loaded.

“Sol” means GPT-6.1 Sol (`gpt-6.1-sol`) throughout Kordra AMR. Medium and High select reasoning effort on that same model. If a required switch target is unavailable, stay only when the already selected, runtime-advertised Kordra profile meets the task's quality floor; otherwise block. This keeps the exact current selection and records it as unverified when supplied by the coordinator. GPT-6 Sol (`gpt-6-sol`) is never a fallback.

| Risk | Profile | Native model / effort | Evidence required |
|---|---|---|---|
| Mechanical lookup | deterministic | `rg` / enumeration | No source interpretation |
| ROUTINE | ROUTINE | gpt-6-luna / high when a switch is worth it | Bounded task, mapped owner, approved/current spec and known tests; stay on a capable current session when interruption costs more |
| STANDARD (default) | ENGINEERING | gpt-6.1-sol / medium | Source comprehension, feature/integration/messaging/contracts |
| CRITICAL | CRITICAL | gpt-6.1-sol / high | Auth, tenant/platform scope, secrets, state integrity, leases, retry side effects, rollback, production |

Reclassify when evidence changes. The default is GPT-6.1 Sol Medium. Recommend GPT-6.1 Sol High for security, tenant/platform scope, distributed concurrency, state integrity, recovery or significant production risk. A move from High back to Medium is considered only when at least two pending tasks are standard engineering and no critical task is queued. Do not pause for a routine DTO or CRUD task just because the model table names Luna High. There are no model-use quotas. Native Code Review selects its own review channel; independent review is mandatory for CRITICAL, and self-checking does not count. A dedicated native local reviewer is the available alternative when provider review cannot run.

### Human-in-the-loop model switching

At an eligible task boundary, supply the current model and effort to `goal recommend`. If the route justifies a switch, AMR stores `awaiting_model_switch` with the task, rationale, recommended selection, source/content hash, Git head/branch/index and next action. `goal step` refuses to dispatch while paused. The user changes the model in Codex and explicitly continues GOAL; the coordinator validates the checkpoint before proceeding.

```bash
python3 docs/tooling/amr.py goal status --id KORDRA-FOUNDATION
python3 docs/tooling/amr.py goal recommend --id KORDRA-FOUNDATION \
  --current-model gpt-6.1-sol --current-effort medium
# After manually switching the model in Codex:
python3 docs/tooling/amr.py goal resume --id KORDRA-FOUNDATION \
  --model gpt-6.1-sol --effort high
python3 docs/tooling/amr.py goal status --id KORDRA-FOUNDATION
```

The current model passed to the local CLI is caller-reported. `goal resume` validates it against the recommendation and current model catalog, but marks it `unverified`; the local CLI cannot inspect the active desktop coordinator session. Do not treat `config/read`, a model name typed by a caller, or local checkpoint resume as proof of the model active in the chat. The serial `goal step` runner independently verifies its own `thread/start` model/effort and terminal turn; this is the only execution path that can produce a task-bound receipt for checkpoint acceptance. Direct coordinator edits remain unverified and cannot advance a task to completed.

Local status values are `active`, `awaiting_model_switch`, `awaiting_approval`, `blocked` and `completed`. Approval evidence is a reference for human review, not an approval token. A blocked/unknown operation cannot replay automatically. The local CLI cannot control the desktop GOAL status; when an active native GOAL exists and the runtime exposes pause, the coordinator pauses it only after saving the local checkpoint. Otherwise it stops and reports the limitation. Resume always requires an explicit user action.

```bash
python3 docs/tooling/amr.py discover
python3 docs/tooling/amr.py route --task docs/engineering/ai-orchestration/task.example.json
python3 docs/tooling/amr.py context --task docs/engineering/ai-orchestration/task.example.json
python3 docs/tooling/amr.py validate-context
python3 docs/tooling/amr.py launch --task docs/engineering/ai-orchestration/task.example.json --dry-run
# Omit --dry-run to open the native interactive session with model/effort set.
python3 docs/tooling/amr.py gates
python3 docs/tooling/amr.py review
```

Launch is local development only, uses workspace-write plus human on-request approvals and never provides unrestricted access flags. Inspect effective runtime: project configuration may be ignored in an untrusted/non-Git folder or overridden by the desktop chat. Config files cannot switch this active chat. The explicit launch arguments select the model in the newly launched native session. Resume an existing native session when possible instead of repeatedly launching; context validation must precede reliance on its decisions/tests. All file or contract changes invalidate a saved handoff/review; regenerate after checking the change. JSON artifacts and exit codes are specified in the [contract](contract.md).

## Quality gates and escalation
`gates` executes strict docs validation, the existing LAB registry scope checker and offline AMR unit/contract/regression tests. When Kordra.slnx exists it also executes locked restore, product build and product tests; failed earlier product stages block dependent stages. Python imports in unit tests also catch syntax errors. On a real Git checkout use `gates --base <verified-base-SHA>` to execute changeguard against the PR range. Existing CI enforces these gates and source/feature changes together, including tooling/config/tests and product tests/bootstrap files. Python CI uses 3.11 and no AI credentials; the separate product validation workflow uses the pinned .NET SDK.

The initial supplied tree had no product build/database/broker. FEAT-ART-001/I1 now has a module-local domain build/test solution; no backend, database, broker or authorization service exists. Do not fabricate tenant, durability or deployment results. Further product increments must add their applicable static/integration/security/contract/failure/rollback checks. Root AGENTS negative test obligations remain in force.

On failure retain command, output, affected contract and root cause. Make a focused repair. At two failed repairs of the same cause, escalate; at four stop and request investigation. Security/state/irreversible risks escalate immediately. Never delete tests or label a changed file a fixed finding without rerunning the reproduction.

## Native review and human approval
Default project configuration selects a human reviewer. Official [Auto-review guidance](https://developers.openai.com/codex/auto-review) requires an eligible approval policy and sandbox. If organization policy permits local Auto-review, select **Approve for me** in the desktop permissions control and confirm it is selected for that chat; for CLI use `--approve-for-me` or eligible on-request + `approvals_reviewer="auto_review"`. Preserve project `auto_review.extra_policy`, which supplements the built-in policy and excludes sensitive operations. Managed requirements take precedence. Do not enable Auto-review for production operations. Native review cannot authorize company resource access.

```bash
python3 docs/tooling/amr.py probe-models
python3 docs/tooling/amr.py probe-auto-review
```

These run actual native turns, not mocks. Auto-review only requests a disposable local marker in a read-only sandbox; it records the native terminal decision. It does not change this chat's permission mode or enable Auto-review globally. The supplied chat has full access/never approvals, so it has no sandbox approval requests for Auto-review to decide. [Runtime evidence](testing-and-evidence.md) separates this from the isolated probe.

For GitHub, configure the Codex connector's repository access and enable automatic Code Review in repository review settings. On a representative PR verify the review's actor, run identity, completion and exact current head SHA; triage findings, fix and rerun checks, then observe/request the next review. `@codex review` is a supported manual trigger when connected. A comment alone, an empty findings list or a failed integration is not proof of completed review. See official [Code Review](https://developers.openai.com/codex/code-review) and [custom repository rules](https://developers.openai.com/blog/custom-code-review-rules-for-codex). Do not change remote settings or post comments until the provider/PR is identified and that action is authorized.

For GitLab, establish the actual supported MR integration, account eligibility and documented trigger before enabling it; GitHub parity is unverified here. At initial inspection the folder had no Git metadata/remote, so provider support could not be verified. The local Code Review diagnostics connector is read-only and requires a real PR/MR identity; it cannot enable automatic review. `amr.py review` calls native `review/start` for a content snapshot and requires a native exit event. It is independent local review, with a separate status from provider automatic review.

Git setup follow-up established NukeGeng/KORDRA on GitHub. For product changes, native `codex exec review --uncommitted` now provides local diff review; FEAT-ART-001/I1 observed terminal completion with no actionable defects. The earlier `amr.py review` custom target is scoped to the AMR implementation and must not be mistaken for product-feature review. Provider automatic review still requires its own current-PR evidence.

## Finding triage
Record review channel, content hash/head SHA, finding ID, severity, feature/module, source/test line, claim and evidence. Validate against the current revision. Reproduce valid findings; fix the owning module and rerun regression/relevant gates. Dismiss only with counterevidence and rationale. Unknown outcomes remain unresolved. Re-review changes; bound repair/review cycles using the same root cause. Stale/duplicate reviews never satisfy the current gate. `validate-review --receipt <local-ledger.json> --critical` validates the [ledger contract](contract.md); it cannot authenticate an approval or waive a missing required check. CI, branch protection, authorized human review and production approval retain their authority.
