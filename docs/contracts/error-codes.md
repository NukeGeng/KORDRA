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

## T06 environment-create v2 reuse

The accepted Resource Policy v1 environment-create route reuses `INVALID_REQUEST` (400), `SESSION_INVALID` (401), opaque `REQUEST_REJECTED` (403, including grant/scope or antiforgery denial), `REQUEST_CONFLICT` (409), and `AUTHORITY_UNAVAILABLE` (503) in its schemaVersion 2 safe error envelope. This does not adopt the unrelated proposed catalog above or disclose whether a foreign resource exists. The shared Host/Origin/deadline guard can reject before endpoint dispatch with the existing security envelope. See [fixtures](fixtures/resource-policy-v1.http.json) and [OpenAPI](openapi/resource-policy-v1.openapi.json).

## Accepted local-authentication-v1 HTTP additions

These entries apply to the accepted `/api/v1/auth/*` contract. The catalog remains `proposed` for other product areas; these rows do not adopt its unrelated error codes.

| Code | HTTP | Meaning | Retry? | Operator action |
|---|---:|---|---|---|
| `AUTHENTICATION_FAILED` | 401 | Credentials could not be verified; unknown, wrong, disabled and locked accounts share the same public result | no | Check credentials or recover the account offline |
| `SESSION_INVALID` | 401 | Session is absent, expired, revoked or invalid | no | Sign in again |
| `REQUEST_REJECTED` | 403 | Origin, Host or antiforgery checks rejected the request | no | Return through the configured local origin |
| `RATE_LIMITED` | 429 | Bounded login or command rate was exceeded | delayed | Respect `Retry-After` |
| `INVALID_REQUEST` | 400 | Request content, shape or bounded input is invalid | no | Correct the request |
| `REQUEST_CONFLICT` | 409 | Idempotency key conflicts with persisted command identity | no | Reconcile the command and submit a new valid operation if needed |
| `SESSION_LIMIT_REACHED` | 409 | The account already has the maximum number of active sessions | no | End an existing session before signing in again |
| `AUTHORITY_UNAVAILABLE` | 503 | Required identity, audit, time or storage authority could not be confirmed | bounded | Retry only after authority is available; do not assume a mutation succeeded |
