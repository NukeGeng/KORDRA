---
id: CTR-ERRORS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Stable error and failure classification

| Code | Meaning | Retry? | Operator action |
|---|---|---|---|
| `AUTH_DENIED` | principal lacks permission/scope | no | request scope/grant |
| `TENANT_BOUNDARY_VIOLATION` | resource owner != resolved tenant | no | security audit |
| `APPROVAL_EXPIRED` | approval TTL exceeded | no | re-approve |
| `SPEC_HASH_MISMATCH` | approved spec changed | no | new approval |
| `TARGET_CONFLICT` | another operation holds target lease | delayed | wait/replan |
| `DRIVER_UNSUPPORTED` | capability missing | no | choose compatible driver |
| `AGENT_OFFLINE` | agent unreachable | conditional | reconnect/observe |
| `OPERATION_OUTCOME_UNKNOWN` | side effect cannot be confirmed | **not blind retry** | reconcile/incident |
| `TARGET_RATE_LIMITED` | provider throttle | yes bounded | capacity policy |
| `HEALTH_VERIFICATION_FAILED` | app verification unsuccessful | policy | rollback/inspect |
| `CREDENTIAL_REVOKED` | credential invalidated | no | rebind/rotate |
| `MESSAGE_SCHEMA_UNSUPPORTED` | consumer cannot read version | no/DLQ | migration |

API returns stable machine code + safe user message + correlation ID. Never return credentials, stack trace, raw remote output or cross-tenant resource existence in public error messages.
