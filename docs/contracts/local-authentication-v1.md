---
id: CTR-LOCAL-AUTH-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-20
---

# Local authentication v1 — IAM owned, browser consumed

Owner: [FEAT-IAM-002](../features/identity-access/features/local-account-session.md). Consumer: local API and independently developed Web client. Wire `schemaVersion: 1`; `/api/v1` is the route version. This is an accepted but unimplemented contract under [ADR-0012](../decisions/0012-operator-pilot-runtime-foundation.md), approved for Foundation T01–T07 on 2026-10-20. It replaces no adopted positive authentication API. Machine/Agent identity and production/public ingress are excluded.

## Browser and transport policy

Pilot origin is exactly `https://localhost:5128`; the listener binds IPv4 loopback and checks Host/authority before authentication. Reject Origin values outside the configured exact origin, wildcard/null origins, arbitrary forwarded host and credentialed cross-origin requests. Same-origin development proxy/static serving is the client integration path; neither a clients build nor broad CORS is enabled by this document. Unsafe browser requests require the framework antiforgery cookie/request-token pair, including login/logout; the request token is sent as `X-Kordra-CSRF`. A GET token response never authorizes a state change and is `Cache-Control: no-store`.

Cookie name `__Host-kordra.session`, `Secure`, `HttpOnly`, `SameSite=Strict`, `Path=/`, no Domain. Framework Data Protection protects the ticket; claims are only principal reference, random session reference, authentication time and boot generation. Never put tenant/role/grant authority in the ticket. Store only a hash of the 256-bit cryptographically random session reference server-side; do not log the reference. No bearer token in localStorage, response JSON or URLs. API authentication failures produce JSON/empty status, not redirects to HTML login.

Cookie keys are explicitly boot-scoped for this first increment: a restart invalidates all sessions and completes audited startup generation before admission. Account/password/audit storage remains durable. Persistent Data Protection key storage and multi-host cookie sharing require a later key-custody design; do not silently enable filesystem key persistence.

Configure an explicit ephemeral framework Data Protection provider; the framework's normal key discovery/persistence defaults are not acceptable evidence that keys are boot-scoped. Test process restart with a captured synthetic cookie. Hashing runs outside the authority writer critical section; login/password mutations acquire the shared technical gate and recheck the credential/security revision before their state/audit commit. Thus expensive hashing does not extend revocation's gate-held budget.

## Routes and shapes

All JSON responses include `schemaVersion` and a server-generated `correlationId`; successful identity bodies below are allowlists. No client-submitted principal/session/tenant ID selects authentication authority.

| Method / route | Request | Result | Authentication / retry |
|---|---|---|---|
| GET `/api/v1/auth/csrf` | none | 200 `{schemaVersion, correlationId, requestToken}` plus antiforgery cookie | Anonymous; no-store; no session created |
| POST `/api/v1/auth/login` | `{username, password}` + CSRF | 200 `{schemaVersion, correlationId, principalId, sessionExpiresAtUtc}` and cookie | Credential verification; no automatic client retry |
| GET `/api/v1/auth/me` | session cookie | 200 `{schemaVersion, correlationId, principalId, username, sessionExpiresAtUtc}` | Current self; no-store; unavailable authority denies |
| POST `/api/v1/auth/logout` | CSRF, no subject body | 204, clear cookie after confirmed current-session revoke | Current self; unknown revoke outcome is unavailable, not success |
| POST `/api/v1/auth/logout-all` | CSRF, `Idempotency-Key`, empty body | 204 after current principal's sessions revoked | Current self; request ID maps only to this subject/action |
| POST `/api/v1/auth/password` | `{currentPassword, newPassword}` + CSRF, `Idempotency-Key` | 204 after password update and all-session revoke, cookie cleared | Current self plus current password; fresh login required |

Error envelope: `{schemaVersion:1, correlationId, code, message}`. Credential errors use 401 `AUTHENTICATION_FAILED` for wrong/unknown/disabled/locked accounts without public reason distinction; absent/invalid session uses 401 `SESSION_INVALID`. Origin/CSRF failure uses 403 `REQUEST_REJECTED`; rate limiting uses 429 `RATE_LIMITED` with bounded Retry-After; validation uses 400 `INVALID_REQUEST`; unavailable storage/audit/time uses 503 `AUTHORITY_UNAVAILABLE`. Public bodies contain no internal exception, authority source health, foreign identifier or raw input. These accepted error additions are linked from the existing error catalog during implementation; existing adopted errors retain their shape.

Protected resource endpoints separately use opaque 403 `AUTH_DENIED` for missing/foreign/invalid resource authority; login errors must not become a resource-existence oracle. Versioned route/schema fixtures are delivered to the client session; no browser/UI build is needed to validate backend HTTP contracts.

## Approved first-increment bounds — not measured SLOs

