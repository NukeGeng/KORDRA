---
id: OPS-DEPLOY
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — Safe deployment to target

## Trigger
Approved immutable release ready for a classified environment.

## Preflight
- Actor permission, tenant/resource binding, approval SoD/TTL/policy version valid.
- Frozen artifact digest and spec hash match; target healthy/reachable; correct controller ownership.
- Lease target free; driver capabilities support plan, execution, observation and rollback expectations.
- Capture current revision/config/digest and DB migration risk; verify secrets and change window.

## Execute
Create `deploymentId/operationId` once → persist workflow → acquire lease with fencing → driver plan (diff if supported) → execute → observe readiness/business probe → record status/evidence → release lease.

## Stop conditions
Unexpected tenant/target, spec mismatch, unsupported capability, stale agent epoch, secrets invalid, DB not writable. Any unconfirmed external side effect → `Unknown`, not blind retry.

## Verification & close
Current digest, target observed health, key API smoke checks, policy and audit evidence. If failed: use [rollback](rollback-runbook.md) only after explicit policy/approval.
