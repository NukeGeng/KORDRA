---
id: ARCH-WORKFLOW
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Durable workflow, state machine and approvals

## Separation
- **Release Manager/Policy:** resolves allowed release and frozen spec.
- **Workflow Orchestrator:** owns process state, durable timers, transitions/compensations.
- **Deployment Coordinator:** acquires per-target lease, dispatches exact operation.
- **Worker/Agent:** executes and reports; cannot independently approve.
- **Observer/Reconciler:** verifies real state; `accepted`/`sent` ≠ `succeeded`.

## Deployment state machine
```mermaid
stateDiagram-v2
 [*] --> Requested
 Requested --> Validating
 Validating --> AwaitingApproval: approval required
 Validating --> Ready: no approval required
 AwaitingApproval --> Ready: approved frozen spec
 AwaitingApproval --> Rejected: rejected/expired
 Ready --> Queued
 Queued --> Dispatching: lease obtained
 Dispatching --> Executing: agent acknowledged
 Dispatching --> Unknown: ack timed out
 Executing --> Verifying: driver returned
 Executing --> Unknown: lost outcome
 Executing --> Failed: terminal execution error
 Verifying --> Succeeded: observed health OK
 Verifying --> Failed: failed verification
 Unknown --> Verifying: reconciled to applied
 Unknown --> Failed: definitely not applied
 Failed --> RollingBack: explicit policy/approval
 RollingBack --> RolledBack: verified recovered
 RollingBack --> RecoveryRequired: cannot verify
```

## Invariants
- Once approved: `artifactDigest`, deployment spec hash, environment, target, approver, policy version locked; any material change restarts approval.
- Require monotonic persisted transition with optimistic concurrency; terminal states cannot transition arbitrarily.
- Every external execution has stable `operationId` and one lease/fencing epoch; retry must inspect target/operation journal before applying.
- Timeout is not equivalent to failure; `Unknown` needs observation/manual resolution.
- Rollback cannot guarantee DB state restoration; external migrations require separate plan.

## Crash recovery
Persist next action before side effects; on restart claim orphan work with lease; query operation journal/target; reconcile before repeat; resume timers. No workflow truth only in RAM.

## Approval SoD
Requester cannot self-approve protected environments by default. Policy may specify one/two approvers, TTL, change window, step-up auth, explicit emergency override with audit. Do not hardcode production by environment display name; use environment classification/policy.
