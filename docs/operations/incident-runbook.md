---
id: OPS-INCIDENT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — Security or deployment incident

## Start
Record incident ID, severity, time, affected tenants/targets/versions, incident commander and communications owner. First contain blast radius: pause affected queue/target, revoke credential/agent if compromised, preserve audit.

## Diagnose
Trace by correlationId → release → approval → deployment → operation → agent journal → observed infrastructure. Compare accepted docs/ADRs with actual code/config, note divergence.

## Restore
Use approved remediation/rollback with independent verification; monitor recurrence. Do not bypass tenant/policy without break-glass path and audit.

## Postmortem (blameless)
Timeline, triggering factors, root causes, which invariant/test/doc was missing, user impact, corrective action, owner, due date, linked DEBT-ID/ADR/feature update. Close when regression test and docs reflect the fix.
