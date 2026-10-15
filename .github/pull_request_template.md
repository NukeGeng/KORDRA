# Pull request

## Outcome
- Feature ID / owning module:
- User story or bug / scope:

## Source and architecture
- Files changed:
- ADR / bounded context / contract changed? Why? Link:
- APIs/messages/schema versions and consumer compatibility:

## Authorization and multi-tenancy
- Actors, permission keys, resource scopes:
- Cross-tenant / privilege escalation / Super Admin behavior tested:

## Reliability & security
- Outbox/inbox/idempotency/retry/timeout/Unknown/lease considerations:
- Secret handling / driver capability / release hash verification:

## Evidence
- Unit/contract/integration/negative tests actually run + outputs:
- Docs updated (feature, contract, architecture, runbook, traceability):
- Debt entries/incident followups:
- Regression/rollback procedure:
- AMR risk/profile, actual observed model/effort, context content hash:
- Native review channel, run/review ID, exact head SHA/content hash and completion evidence:
- Finding validation/fix/reproduction/dismissal evidence; unresolved blockers:
- Unsupported/not configured/not tested capabilities and required human approval:

## Checklist
- [ ] `python3 docs/tooling/doccheck.py` passes
- [ ] Feature/module docs match code change
- [ ] Permission & tenant scopes reviewed
- [ ] Retry/compensation and failure tests handled
- [ ] No unowned TODO or undocumented architecture drift
- [ ] AMR unit/contract tests and strict docs checks pass; missing applicable gates are explicit
- [ ] Independent review for CRITICAL; native provider review verified where configured
- [ ] Sensitive operations retain explicit human approval; AI review is not an approval grant
