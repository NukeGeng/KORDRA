---
id: ARCH-CONTAINERS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# C4 Container / Process topology

```mermaid
flowchart TB
 UI[Vue Web / Tauri / CLI] --> API[.NET API Server]
 Webhooks[External signed webhooks] --> API
 API --> AUTH[AuthZ / Policy component]
 API --> DB[(PostgreSQL / Marten)]
 API --> MSG[Wolverine Durable Messaging]
 MSG --> WORK[.NET Workflow/Execution Workers]
 WORK --> DB
 WORK --> AGW[Agent Gateway]
 AGW <-->|Outbound secure session| AG[Private Agent]
 AG --> TAR[Deploy targets]
 WORK --> TAR
 AG --> OBS[Observation ingest]
 OBS --> DB
 WORK --> OT[Logs Traces Metrics]
 API --> OT
```

**C4 container không đồng nghĩa K8s container:** là deployable app/data store. Ban đầu API + Workers có thể dùng chung modular monolith source nhưng chạy khác process. Agent là tiến trình độc lập để nối mạng riêng.

### Runtime boundaries
- API không giữ long-running deploy request đồng bộ.
- Worker chịu trách nhiệm durable scheduling; không lưu state chỉ trong RAM.
- Gateway không chứa quyền quyết định approval; chỉ xác thực và chuyển operation có chứng thực.
- PostgreSQL giữ operational state; log blobs/object store nếu cần; không đẩy raw log vào event store.
- Broker bên ngoài (RabbitMQ) optional trong MVP; consumer topology phải tồn tại ở mức hợp đồng.

### Dependency direction
UI → API; handler → application/domain ports; adapters → port interfaces; domain không tham chiếu Helm, Tauri, Wolverine, Kubernetes SDK.
