---
id: REF-LAB-MSG
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — messaging taxonomy, 13 queue intake and isolation

## Boundaries (do not collapse)
1. **MQTT/VerneMQ:** device transport, topic/payload/QoS/session/backlog metrics; e.g. `Traffic/v1/SB<SN>/Init` and `/Monitor`. `<SN>` is a placeholder, not a production device identifier. Payload may contain serial, timestamp, AES/Base64; redact before evidence.
2. **MQTT processor:** application translating device events to domain/integration messages; it is **not** a message broker.
3. **RabbitMQ business bus:** exchange → binding/routing key → classic queue → consumer → ACK; 13 queue names/event contracts are NOT present in the conversation excerpt and **must not be invented**.
4. **Control Plane messaging:** own `CommandBus/EventBus/Outbox/Inbox/Saga/DLQ`; physical transport isolated from business bus (separate broker or vhost, identity, quotas, ACL).
5. **SignalR:** realtime UI push; **not** persistent workflow state. Redis/Valkey backplane is unknown until verified.

## Mapping to collect for EACH of 13 real queues
Fill [13-queue worksheet](messaging-queue-catalog.template.csv) after sanitized import of original inventory. For each: `queueName`, `vhost`, `type`, `durable`, `arguments`, `exchange`, `binding/routingKey`, `publisher`, `consumer`, `eventContract`, `schemaVersion`, `ackMode`, `prefetch`, `ready`, `unacked`, `publishRate`, `deliverRate`, `retry/DLX`, `lastObservedAt`, `evidenceRef`.

## Read-only inspection approach
- RabbitMQ: Management API / metrics (`GET`), do not `basic.consume` on business queues, and do not requeue/purge/delete/republish.
- VerneMQ: broker stats, plugin-supported introspection, sanitized artificial test topics only in dedicated test namespace.
- Message contract verification: captured sanitized fixtures or a **dedicated** sandbox broker; assert serialization, schema compatibility, idempotency, delay/retry/dead letter handling.
- For classic → quorum exploration, construct new queues in sandbox, model ordering, durability, poison handling, incompatible policies; no production queue mutation by the tool.

## Definition of done
- All 13 queue metadata rows filled and validated against evidence, with no guessed names.
- At least one business-event path documented **without** injecting into production.
- The Control Plane queue path has duplicate/retry/worker crash tests in a test environment and is independently observable.
