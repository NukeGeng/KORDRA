---
id: ENG-AMR-GUIDE
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-21
---

# AMR v4 — Architect / Developer / Reviewer

The product owner approved this policy for the existing [ENG-TOOLING](module.md) / [FEAT-AMR-001](features/quality-first-amr.md) implementation. It replaces the previous Sol-driven GOAL review loop without changing product architecture, accepted ADRs, tenant permissions or production approval. Correctness, architecture integrity, security, reliability and maintainability remain mandatory. AMR stays single-agent; deterministic CLI/CI performs executable checks.

## Roles

| Role | Selection | Responsibility |
|---|---|---|
| Architect | GPT-6.1 Sol High, `gpt-6.1-sol/high` | Produce a bounded design for milestones, features, boundaries, contracts, rules, acceptance and failure cases; stop for explicit human design approval. |
| Developer / GOAL coordinator | Luna Max, `gpt-6-luna/max` | Execute approved GOAL tasks, maintain tests/docs, run deterministic gates, commit and prepare a feature PR. |
| Default PR reviewer | Codex Auto PR Review | Review the feature PR after applicable CI; report current-head findings. |
| Escalation debugger | Sol Medium or High | After two evidenced unsuccessful Luna fixes, debug only the reported defect and affected contracts; Medium for non-critical issues, High for critical security, concurrency, tenant isolation, irreversible effects, state integrity or distributed recovery. |

The native model catalog must advertise the exact model/effort before dispatch. The repository's verified native catalog advertised Luna Max and GPT-6.1 Sol Medium/High; see [runtime evidence](testing-and-evidence.md). Configuration and catalog availability do not prove that a particular desktop turn used that model. Never silently substitute another model or effort.

## GOAL workflow

Use the existing [GOAL workflow](goal-execution.md) and local checkpoint. The normal path is: GOAL → bounded Sol High design when needed → explicit human design approval → Luna implementation → deterministic tests/gates → commit → feature PR → Codex Auto PR Review → fix or bounded escalation → human merge.

A new design session is unnecessary when accepted designs, ADRs and contracts fully cover the implementation. Critical risk signals alone do not reroute a Developer task to Sol. If implementation requires a new architectural decision, stop and create a bounded Architect task. Sol design and debugging sessions finish after their assigned task; neither owns continuous GOAL execution.

`goal validate`, `goal accept` and milestone transitions do not invoke AI reviewers under policy v4. They preserve execution receipts, deterministic gates, contributor triage, exact-content human design approval and no-replay recovery. A successful GOAL task does not certify its PR. The default PR review and its current-head evidence remain separate.

New dispatch receipts use `policy_version: "4"`; checkpoint schemaVersion 1 remains unchanged. Completed v3 and v3.1 receipts keep their original validation rules. An unfinished historical task can migrate only its review obligations to v4 while retaining its original execution receipt and model-selection rules. Interrupted/unknown tasks are reconciled; they are never replayed automatically.

```bash
python3 docs/tooling/amr.py discover
python3 docs/tooling/amr.py route --task docs/engineering/ai-orchestration/task.example.json
python3 docs/tooling/amr.py goal status --id <goal-id>
python3 docs/tooling/amr.py goal recommend --id <goal-id> \
  --current-model gpt-6-luna --current-effort max
python3 docs/tooling/amr.py goal step --id <goal-id> --context .amr-local/context.json
# Implement the prepared task in this chat, then run applicable deterministic gates.
python3 docs/tooling/amr.py goal finish --id <goal-id> --task-id <task-id> \
  --evidence .amr-local/completion.json
```

Architect acceptance requires a human approval object bound to the exact task digest, content digest, approver, scope and decision evidence. The CLI records that decision but cannot authenticate the person. `goal step --design-review` is rejected under v4; design approval happens after the Architect output. Proposed ADRs still require their own human decision.

## Deterministic gates

