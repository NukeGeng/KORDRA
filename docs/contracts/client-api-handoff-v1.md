---
id: CTR-CLIENT-HANDOFF-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-20
---

# Product API handoff to independent clients — v1 proposal

Product owns backend schemas, semantics, authorization, contract fixtures and API evidence. The independent clients session owns UI implementation in its own Git repository/submodule. This file is a handoff specification only: no clients files, submodule revision, website, database, process or ports are modified/tested by this Product design task. No cross-session message is sent.

## Consumer behavior

| Screen / client use case | Product capability | Required states |
|---|---|---|
| Sign in / account | [FEAT-IAM-002](../features/identity-access/features/local-account-session.md), [auth contract](local-authentication-v1.md) | Anonymous, submitting, authenticated, generic failure, rate-limited, session expired; login does not imply a selected tenant |
| Tenant/project/environment selector | TEN/PRJ authorized catalogs | Explicit empty selection, loading, selected, forbidden/unavailable; context changes discard prior protected results |
| Target/profile and inventory | INV/SEC with [admission contract](authority-admission-v1.md) | Disconnected, denied, stale, partial, unavailable and observed status remain distinct; credentials never arrive in browser DTOs |
| Artifact/release/approval queue | ART/REL/APR | Immutable digest/spec reference, awaiting approval, rejected/expired/revoked; no optimistic “approved” before server commit |
| Operation/history/rollback | DEP/WFL/OBS/AUD with [execution contract](release-execution-v1.md) | Pending/running/verifying/verified/failed/unknown; rollback is a new separately authorized operation |

The API supplies server timestamps, scope references, stable machine codes and correlation IDs. The UI must not infer permissions from display role names or treat network errors as infrastructure failure. Render no retained protected data after session loss, tenant switch or revoked disclosure. A hidden button is not API authorization. Disable automatic retries for login and destructive commands; retriable mutations retain their exact idempotency key/body identity.

Initial protected responses use `Cache-Control: no-store`; no browser/shared protected-result caching or offline replay is part of this contract. Each bounded page/poll is a new authorized invocation. Persisted UI selection is a hint, never permission or proof that previously displayed data remains disclosable.

## Contract delivery and compatibility

Each implemented feature publishes versioned OpenAPI plus synthetic request/response/error fixtures under proposed `docs/contracts/examples/` only when the actual contract is adopted. Code-generated schemas belong to the owner feature mapping and include source SHA; no generated client is silently installed in clients. Product tests exercise HTTP contracts directly; client mocks must use the delivered fixtures and label unimplemented backend capability.

First authentication uses exact same-origin HTTPS `https://localhost:5128`, antiforgery headers and HttpOnly credentials. Client dev proxy configuration is a separate clients-owned task; no wildcard CORS or alternate credential transport is an implementation convenience. Public website domain `kordra.id.vn` does not become an allowed local API origin by branding association.

Version route `/api/v1`, `schemaVersion: 1` in JSON envelopes; breaking fields/semantics require a new version and consumer migration. No GraphQL/realtime stack or streaming API is selected here. Initial status refresh is bounded polling; WebSocket/SSE need a separate disclosure/revocation contract. Architecture/feature gates apply before screens can claim a capability shipped.

## Ownership and acceptance

Product acceptance: schema examples match real backend tests, all reads/mutations enforce tenant scope, errors redact sensitive details and operation IDs reconcile unknown outcomes. Frontend acceptance is independently owned and is not inferred from Product tests. A product milestone needing UI is not complete until actual client integration evidence exists; backend completion can be recorded separately without blocking unrelated eligible backend tasks.
