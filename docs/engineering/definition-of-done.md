---
id: ENG-DOD
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Definition of Done (DoD) — not only build success

A feature is Done only when **all applicable** conditions are met:

- [ ] Owning module + feature ID unique; spec reviewed and current; no duplicate source-of-truth.
- [ ] Actor, tenant/resource scope, permission keys, role-independent policy, sensitive operations reviewed.
- [ ] Domain invariants + state transitions + API/message contract and schema versions defined.
- [ ] Implementation follows module boundaries and accepted ADR; unresolved deviations recorded.
- [ ] Unit, integration, contract, negative tenancy tests and failure recovery tests passed where relevant.
- [ ] External side effects idempotent; durable commit/outbox, timeout/unknown and retry strategy validated.
- [ ] Logs/metrics/tracing/audit do not leak secrets or another tenant's data.
- [ ] Migration/backward compatibility and rollback runbook updated if needed.
- [ ] Technical debt recorded with owner, severity, due/exit criteria (or closed with evidence).
- [ ] Feature `source_paths`, `test_paths`, PR/commit evidence provided; docs updated in same PR.
- [ ] `python3 docs/tooling/doccheck.py` and appropriate CI gates pass; reviewer signs off.

For production-critical change: rollback/unknown outcome drill and SoD/approval compliance are explicit signoff items. If a condition is not applicable, explain why; do not silently drop it.