The current chat is the default executor for approved local Developer tasks. Preparation/completion invokes no AI session; an explicit `--native-runner` retains the serial runner and its `validate`/`accept` flow. For stopped/reconciled failed local Developer work, [inspected continuation](goal-execution.md#approval-boundaries) records current evidence under the existing approved scope without repeat design approval. It creates a labeled successor and retains the original failure. Historical explicit `goal recover` and all design/production/merge approval boundaries remain unchanged.

`gates` runs strict docs, LAB case-registry checks and offline tooling tests. With `Kordra.slnx`, it also runs locked restore, Release build and product tests; failed restore/build blocks dependent stages to avoid stale binaries. On a PR, supply a verified base SHA for source-to-feature mapping. CI remains independent of AI credentials.

```bash
python3 docs/tooling/amr.py gates --base <verified-base-SHA>
python3 docs/tooling/amr.py validate-review --receipt .amr-local/triage-ledger.json --critical
python3 docs/tooling/amr.py validate-bir --receipt .amr-local/pr-review.json --head <verified-PR-head-SHA>
```

Optional `gates --reuse <private-receipt> --validation-context <nonsecret-fixture-epoch>` reuses only successful matching input/environment/toolchain evidence within one hour of its original execution. Product integration-test reuse requires the same declared fixture epoch; rotate after fixture/service changes or unknown effects. Docs/scope/mapping always execute and docs changes also invalidate tooling tests. See the [reuse contract](contract.md#deterministic-evidence-reuse).

Formatting checks remain deterministic; run the repository's applicable formatter and whitespace checks. At two failed Luna repairs of one root cause, escalation requires a defect ID, reproduction evidence and affected paths; a fourth failed repair blocks for maintainer investigation. Critical defects route directly to Sol High only as a bounded debugging task. Blocking findings and failed applicable gates prevent completion.

## PR review and BIR

Codex Auto PR Review is the default independent reviewer after a feature PR and its applicable CI checks. One review is the default; allow at most three requests total per PR. Round 1 covers the full diff. Only request rounds 2–3 when there is a new head or an unresolved finding that needs follow-up; use the last verified reviewed SHA as the incremental base and include surrounding context. Group findings by category/root cause, track canonical IDs and never repost existing bugs. Do not request another review for an unchanged source tree or SHA. Blocking findings must be resolved before merge. Round exhaustion is never approval.

The [BIR protocol](bounded-incremental-review.md) uses a local deterministic ledger. The validator checks SHAs, Git ancestry, round accounting, finding deduplication and escalation; it does not call or stop the provider, authenticate provider evidence or impose a native hard cap. Manual enforcement remains necessary until a provider capability is separately verified. Native hard-cap/incremental enforcement is not claimed, and no review-trigger controller is added by this migration.

Manual trigger authority belongs only to Luna as GOAL coordinator or the human maintainer. Sol Architect and debugger sessions must not post/edit PR comments, invoke `@codex review` / `@codex security review`, or start/manage BIR rounds; invoking a reviewer is still a PR-side effect. Record the initiating role and available session-bound evidence at handoff. Repository instructions and the current ledger validator are manual safeguards, not a technical block on GitHub connector actions or provider auto-triggers.

To report a provider review as verified, inspect connected repository configuration, request/run identity, terminal completion and exact current PR head, then triage its findings. A local review, comment, empty findings array or ledger exit code alone is not provider execution evidence. The PR owner retains merge authority. See official [Code Review documentation](https://learn.chatgpt.com/docs/code-review).

## Checkpoints and human authority

Private checkpoints remain atomic under `.amr-local/goals`, retain immutable plan/objective/source/Git identities and protected write paths, and bind native execution to thread/turn. Native timeouts or crashes become unknown and are not replayed. Local selection claims remain unverified without runtime evidence bound to the turn. The local CLI cannot pause or resume the desktop GOAL.

Sandbox Auto-review and Codex Auto PR Review are distinct capabilities. Neither grants merge authority, company access or production approval. Sensitive production operations still require explicit authorized human approval, target lease, locked specification and rollback/evidence controls. LAB-01 remains observe-only by default. Live model/review probes are opt-in and separate from deterministic gates.
