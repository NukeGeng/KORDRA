---
id: ARCH-DEPLOY
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Deployment topology & environments

```mermaid
flowchart LR
 subgraph Central[Control Plane network]
 API[API replicas] --> PG[(PostgreSQL HA)]
 WK[Workers] --> PG
 API --> MW[Messaging transport]
 WK <--> MW
 AGW[Agent Gateway] --> WK
 end
 subgraph Private[Customer private network]
 AG[Outbound Agent] --> K[K8s / Docker]
 end
 AG <-->|Outbound authenticated connection| AGW
```

## Environments
`local`: single PostgreSQL, API, worker, optional RabbitMQ (only when needed), fake agents/targets; `staging`: realistic auth/policy/agent; `production`: HA/backups/secrets/alerts/least privilege.

## Security network
Internet exposed API only behind TLS/reverse proxy; agent gateway separate auth; DB/broker restricted private network; operator access via identity provider; secret provider isolated.

## Scaling axes
Scale API on request rate; worker pools by target queue/throughput; agent pools per private network; broker partitions/queues by workload type. Never horizontally scale state machine without CAS/fencing and leader/lease plan.

## Unresolved
`[NEEDS-DECISION]` production cluster/VM host, multi-region requirements, tenant isolation tier, HA/RPO/RTO, identity provider. Không kết luận dựa trên diagram.
