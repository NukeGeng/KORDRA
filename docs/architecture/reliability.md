---
id: ARCH-RELIABILITY
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Reliability, concurrency and disaster recovery

## Crash & delivery matrix
| Failure | Expected behavior |
|---|---|
| API killed after DB commit | Outbox eventually emits; request ID remains queryable |
| Worker killed mid-deploy | Lease expiry and reconcile operation; no blind repeat |
| Agent disconnected | Operation `Unknown`; journal queried on reconnect |
| Message duplicated/out of order | idempotency key + sequence/epoch guard |
| Two releases on same target | target lease + fencing, queued fairness |
| Broker unavailable | outbox retained; API backpressure; no claim of dispatch |
| DB unavailable | reject/mode read-only for unsafe operations; no volatile approval |
| Credential revoked | execution refused, audit recorded |
| Target partly updated | observed state/compensation policy; operator action when ambiguous |

## Controls
Durable state machine, TTL leases with fencing epochs, monotonic CAS state transitions, bounded retries, per-target queue quotas, action deadlines, idempotent commands, operator recovery UI and safe DLQ replay.

## DR plan
Define RPO/RTO based on real customer tier (not invented), PITR backup, encryption, identity/secret dependency, agent reconnect, migration plan, regional recovery testing. Backup and rollback have separate runbooks.

## Reliability tests
Kill worker at every workflow boundary, duplicate every command, lose agent result ACK, restart control plane during approval, broker disconnect, tenant context mismatch, rotate credential mid-execution.