| Setting | Accepted value / rule |
|---|---|
| Username | 3–64 ASCII characters from letters/digits/`._-`; case-insensitive unique normalized form; display preserves original spelling |
| Password | 15–128 Unicode scalar values, at most 512 UTF-8 bytes; no truncation/normalization or forced composition rule; no echo in validation; enrollment/change/recovery consult an offline common-password denylist |
| Password hashing | Framework IdentityV3 hasher; PBKDF2-HMAC-SHA512, at least 220,000 iterations, framework random salts; verify selected package behavior and benchmark before merge; rehash on successful verification when required |
| Session | Absolute 8 hours, idle 30 minutes; no absolute extension; expiry equality denies; activity refresh is atomic and never alters revoked/expired state |
| Sessions per principal | Maximum 5 active; new login beyond limit fails safely until explicit logout/revoke; no silent session eviction |
| Lockout | 5 failed password checks → 15 minutes; unknown account follows a bounded dummy-hash path, not a new database account |
| Login rate | Global 20 attempts/minute, normalized-account bucket 5/minute; bounded 1,024 buckets with expiry, reject overflow; 2 concurrent hash operations, queue length 0; no forwarded-IP trust |
| Request / storage | JSON body at most 8 KiB; total authentication request deadline 5 seconds; DB command deadline 2 seconds; audit timeout follows authority/admission contract |
| Time | UTC and monotonic clock checked together; more than 1 second observed rollback/disagreement closes new protected admissions until operator reconciliation/restart; no backward-clock lifetime extension |

These are approved pilot policy limits, not an assertion that latency or resistance is proven. If hash cost cannot meet the request budget, report measurements and propose a reviewed adjustment; do not lower the work factor silently. Rate state resets on restart; this is a local-pilot limit, not distributed abuse protection.

The denylist adapter consumes a versioned local artifact with publisher/license/hash provenance recorded during T01, or an operator-provisioned private artifact with pinned hash. Implementation tests use a tiny synthetic list. No remote password query, plaintext telemetry or invented full-coverage claim is allowed; missing/invalid list prevents password enrollment/change/recovery. This is explicit runtime configuration/evidence, not permission to install an unreviewed dependency or silently skip screening.

## Owned data and atomic mutations

IAM account references include principal ID, normalized username, password hash, framework security/concurrency stamps, active/disabled status and revision. Session rows include session-reference hash, principal ID/revision, boot generation, created/last-activity/absolute-expiry timestamps and revoked state/reason. No tenant ID belongs to a global account/session. Tenant membership is resolved independently.

Password change/recovery/disable, session invalidation and required AUD append use one enlisted PostgreSQL transaction through owner handlers. AUD owns its insert; IAM does not manipulate AUD tables. A failed or unknown transaction cannot issue a success cookie. Logout-all and command retries do not store passwords or password-derived request hashes in audit/idempotency rows; duplicate command IDs cannot select a new subject or mutate a second time. A crash after commit but before HTTP response is resolved from the nonsecret command record after reauthentication or by offline maintainer tooling.

Offline `enroll-user`, `recover-user` and `disable-user` run through typed IAM owner handlers with the API stopped, an exclusive local process lock and explicit principal/account selection. Password input is hidden standard input, never argv/env/log output. Recovery leaves Disabled accounts disabled and revokes existing sessions; enablement requires a distinct reviewed administrative action. No magic account name, seed password or HTTP bootstrap endpoint exists. The tool returns a safe principal reference/correlation and outcome only.

## Compatibility and validation

Schema additions must preserve v1 semantics; breaking authentication/session changes require consumer migration and cookie/session generation invalidation. Real PostgreSQL account/session/audit tests, fake clocks, concurrency barriers and loopback HTTPS HTTP tests are mandatory for implementation. Synthetic IAM proof remains separate. Test wrong Host/Origin/CSRF, malformed cookies, restart, same-account races, audit failure and secret markers in response/logs. No real company credentials or account password is required for those tests.

## Primary basis

[Microsoft Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [cookie validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) and [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) provide framework mechanisms; the values above are Kordra's approved pilot policy. [OWASP password storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) lists algorithm-specific work factors; no security certification is inferred from selecting one.

## PR #10 implementation correction — 2026-10-22

The v1 wire/schema and accepted rules above are unchanged. The implementation resolves a committed command key only after validating a current self session. Matching principal/action returns its nonsecret recorded success without another password change, revoke or audit mutation; another subject/action conflicts. Password-command recovery after fresh authentication does not recheck the superseded password. It is a result lookup, not permission to execute a new command. Session activity refresh uses a PostgreSQL-side maximum so concurrent updates cannot move the timestamp backward. Tests cover these paths through real isolated PostgreSQL and loopback HTTPS, including restart and unauthenticated replay. See the owning feature for current evidence.
