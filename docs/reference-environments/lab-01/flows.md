---
id: REF-LAB-FLOWS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — flows, boundaries, ownership

## Existing business traffic (observed by external system, NOT Control Plane commands)
```text
User       Browser/FEAdmin → ASP.NET WebAPI → PostgreSQL / Valkey
Device     Device or mqtt.py → VerneMQ → MQTT processor → RabbitMQ → WebAPI → DB
Notify     RabbitMQ → Notification Worker → DB history / Firebase
Realtime   WebAPI → SignalR /mqtthub → browser
Logs       WebAPI/MQTT/Notification → Grafana Alloy → Loki → Grafana
Autoscale  Metrics Server → HPA → WebAPI Pod scale
Release    GitLab source → Docker Buildx → registry/snapshot → Helm → Kubernetes
```

## Proposed Control Plane path (do not interfere with business events)
```text
Git/Registry metadata READ → Artifact Discovery → Release Candidate
  → Approval (own DB) → Workflow → Queue (dedicated)
  → Agent/Driver → isolated target → Observe Helm/K8s → Audit & Report
```

**Separation invariant:** MQTT device topics and RabbitMQ business queues are **targets to inspect**, not the Control Plane's command transport. Use dedicated broker/virtual host, credentials and network access for Control Plane if RabbitMQ is chosen internally. Never subscribe to business queues as an ordinary consumer for inspection, because deliveries may be distributed or ACK behavior changed.

## Ownership and reconciliation
- Kubernetes HPA owns replica scaling for mapped workloads; Control Plane **must not** hard-set `replicas` against HPA for observation-only experiments.
- Helm release state is owned by Helm (or a currently deployed GitOps reconciler, if discovered); never deploy through multiple writers concurrently.
- CNPG owns PostgreSQL lifecycle/failover; Control Plane inventory is read-only in phase 0.
- Prometheus/Loki/Alertmanager are observability sources; Control Plane should not overwrite alert rules without explicit policy.
- Talos OS upgrades/reboots, Proxmox VM operations and NFS stateful disruption are **excluded** from shared-cluster experiment actions.
