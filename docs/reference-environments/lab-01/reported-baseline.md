---
id: REF-LAB-BASELINE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — reported inventory baseline

**As of:** 2026-10-15. **Evidence level:** USER-REPORTED ONLY; cluster/source chưa được truy vấn lại. Giá trị dưới đây là dữ liệu để thiết kế test, tuyệt đối không dùng như chứng cứ vận hành hiện hành.

| Component | Reported state | Verify before execution |
|---|---|---|
| Source | GitLab, chưa tích hợp GitLab CI/CD | project/branch/CI availability qua API read-only |
| Build | Docker + Buildx; Compose local riêng | platform, OCI digest, local-only boundaries |
| Artifacts | HTTP registry trong LAN, HTTPS registry, FE snapshot | registry read ACL, TLS, digest, snapshot hash |
| Virtualization / OS | Proxmox VMs, Talos Linux | node identities/version/API permissions |
| Kubernetes | 1 control-plane + 4 workers, Ready | contexts, node/pod inventory, taints, owner |
| Deploy | Helm/chart/values/YAML | Helm release ownership, current revision, GitOps conflicts |
| FE/API | FEAdmin/Nginx, WebAPI ASP.NET | namespaces, endpoints, workloads, probes |
| WebAPI HPA | 2 pods; min2/max6, target CPU70% | HPA status, metrics availability, workload selector |
| Realtime | SignalR `/mqtthub` + WebSocket | hub auth, scaling/topology, possible backplane unknown |
| MQTT | VerneMQ 2 nodes running, MQTT processor | broker cluster health, subscriptions, payload contract |
| RabbitMQ | 3 broker nodes, 13 business classic queues | type, durability, consumers, DLQ, ack, backlog, leader/replicas |
| Notification | worker, Firebase, database history | delivery history, non-production token and retry behavior |
| Database | PostgreSQL CNPG primary+replica healthy, TimescaleDB/PostGIS | replication, backup status, DB extensions |
| Cache | Valkey 2 Pods; replication not verified due to ACL | ACL-approved health; do not assume sentinel/HA |
| Storage | RWX assets PVC 5 GiB; NFS server one pod | availability, backup/restore, actual mounted capacity |
| Scaling | HPA, Metrics Server, PDB, placement, rollout | selector/threshold/status, missing safeguards |
| Metrics | Prometheus, node-exporter, kube-state-metrics, cAdvisor | targets/scrape status, label cardinality |
| Logs | Alloy → Loki → Grafana | log query permission, PII redaction |
| Alerts | Grafana + Alertmanager | configured routes and contacts; read-only test |
| Traces | OTel Collector/Jaeger configured, no workload observed | do not mark tracing operational |
| Tool hiện tại | Python, SQLite, HTML/CSS/JS | compare features, do not presume robust multi-user coordination |

## Reported concerns — never auto-remediate
- Metrics Server sử dụng `--kubelet-insecure-tls`: đánh dấu security risk, xác minh lý do/PKI; không tự chỉnh cluster flags.
- Assets backup CronJob `suspend: true`: backup gap; **không** tự bật hoặc thử restore trên data thật.
- RWX/NFS với một NFS pod: nguy cơ điểm lỗi đơn; chạy chaos/restore trong clone.
- Valkey chưa xác định replication health: status `unknown`, không suy đoán healthy.
- RabbitMQ 13 classic queues: không tự chuyển sang quorum. Cần review feature compatibility, topology và migration plan; broker 3 nodes **không tự động** bảo đảm từng queue được nhân bản.
- OTel/Jaeger chưa quan sát được workload: không ghi pass E2E tracing.

## Source provenance
- Primary: mô tả inventory người dùng cung cấp trong hội thoại, 2026-10-15.
- Original detail inventory: `DEVOPS_CONTROL_PLANE_INVENTORY.md` trên máy người dùng (chưa import được).
- Older screenshots/docs are historical references only; do **not** override this reported baseline. Khi số liệu thay đổi, tạo snapshot có timestamp ở artifact store được cấp quyền, cập nhật bảng risk và mở drift ticket nếu cần.
