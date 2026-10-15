---
id: CTR-API
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# HTTP API rules and request consistency

- Prefix `/api/v1/...`; API resources by tenant/project/environment hierarchy, but derive authorization from **resource ownership**, not URL alone.
- Auth OIDC/OAuth2, machine principals with scoped identity; request validation and policy errors use stable code.
- POST side-effect needs `Idempotency-Key` or stable client request key; return `202 Accepted` + `deploymentId` for long operation. Support GET status and cancellation only if safe.
- Concurrency use `ETag` / row version where configuration changes; immutable release spec cannot PATCH after approval.
- Pagination/cursor with stable tenant filter; API timeout never assumed to cancel external deployment.
- Webhook endpoints verify provider signature, timestamp/replay window and deduplicate delivery ID.

## Illustrative endpoints (not locked)
| Route | Permission | Expected behavior |
|---|---|---|
| `POST /api/v1/organizations/{org}/releases` | `release.create` | immutable release intent |
| `POST /api/v1/environments/{env}/deployments` | `deployment.request` | 202 + workflow id |
| `POST /api/v1/deployments/{id}/approvals` | `deployment.approve` | frozen spec hash check |
| `POST /api/v1/deployments/{id}/rollback` | `deployment.rollback` | new controlled operation |
| `GET /api/v1/deployments/{id}` | `release.read` or `project.read` | scoped status projection |

## Compatibility
Nonbreaking additive fields in v1, breaking semantic changes require new version and migration plan. JSON error schema at [error codes](error-codes.md). Caching/ETag must not cross tenant boundary.
