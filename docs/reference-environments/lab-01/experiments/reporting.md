---
id: EXP-LAB-REPORT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — scorecard, defect routing and metrics

Use this scorecard for every weekly review (all initially `NOT_RUN`):

| Metric | Definition | Source | Initial result |
|---|---|---|---|
| Coverage | PASS / applicable executable cases | coverage-matrix + evidence store | NOT_RUN |
| Drift accuracy | confirmed divergences / all reported divergences in controlled fixture | reconciler observations | NOT_RUN |
| Status freshness | observation timestamp lag | inventory state store | NOT_RUN |
| Duplicate side effects | count of repeated effective driver mutations per operation | driver audit + target state | NOT_RUN |
| Failed rollout recovery | recoveries / attempted isolated recovery drills | workflow + operation results | NOT_RUN |
| Messaging lag | per-queue ready/unacked and processing lag | RabbitMQ metrics | NOT_RUN |
| Documentation freshness | changed features with same-PR docs/tests/evidence | changeguard + PR | NOT_RUN |
| Security negatives | denied cases / relevant negative attempts | auth test reports | NOT_RUN |

## Review / triage
- Every FAIL → issue linked to owning feature, evidence, incident severity and test reproduction.
- Environment weakness (e.g. NFS single pod) → environment risk register, **not automatically a product bug**.
- Product bug (e.g. wrongly claims Valkey HA) → product issue and regression test.
- Unsupported adapter feature → capability = `unsupported`, not failed test masquerading as supported.
- New technical decision → ADR, and update AGENTS/contract/feature only when approved.

Required weekly deliverable: deltas from previous verified snapshot, executed cases/evidence, failures by module, open blockers, policy violations, accepted ADRs, changes to production boundaries.
