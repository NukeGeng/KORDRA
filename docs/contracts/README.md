---
id: CTR-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Public contracts and compatibility

Ở đây lưu chuẩn được nhiều module dùng chung, không thay thế feature specs của owner. [Permissions](permissions.md), [Message catalog](message-catalog.md), [Message envelope](message-envelope.md), [Driver contract](driver-contract.md), [Agent protocol](agent-protocol.md), [API guidelines](api-guidelines.md), [Error codes](error-codes.md).

Mọi public API/message phải có owner, schema version, request/response/error semantics, tenant scope, idempotency, consumers và compatibility policy. Mọi thay đổi phá vỡ consumer phải có migration/rollout và ADR nếu sửa invariant.
