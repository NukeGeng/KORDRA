---
id: ARCH-DATA
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Data Architecture — persistence & source of truth

## Storage responsibilities
| Data | SoT | Access rules |
|---|---|---|
| Identity/membership/policy bindings | Identity/Access store | tenant/platform scoped |
| Target inventory/capabilities | Inventory store | tenant-scoped except platform inventory |
| Release/immutable spec/approval | Release/Approval aggregate | freeze/version checks |
| Deployment workflow state/timers | Workflow persistence | durable & concurrency safe |
| Event history | Marten event streams (where justified) | append-only business history |
| Inbox/outbox/envelopes | Wolverine persistence | protected operational tables |
| Observed health | observation snapshots/TSDB | bounded TTL, stale marker |
| Logs/artifact evidence | object/log storage | retention and scoped access |
| Secrets | external secrets provider | only reference in primary DB |
| Audit | append-only audit sink & tamper evidence strategy | separate operator controls |

## Primary entities
`PlatformUser`, `Organization`, `Membership`, `RoleDefinition`, `RoleBinding`, `Project`, `Application`, `Environment`, `Target`, `ConnectionProfile`, `Artifact`, `Release`, `ApprovalRequest`, `Deployment`, `WorkflowInstance`, `Operation`, `Agent`, `ObservedSnapshot`, `PolicyDecision`, `AuditRecord`.

## Tenant keys
Tenant entities carry `organization_id` and resource owner FK. Cache/read model/external message scoped; unique constraints include tenant ID for tenant local names. Platform tables separated logically. Never let broad list query accidentally bypass org filter.

## CQRS + event sourcing
Event sourcing selectively for Release/Deployment if replay/trace adds value; avoid forcing each config row into event stream. Critical authorization/release validation reads authoritative records, not lagging async projection. Inline projection for consistency-sensitive reads; async for dashboard/report only.

## Migration & retention
Expand → backfill → dual-read/write if needed → contract; record version and reversible steps. Audit retention, privacy deletion and legal policy must be explicitly decided. Regular backup AND restore drill; backup is not proven until restore succeeds.
