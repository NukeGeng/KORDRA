---
id: CTR-ENVELOPE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Message envelope, delivery & versioning

```json
{
  "messageId": "01J...",
  "messageType": "deployment.execute.requested",
  "schemaVersion": 1,
  "occurredAt": "2026-10-15T06:00:00Z",
  "scope": {"kind": "organization", "organizationId": "org_123"},
  "actorRef": {"kind": "service", "id": "workflow-engine"},
  "correlationId": "dep_001",
  "causationId": "msg_000",
  "operationId": "op_456",
  "resourceRef": {"kind": "target", "id": "target_789"},
  "traceparent": "00-...",
  "payload": {"releaseId": "rel_123", "specHash": "sha256:..."}
}
```

## Contracts
- `messageId` uniqueness for dedup; `operationId` stability through retries.
- Version immutable; register consumer compatibility in [message-catalog](message-catalog.md).
- Scope is *context*, not authorization proof: consumer resolves trusted resource ownership and validates signed operation claim.
- Encrypt transport, redact payload; no credentials, raw kubeconfig or auth cookies.
- `occurredAt` is informational; ordering uses stream sequence/aggregate version/lease epoch, not wall clock.
- Classify retry: transient (`timeout`, `rate_limit`, target_unreachable) / permanent (`invalid_scope`, `unsupported_capability`, `signature_invalid`).
- Message publishing follows outbox; receive follows inbox/dedup for durable commands.
