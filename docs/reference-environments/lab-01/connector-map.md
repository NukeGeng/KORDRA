---
id: REF-LAB-CONNECT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — module/connector/permission/test ownership mapping

| Source technology | Product module owner | Adapter/capability | Initial mode | Experiment IDs |
|---|---|---|---|---|
| GitLab | ci-integrations | repository/CI metadata | observe | LAB-CI-001 |
| Docker Buildx | artifact-management | metadata/platform/digest verifier | fixture/sandbox | LAB-ART-002 |
| Docker Compose | deployment-drivers | compose deployment | sandbox | LAB-DEP-004 |
| Registry HTTP/HTTPS | artifact-management + connection-secrets | OCI/snapshot discovery and digest pin | observe | LAB-ART-001, LAB-ART-003 |
| Proxmox / Talos | infrastructure-inventory | node and VM inventory | observe | LAB-INV-002 |
| Kubernetes/Helm | infrastructure-inventory + deployment-drivers | discovery, plan, apply, rollout observation | observe then sandbox | LAB-INV-001, LAB-DEP-001/002/003 |
| SignalR | observability | hub inventory / app health | observe/fixture | LAB-OBS-004 |
| VerneMQ | messaging | topic/broker metrics connector | observe/fixture | LAB-MSG-001/002 |
| RabbitMQ | messaging | exchanges/queues/consumers, no consume | observe/fixture | LAB-MSG-003/004/005 |
| CNPG/TimescaleDB/PostGIS | infrastructure-inventory + operations | DB status and backup metadata | observe | LAB-DATA-001/002 |
| Valkey | infrastructure-inventory | cache status, ACL-aware | observe | LAB-DATA-003 |
| PVC/NFS | infrastructure-inventory + operations | storage health / snapshot evidence | observe | LAB-DATA-004/005 |
| HPA/Metrics Server/PDB | observability + infrastructure-inventory | scaling & policy inspection | observe/sandbox | LAB-HA-001/002 |
| Prometheus/Grafana | observability | metrics/dashboard query | observe | LAB-OBS-001 |
| Alloy/Loki | observability | log query and contextual jump | observe | LAB-OBS-002 |
| Alertmanager | observability | routing inspection | observe | LAB-OBS-003 |
| Jaeger/OTel | observability | capability detection | observe | LAB-OBS-005 |
| macOS Python tool | deployment-management | behavior comparison/migration test | offline | LAB-LEG-001 |
| Control Plane auth | identity-access + platform-admin + tenant-management | tenant-scope permissions | isolated | LAB-SEC-001/002/003 |

**Connector schema requirement:** `capabilities`, `read_operations`, `write_operations`, `healthProbe`, `timeout`, `secretRef`, `networkBoundary`, `observedAt`. `write_operations` empty by default. Driver declares `Plan/Observe/Execute/Rollback` separately; unsupported operation fails closed.

**Credential scopes:** read-only Kubernetes discovery service account, read-only registry/RabbitMQ Management API, Prometheus/Loki query reader, Talos minimum access. Secret refs are external; no kubeconfig or key committed.
