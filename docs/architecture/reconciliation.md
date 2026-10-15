---
id: ARCH-RECONCILE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Desired/Observed State & Controller Ownership

## Three kinds of state
- **Desired:** approved release intent and expected deployment spec (Control Plane).
- **Observed:** actual image digest/revision, available replicas, health, drift reason (read from target).
- **Workflow:** progress and decision history (workflow store).

## Reconciliation ownership modes
| Mode | SoT for desired runtime config | Control Plane role |
|---|---|---|
| `managed-direct` | Control Plane approved manifest/release | May reconcile to approved spec under policy |
| `external-gitops` | Git repository / Argo CD | Observe; request change via GitOps integration; do not kubectl-override |
| `external-ci` | Jenkins/provider pipeline | Submit run, observe, respect provider authority |
| `observe-only` | External operator | Alert/report drift only |

## Loop
Observe → normalize → compare → classify `InSync/Drift/Progressing/Unknown` → decide policy → send idempotent reconcile command or alert. Backoff/jitter and per-target rate limit. Manual edits/races must be surfaced, not silently overwritten without authorization.

## Invariants
At most one authorized desired-state writer for a target/release scope; version/epoch fencing to prevent stale reconciler. ObservedState timestamp/quality required; stale telemetry cannot prove success. Controller decision and remediation steps auditable.
