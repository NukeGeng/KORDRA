---
id: OPS-ROLLBACK
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — Safe application rollback

## Trigger
Deployment unhealthy, failed verification, or incident commander approves recovery.

## Preconditions
Identify last **verified** immutable artifact/config, namespace/target ownership, approval/emergency policy, database migrations, data compatibility, currently executing lease.

## Decision by driver
- Helm: select verified release revision and invoke controlled `helm rollback`, then independent health observation.
- Docker Compose: pin previous image digests and Compose config; redeploy via approved channel; observe container and business health.
- Argo CD external GitOps: revert desired Git state and sync through owner (do not direct-mutate resource by default).
- Unknown or partial state: preserve evidence, use operator intervention, no blind retry.

## Data safety
Rollback application image does **not** imply rollback database schema/data. If migration not backward compatible, use tested forward-fix or dedicated restore plan approved by data owner.

## Close
Record who approved, old/new artifact/spec, commands, verification, remaining risk, linked incident and postmortem.
