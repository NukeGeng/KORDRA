---
id: EXP-LAB-EVIDENCE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — experiment evidence template

Copy outside documentation tree into a private evidence store or attach to approved ticket. Do not set `PASS` without machine-observed data.

```yaml
case_id: LAB-DEP-002
environment_ref: lab-01-sandbox
mode: P1_SANDBOX
status: NOT_RUN # NOT_RUN | BLOCKED | PASS | FAIL | INCONCLUSIVE
executed_at_utc: null
operator_ref: null
reviewer_ref: null
control_plane_commit: null
profile_revision: null
artifact_digest: null
spec_hash: null
approval_ref: null
operation_id: null
correlation_id: null
source_observed_at: null
preflight_result: null
expected_result: null
actual_result: null
assertions: []
evidence_links: [] # restricted links; no secrets/PII
cleanup_result: null
rollback_result: null
incident_ref: null
debt_ref: null
notes: null
```

**Pass criteria:** assertion result machine-verifiable; no unexpected write to shared cluster; event/DB/UI results correlated with same operation; cleanup/rollback evidence where applicable. **BLOCKED:** missing inventory, permission, owner, safety approval or isolation — not FAIL.
