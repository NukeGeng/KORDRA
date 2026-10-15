---
id: ARCH-MESSAGING
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Messaging architecture — command/event/delivery

## Các kênh
- **Commands:** durable point-to-owner/handler (`RequestDeployment`, `DispatchOperation`, `ResolveUnknownOutcome`). Không dùng broadcast để thực thi deploy.
- **Domain Events:** immutable fact trong module (Marten event stream nếu được chấp thuận); không mặc định ra broker.
- **Integration Events:** published contract đã version hóa tới consumers; event names ở quá khứ (`DeploymentSucceeded`).
- **Telemetry:** heartbeat, logs, metric batches, observations đi qua channel riêng theo volume/SLA; không cạnh tranh deployment command queue.
- **UI realtime:** push cập nhật dựa trên read model/outbox; không là source of truth.

## Delivery semantics
PostgreSQL + Wolverine durable queues/inbox/outbox giai đoạn đầu; RabbitMQ chỉ thêm khi cần scale nhiều process hoặc isolation. Broker/inbox đảm bảo delivery theo cấu hình, **không chứng minh side-effect bên Kubernetes/SSH exactly-once**. `operationId` idempotency + inspect actual state sau timeout.

### Required message envelope
`messageId`, `type`, `schemaVersion`, `tenantScope`, `correlationId`, `causationId`, `occurredAt`, `actorRef`, `resourceRef`, `operationId` khi side-effect, `traceparent` nếu tracing. Payload tuyệt đối không chứa bearer token, plaintext secret hoặc kubeconfig.

## Transactional consistency
Use-case update state + append domain events + outgoing message cùng transaction (outbox). Incoming deduplicate/inbox before handler. DB commit phải thành công trước khi ACK ở durable boundary. Consumer replay không bypass policy/approval.

## Queue isolation (logical)
`artifact.discovery`, `deploy.commands`, `deploy.results`, `workflow.timers`, `agent.lifecycle`, `observation.ingest`, `notifications`, `dead-letter`. Capacity, priority, delay/backoff, retention & redrive policy riêng từng nhóm.

## Failure rules
- Retry transients với exponential backoff + jitter, giới hạn attempt/deadline.
- Invalid schema/authorization: reject, audit, quarantine; không retry vô hạn.
- DLQ record failure class, attempt history, payload sanitized, owner và replay guard; replay chỉ qua admin action có audit.
- Per-target ordering: target lease + persisted sequence/fencing; broker ordering không đủ khi multiple consumers/retries.
- No global command ordering assumption; use correlation/causation graph.

## Schema compatibility
Public message fields optional additions backward compatible; breaking change tạo new schema version + dual reader/producer rollout; consumers inventory trong [catalog](../contracts/message-catalog.md).

## Operational indicators
Oldest queue age, depth, outbox age, handler failure rate, poison message, retries, queue saturation, correlation gaps; alert thresholds được set theo SLO thực đo.

## References
- https://wolverinefx.net/guide/durability/postgresql
- https://wolverinefx.io/guide/durability/index.html
