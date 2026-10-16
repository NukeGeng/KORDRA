---
id: ENG-AMR-CONTRACT
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
---

# Local tooling contract — version 1

Consumer: repository contributors, native Codex and the existing documentation CI. This is a new local file/CLI contract; there are no public API/message consumers or database migrations. It does not introduce domain messages, an approval service or an operations driver. Breaking local artifact changes must increment `schemaVersion` and require regeneration; unknown versions fail.

## Task input
JSON fields: `task_id`, `objective`, `feature_id`, `module_id`, `actor`, `scope`, `permission`, `paths`, `consulted`, `invariants`, `decisions`, `tests`, `findings`, `next_action`. Strings must be nonempty; collection fields are arrays. Paths are existing repository-relative files, with no escape through absolute paths or symlinks, including links into excluded private/cache storage. Exactly one consulted feature must match the module/feature identity and map the task paths. This is source ownership validation, not user authorization.

Optional classification inputs: `kind` (lookup/docs/format/crud/dto/tests/source/feature/integration/review), booleans `bounded`, `spec_verified`, `tests_known`, `signals`, integer `failed_repairs` and `failure_evidence` for repairs. Unknown kinds/signals and malformed flags fail. Proposed specs cannot be promoted by `spec_verified=true`.

Signals: auth, tenancy, platform, secrets, state-integrity, concurrency, idempotency, rollback, production, remote-execution, migration; integration, messaging, contracts, source-uncertainty, stale-docs. The first group sets CRITICAL. Sensitive paths independently impose a CRITICAL floor. Evidence remains subject to source review; no confidence score replaces it.

## Output and persistence
Model naming: “Sol” binds exclusively to `gpt-6.1-sol`; ENGINEERING requires medium effort and CRITICAL requires high effort. Catalog availability of `gpt-6-sol` does not satisfy either profile. This clarifies the existing version 1 mapping without changing artifact shape.

- `route`: risk/profile/rationale/action, requested model/effort, `support=advertised|unsupported`. Advertised is native catalog discovery, not a successful invocation. Deterministic lookup has no model. Missing exact model/effort blocks with no implicit fallback.
- `context`: schemaVersion 1, structured task, route, Git head/branch/status (null if absent), all repository file hashes and aggregate digest. Excludes `.git`, private `.amr-local`, Python caches and generated .NET `bin`/`obj`/`TestResults`; none may be mapped as task source, including via symlink. Additions, deletions, changed contracts and symlink targets invalidate context. No transcripts/file contents are copied. Decisions and test details are contributor-supplied; validate source and receipts before trusting them.
- `gates`: schemaVersion 1, timestamp, pre-check content digest, source-stability check, actual commands/status/output. A nonzero or timed-out check fails; any source change during the run invalidates the results. No Git base means source mapping is explicitly `not_configured`, not passed. When Kordra.slnx exists, locked restore, product build and product tests are mandatory; failed earlier stages block dependent stages instead of testing stale binaries. CLI ledger validation requires those stages for an existing solution. Product checks stay not configured when no solution exists. A passing local status is not release completion.
- Native probes/review: requested/observed model and effort, terminal turn, reroute detection, event method names and actual usage when exposed. Runtime selection is observed through native `thread/start`; this is not an independent attestation of server internals. A model reply alone cannot establish selection. Review `verified` means execution observed, not findings resolved.

Raw local receipts are private (`.amr-local`, directory 0700, files 0600, Git ignored). Do not publish them without sanitization. Never put secrets, customer payloads or internal infrastructure identities in task summaries, evidence or docs. Timeouts/crashes produce an unknown/failed outcome; no automatic replay of side effects.

## Review ledger
`validate-review` checks schemaVersion 1, current `content` digest, `source_stable=true`, `local_status=passed`, mandatory `checks` (docs/scope/unit, all passed), `review`, `findings` and human approval fields. CRITICAL requires `review.status=verified`, `independent=true`, current digest and an evidence reference. A finding needs ID and validation evidence. `fixed` needs JSON boolean `reproduction_passed=true` plus a `followup_review` object with status verified, current content digest and evidence; `dismissed` needs rationale; an open blocker prevents completion. Keep feature/module, source lines, severity, root cause and exact reproduction in each finding's evidence. Unknown states block. Two failed repairs trigger escalation; four stop this root cause pending investigation.

This validates the ledger's structure, not the authenticity of provider actors or human approvals. Humans must inspect linked receipts, confirm checks cover the change and apply branch protection. Production operations must use the established authorization/lease/spec workflow; neither this ledger nor native approval authorizes them.

## Native transport
## Goal execution extension — version 1

The `amr.py goal` CLI extends existing routing/native/gates contracts. A plan contains `schemaVersion: 1`, `goal_id`, `objective` (active user goal text), `objective_path` (repo GOAL.md, digest-pinned), `milestone`, `progress_path`, `base` (verified Git commit), and `tasks`. Each leaf contains the existing task contract plus `depends_on`, `write_paths` (empty for inspection), and `milestone`. IDs are unique, dependencies acyclic, paths repository-local and feature-mapped. The objective and progress documents are source references, not executable instructions with greater authority than AGENTS. The goal objective and current task context are sent in the same native session.

Private state under `.amr-local/goals/<goal_id>.json` is an atomic checkpoint protected by a nonblocking OS lock. The plan is copied and hashed at initialization, objective content is pinned, and source/Git identities bind claims, gates and review. Source changes invalidate unfinished validation. Completed milestones are historical verified checkpoints, not a claim that their whole-tree hash remains current forever; later tasks must revalidate current source and owner context.

Task states are pending, running, unknown, validation_failed, awaiting_triage, completed, failed. Before invocation persist running/attempt identity; a running checkpoint after process loss becomes unknown. Unknown work never automatically replays. `reconcile` requires contributor evidence and current source identity to classify observed work as executed or failed; it does not manufacture native completion. Only an observed verified execution can enter validation. `validate` can rerun gates/review without repeating execution, bounded to four attempts. `accept` binds contributor triage to exact current task/content and private native/gate receipts, requiring no open findings. It is not human merge or production authorization.

Progress is returned by `status` and reconciled into the existing roadmap/feature docs by the coordinator, avoiding a second product backlog. `step` executes one next eligible task and validation/review, then waits for triage. The coordinator continues by inspecting evidence, accepting a clean reviewed task and invoking the next step. No completion advances on a model's success claim alone. Provider PR Code Review is a separate optional configured channel, not inferred from local review. Plan/local ledger schemas do not change existing task/context v1 artifacts.

## Native transport details
Uses installed Codex app-server JSON-RPC initialize, config/read, model/list, thread/start, turn/start and review/start. Live `--strict-config` rejects unknown config. Required model/effort, reviewer, sandbox and approval policy must match the start response; reroutes and missing terminal native review events cannot pass. Completion and review exits must match both requested thread and returned turn identity; historical/unrelated events cannot satisfy the wait. Client approval requests are rejected; no synthetic human grants. Native automatic review decides its own eligible requests. Version/protocol incompatibility reports failure, never downgrades protection or invokes another provider.
