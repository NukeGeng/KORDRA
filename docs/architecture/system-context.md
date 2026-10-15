---
id: ARCH-CONTEXT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# System Context — Actors & external systems

```mermaid
flowchart LR
 Dev[Developer] --> CP[DevOps Control Plane]
 Op[Operator / Approver] --> CP
 PA[Platform Operator] --> CP
 CP <--> GIT[Git / CI Providers]
 CP <--> REG[OCI Registry]
 CP <--> AG[Private Network Agent]
 AG <--> K[Kubernetes / Helm]
 AG <--> D[Docker Compose / SSH]
 CP <--> CD[Argo CD / Jenkins]
 CP --> SEC[Secret Manager]
 CP --> OBS[Observability Providers]
```

**Control Plane owns:** release intent, frozen deployment specification, authorizations, workflow, audit, agent routing, inventory metadata.

**Does not own:** CI source build result, OCI blob durability, Kubernetes desired manifests when GitOps owner, cloud infrastructure provisioning (unless future plugin ADR).

### Trust boundaries
Browser/Desktop ↔ Public API; external webhooks ↔ Ingress; API/Workers ↔ broker/DB; Control Plane ↔ Agent Gateway; Agent ↔ target infrastructure. Mỗi boundary cần authentication, authorization, anti-replay, timeout và correlation.
