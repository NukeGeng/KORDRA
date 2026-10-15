---
id: ENG-TEST
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Test pyramid, failure injection & security regression

## Levels
1. **Domain unit tests:** invariants, deterministic transition, policies, role grant ceilings, tenant ID.
2. **Contract tests:** OpenAPI, message schema compatibility, agent/driver version negotiation.
3. **Integration tests:** PostgreSQL/Marten/Wolverine inbox/outbox, broker (if used), registry/secret adapters with isolated test env.
4. **End-to-end tests:** approve → frozen release → dispatch → observe → status; Helm K8s test cluster & Compose target in sandbox.
5. **Chaos/recovery tests:** kill process between commit/outbox dispatch, replay duplicate, stale epoch, unknown agent outcome, lost ACK.
6. **Security tests:** horizontal/vertical access escalation, support TTL, cross-tenant list/query/websocket/export, secret exposure, webhook spoofing.

## Required cases per feature
| Case | Minimum assertion |
|---|---|
| Success | observable business outcome |
| Wrong tenant | deny without existence leak |
| Missing/revoked grant | denied at execution boundary where critical |
| Duplicate message/retry | one logical mutation, idempotent receipt |
| Worker/agent restart | persisted status, safe recovery |
| Timeout | Unknown or safe failed state, not blind rollback |
| Contract evolution | producer/consumer compatibility |
| Audit | correlation, actor/scope, safe logs |

## Evidence
Store test identifier/report link, target build SHA, observed timestamp; no green status from unexecuted tests. Feature cannot be `verified` without real test evidence.

## Suggested CI stages
`docs-check → format/lint → unit → contract → integration → security isolation → driver conformance → build/package → staging smoke`. Slow chaos tests may run nightly/pre-release but must be recorded and cannot be silently skipped for relevant risk.
