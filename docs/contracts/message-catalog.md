---
id: CTR-MESSAGES
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Command / Event catalog and ownership

**Proposed logical contracts; names below are not yet production schemas.** Consumers must be reviewed during implementation.

| Message | Kind | Owner/Producer | Consumer | Durable | Idempotency/Ordering |
|---|---|---|---|---|---|
| `artifact.discovered.v1` | Integration event | ci-integrations | artifact-management | yes | providerEventId + digest |
| `release.created.v1` | Domain/integration event | release-management | approval-governance, audit | yes | releaseId |
| `approval.requested.v1` | Command | release-management | approval-governance | yes | requestId |
| `approval.granted.v1` | Event | approval-governance | workflow-orchestration, audit | yes | approvalId + specHash |
| `deployment.requested.v1` | Command | release-management | workflow-orchestration | yes | deploymentId |
| `deployment.dispatch.v1` | Command | workflow-orchestration | execution worker | yes | operationId + leaseEpoch |
| `deployment.accepted.v1` | Result event | execution-agents | workflow-orchestration | yes | operationId + seq |
| `deployment.finished.v1` | Result event | deployment-drivers | workflow-orchestration | yes | operationId + seq |
| `deployment.verified.v1` | Event | deployment-management | audit, notification | yes | deploymentId + transitionVersion |
| `deployment.unknown.v1` | Event | workflow-orchestration | recovery operator, audit | yes | deploymentId |
| `deployment.rollback.requested.v1` | Command | deployment-management | workflow-orchestration | yes | rollbackOperationId |
| `target.observed.v1` | Observation event | observability | reconciliation | bounded retention | targetId + observationVersion |
| `agent.lifecycle.changed.v1` | Event | execution-agents | inventory, notification | yes | agentId + version |
| `tenant.membership.changed.v1` | Event | tenant-management | policy cache invalidation, audit | yes | membershipId + version |
| `notification.send.v1` | Command | domain subscribers | notifications | yes | deliveryId |

## Queue routing proposal
- `deploy.commands` (strict durability, per-target guard); `deploy.results` (durable); `workflow.timers` (durable scheduled); `agent.lifecycle` (durable); `observations` (bounded/coalesced); `notification` (retry/DLQ); `artifact.discovery` (signed webhook dedup).
- Deploy queue must not share operational throughput budget with raw logs/metrics.

## Event lifecycle changes
Producer PR → new schema version + consumer inventory → integration tests → dual-read period → switch producer → deprecate old → record retirement date. No silent rename in active messages.
