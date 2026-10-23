---
id: CTR-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
---

# Public contracts and compatibility

## Delivery contracts and current adoption status

- [Local authentication v1](local-authentication-v1.md) — accepted for Foundation T01–T07; FEAT-IAM-002 browser/session contract and numeric bounds.
- [Authority/admission v1](authority-admission-v1.md) — accepted for Foundation T01–T07; owner facts, management DTOs, single-process ordering and AUD intent/idempotency.
- [Client API handoff v1](client-api-handoff-v1.md) — proposed independently owned clients integration, schemas and UI states.
- [Release/execution v1](release-execution-v1.md) — proposed immutable release/approval, operation transitions, durable delivery and verification/rollback.

The delivery design and ADR-0012 distinguish accepted runtime contracts from proposed future contracts. The accepted contracts remain unimplemented until source and tests provide evidence; none grants permission to operate targets.

Ở đây lưu chuẩn được nhiều module dùng chung, không thay thế feature specs của owner. [Permissions](permissions.md), [Message catalog](message-catalog.md), [Message envelope](message-envelope.md), [Driver contract](driver-contract.md), [Agent protocol](agent-protocol.md), [API guidelines](api-guidelines.md), [Error codes](error-codes.md).

Mọi public API/message phải có owner, schema version, request/response/error semantics, tenant scope, idempotency, consumers và compatibility policy. Mọi thay đổi phá vỡ consumer phải có migration/rollout và ADR nếu sửa invariant.

## Scoped in-process inventory seam — ADR-0010

[FEAT-INV-001/I1](../features/infrastructure-inventory/features/register-target.md) owns `IInventoryReader` and immutable `InventoryReadResult` / `InventoryResource` in `src/Modules/InfrastructureInventory/Contracts/`. Adopted consumers are the own-module unavailable reader, API DI composition and bootstrap tests. A positive reader exists only in tests; HTTP inventory requests never invoke it and always return empty 403. There is no adopted positive HTTP inventory schema, event, provider protocol or external consumer migration. This BCL-only in-process interface is not a versioned wire contract; future HTTP/message adoption needs explicit consumer/schema/compatibility review.
