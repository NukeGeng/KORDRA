# WORKFLOW — Change lifecycle and documentation synchronization

```text
Idea / Bug / Incident
  → Register & triage (feature/module/owner)
  → Read accepted architecture + contracts
  → Impact analysis (actors, tenants, messages, data, integrations)
  → ADR if architectural change
  → Update feature spec + tests acceptance BEFORE implementation
  → Implement in owning module + automated tests
  → Update docs/traceability/debt/runbooks in same PR
  → CI docs checks + tests + security review
  → Human review + merge
  → Deploy/verify/incident learning
```

## Mandatory change request checklist
- [ ] One owner module, `feature_id` and requirement identified (or created).
- [ ] Actor/permission/scope/tenant isolation reviewed; super-admin and cross-tenant path checked.
- [ ] Command/event/API/data schema and backward compatibility reviewed.
- [ ] Durable operations and failure paths reviewed if external side effects.
- [ ] Docs updated at source-of-truth location; no duplicate stale pages.
- [ ] Positive, negative, retry, recovery tests passed (provide logs/evidence links).
- [ ] New technical debt logged with owner/severity/exit criterion; no silent TODO.

## PR policy
Use `.github/pull_request_template.md`. Reviewer checks the docs-to-code mapping and baseline drift. Emergency hotfix can merge through designated approval with follow-up record, owner, deadline, and incident link; cannot permanently skip documentation.

The owner's solo-maintainer merge policy requires a PR, both strict CI checks and an owner-only update rule for `main`, with zero mandatory GitHub approvals. An explicit owner merge instruction supplies human merge authorization; a second approving reviewer is not universally required. Risk-based independent review and sensitive production approval remain separate requirements. See the dated [repository policy evidence](docs/engineering/status-evidence-policy.md).

## Definition of complete
`docs/engineering/definition-of-done.md`.

## AI-assisted engineering and native review
Follow the single-agent [AMR v4 workflow](docs/engineering/ai-orchestration/goal-execution.md): GOAL → bounded Sol High Architect design when needed → exact-content human design approval → Luna Max implementation → deterministic tests/gates → commit → feature PR → Codex Auto PR Review → fix or bounded debugger escalation → human merge. Inspect `amr.py goal status --id <goal-id>` at session start. Keep checkpoints, execution receipts and no automatic replay.

Sol High (`gpt-6.1-sol/high`) designs and returns; it does not own continuous GOAL execution, routine validation or default PR review. Architect and debugger sessions must not post/edit GitHub PR comments, invoke `@codex review` / `@codex security review`, or start/manage BIR rounds. Those are PR side effects even when Codex performs the review. Luna Max (`gpt-6-luna/max`) is the Developer and GOAL coordinator for approved designs, source, tests, ordinary fixes and docs. Critical risk signals alone do not switch implementation to Sol. If an accepted design/contract does not cover a needed architecture change, stop for a bounded Architect task. Proposed ADRs still require their own human decision.

For approved local Developer tasks, `goal step --context .amr-local/context.json` prepares the current chat's scope and `goal finish --evidence .amr-local/completion.json` records its completed work. These commands do not invoke another model. The optional `goal step --native-runner` retains the serial runner for explicitly scoped work; its native execution still uses `validate`/`accept`. Current-session completion is labeled separately and must not fabricate native terminal execution. See the [completion and continuation contracts](docs/engineering/ai-orchestration/contract.md#current-session-developer-completion).

A stopped/reconciled failed local Developer attempt can use inspected `goal continue` evidence under its already-approved repository-only scope, retaining the failed checkpoint. Repeated recovery approval is unnecessary for that narrow path. Unknown/external operations, design decisions, sensitive production operations and human merge retain their existing gates. Run targeted checks during implementation and required full gates at validation/PR boundaries. Explicit evidence reuse checks input/environment/toolchain hashes, original freshness and fixture epoch; docs/scope/mapping always run. A mixed worktree requires scoped dependency-aware staging, not another architecture approval or a combined commit of unfinished features.

After at least two evidenced failed Luna repairs, a bounded reported defect may go to Sol Medium for non-critical debugging or Sol High for critical security, concurrency, tenant isolation, irreversible effects, state integrity or distributed recovery. Provide a finding ID, reproduction, evidence and affected paths; do not expand the task into GOAL or PR review. Four failed repairs stop for maintainer investigation.

