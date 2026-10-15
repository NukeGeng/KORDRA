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

## Definition of complete
`docs/engineering/definition-of-done.md`.

## AI-assisted engineering and native review
Use the existing [AI review protocol](docs/governance/ai-review-protocol.md) and [AMR v2.1 guide](docs/engineering/ai-orchestration/README.md). Discover runtime capabilities → classify with source/owner evidence → resolve supported model/effort → preserve context → implement → execute deterministic gates → independent review for CRITICAL → provider native review when configured → triage/fix/retest/re-review → reconcile docs/evidence → required human approval. Keep a capable session rather than switching at every stage.

Record exact model/effort observations, gate commands/results, review channel and content hash/head SHA. Escalate immediately for security/state risks, at two failed repairs of one root cause, and stop at four pending investigation. Missing infrastructure or native integration is an explicit limitation, never a passing gate. Auto-review and code review cannot grant production approval. Provider automatic review must be observed on the current PR/MR revision before it is claimed verified.

## Additional workflow for reference-environment experiments
1. Register case in [LAB-01 coverage matrix](docs/reference-environments/lab-01/experiments/coverage-matrix.csv).
2. Confirm [safety mode](docs/reference-environments/lab-01/experiments/safety-and-change-control.md), target identity, ACL, and approved data policy.
3. Save preflight + observed state (redacted) and immutable spec; run only within permitted phase.
4. Record actual status and evidence via [template](docs/reference-environments/lab-01/experiments/evidence-template.md).
5. Map results to module feature/contract, issue/debt, and drift entry. Failed test never silently changes product contract.