Codex Auto PR Review is the default independent PR reviewer after applicable CI. Only Luna as GOAL coordinator or the human maintainer may initiate a manual PR review request or manage BIR rounds. Apply the [BIR protocol](docs/engineering/ai-orchestration/bounded-incremental-review.md): one review by default, at most three automated requests total per PR, round 1 full diff, rounds 2–3 incremental from the last verified SHA with surrounding context. AMR v4 receipts use BIR schemaVersion 2 and require a declared trigger, allowed initiator and evidence on every round; Luna requests include session evidence and maintainer requests include authorization evidence. Validation rejects missing/disallowed declarations but cannot authenticate them or control the provider. Do not review an unchanged source tree/SHA or repost known bugs. Keep canonical unresolved findings and stop after round 3. Escalate to the human maintainer; Sol is never triggered automatically by PR findings or budget exhaustion. Blocking findings prevent merge. Native hard-cap enforcement is unverified; maintainers control triggers manually.

`goal validate`, `goal accept` and milestone transitions do not invoke AI reviewers under v4. Record exact requested/observed model/effort, source identity, real gate commands/results and provider run/head SHA separately. Configuration, catalog discovery and a typed model name are not execution evidence. A provider review is verified only after terminal completion on the current PR revision. Task acceptance is not PR/merge approval. Missing infrastructure or integration is a limitation, never a passing gate. Sensitive production operations still require explicit authorized human approval.

## Source architecture governance (v1.2 proposal)
The owner's scoped [ADR-0013](docs/decisions/0013-clients-git-submodule.md) is the accepted exception for Git ownership of `clients/`. Use the [Product/Clients workflow](docs/engineering/clients-development.md): independent repository/branch/PR/CI, canonical Product API contracts, then a Product integration PR that pins a remotely available Clients commit. Never update/reset an active frontend submodule as part of ordinary Product validation. This accepts neither broad ADR-0009 nor a UI stack and creates no shared checkpoints or additional AI reviewer.

Before creating a project/module/host, identify the owner and current Git/source state, then compare the proposed [repository](docs/architecture/repository-structure.md), [solution](docs/architecture/solution-architecture.md), [module](docs/architecture/module-internal-structure.md), [dependency](docs/architecture/dependency-rules.md) and [host](docs/architecture/host-composition.md) specifications. Do not treat them as accepted architecture.

Use [ADR-0009](docs/decisions/0009-source-architecture.md) for review of the proposed source baseline. ADR-0008 remains the accepted scoped .NET/test bootstrap and is not replaced by this proposal. Any large source move, host/trust-boundary change or new dependency waits for explicit decision. Implement approved structure incrementally, update source/test mappings with the feature docs, and distinguish real architecture-test/CI execution from the proposed [test rules](docs/engineering/architecture-test-rules.md).

## OSS release scope and evidence synchronization
1. Before each milestone, identify whether the change is Foundation, private Operator Pilot, OSS Beta, production-readiness, Enterprise or Cloud, and check its accepted feature/ADR authority.
2. Keep product scope and execution evidence separate: proposed features and A–F example technologies are not implemented capabilities; CI success on one module does not prove application availability.
3. After PR merge, verify exact GitHub PR/review/merge SHA and CI run before reconciling roadmap, owning feature, debt and traceability. Do not infer approval from a merged state alone.
4. Any public LAB-01 inventory disclosure, OSS license and security policy publication requires rights-owner review; do not delegate such decisions to AI.
5. A private Operator Pilot must be observe-only on shared targets by default; any change must have real rights-owner permission, target preflight, approved spec, durable state, failure/rollback and audit evidence. HPA only scales Pods, not Talos workers.
6. Do not create dependencies on AMR for ordinary product builds or CI; AI orchestration remains optional.

## Additional workflow for reference-environment experiments
1. Register case in [LAB-01 coverage matrix](docs/reference-environments/lab-01/experiments/coverage-matrix.csv).
2. Confirm [safety mode](docs/reference-environments/lab-01/experiments/safety-and-change-control.md), target identity, ACL, and approved data policy.
3. Save preflight + observed state (redacted) and immutable spec; run only within permitted phase.
4. Record actual status and evidence via [template](docs/reference-environments/lab-01/experiments/evidence-template.md).
5. Map results to module feature/contract, issue/debt, and drift entry. Failed test never silently changes product contract.


### Stopped coordinator work and checkpoint drift

Use the existing `goal status` diagnostics before repeating validation. Declare generated source/test/package-lock paths and co-owned feature-document updates before implementation. If scope or Git identity changed, inspect retained artifacts and use `goal reconcile` with contributor evidence; it saves an exact snapshot and never replays or completes the task. A fresh bounded plan may inspect/fix retained work under the already-approved design. Preserve known provider findings during recovery, and never reset a historical protected baseline to make a task pass.
