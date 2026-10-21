---
id: FEAT-IAM-002
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-20
module_id: MOD-IAM
feature_id: FEAT-IAM-002
phase: Foundation / M1-A
source_paths: []
test_paths: []
---

# FEAT-IAM-002 — Local accounts and revocable browser sessions

## User story / outcome

Actor: local User; trusted LocalMaintainer for offline enrollment/recovery. A user can authenticate, inspect their own identity, change their password and end sessions. Login grants no tenant, target or platform authority. MOD-IAM owns accounts, credential hashes and sessions. The [delivery design](../../../architecture/control-plane-delivery-design.md) places this feature before the management/inventory/release milestones.

## Pre-conditions

ADR-0012 and the authentication v1 contract are accepted for implementation under Foundation T01–T07. Audit foundation and isolated migration validation must be available before successful login/session mutations. The maintainer supplies local database/TLS configuration through private locators, not source/docs or an AI-generated default password. Actor identity and project storage are distinct concerns.

## Business rules and invariants

- A unique normalized account identifier maps to exactly one principal; identifiers are not tenant membership. Account state is Active or Disabled; temporary login lockout is separate and does not grant/revoke entitlements.
- Use framework hashing/verification and rehash-on-login; no reversible password storage, homemade hashing or role-name authorization.
- A successful credential check creates a fresh random session reference and immutable absolute expiry. Every protected request resolves current active principal/session and the boot generation. Cookie claims do not carry authoritative roles, grants or tenant scope.
- Authentication endpoints cannot create or assign tenant/platform grants. Additional users and recovery are offline trusted-local operations for this first increment. No public registration, email recovery, OIDC, service account or Agent identity is adopted.
- Password change/recovery and principal disable revoke all existing sessions atomically with the account revision update and audit. Logout revokes the current session; logout-all revokes all sessions belonging to the caller. Login rechecks the principal revision before session commit, closing the password-change/disable race.

## Main flow

1. Maintainer runs reviewed migrations and offline enrollment with API stopped; hidden input is validated and hashed. Enrollment produces a safe principal reference and audit, not a tenant grant.
2. Browser obtains an antiforgery request token over the allowed HTTPS origin, then submits credentials with the required header.
3. IAM performs bounded credential checks, current-state validation and durable session/audit commit before issuing the cookie. Audit/DB uncertainty issues no cookie.
4. `/auth/me` resolves the current principal/session, returns bounded identity/session metadata and no cross-tenant resources.
5. Logout/password change revokes authority through the same ordered mutation boundary as protected resource access. Browser clears the cookie after confirmed revocation; if the outcome is unknown, client-side clearing does not claim server revocation succeeded.

## Alternative & failure flow

Unknown user, wrong password, disabled principal and active lockout use the same safe credential-error response. Missing/expired/revoked/tampered session gives 401 on self endpoints. DB/audit failure gives a bounded unavailable result without signing in. CSRF/origin/Host rejection occurs before credential/session mutation. Startup invalidates prior boot sessions before admitting pilot traffic. Crash after session commit but before cookie leaves an expiring undisclosed session, not a recovered cookie; replaying a login requires a fresh credential check. Offline recovery is audited and requires API shutdown; it never silently activates a disabled account.

## Authorization / tenant-scope review

| Operation | Actor / authority | Tenant and disclosure boundary |
|---|---|---|
| Login | Anonymous credential claimant + antiforgery/origin policy | No tenant resource lookup or entitlement; generic failure |
| Me, logout, logout-all, change password | Current authenticated principal/session; self-only action | Subject resolved from session, never a body principal ID; password change also verifies current password |
| Enroll/recover/disable | Explicit offline LocalMaintainer, local lock, private DB authority | Separate audited founding/recovery authority, no HTTP route or implicit platform/tenant grant |

Self-service authentication actions use subject ownership, not fabricated resource permission keys. Business endpoints continue to require catalog permission and trusted scope through FEAT-IAM-001.

## Commands, APIs, data and migration

The authentication contract owns `/api/v1/auth/*`, request/response shapes, v1 compatibility and numeric limits. IAM owns Identity account data plus `iam_sessions`; AUD owns audit records. Migrations are additive/offline with reviewed rollback. Offline account enrollment is not a message event; no candidate event is published until its owner/consumer is adopted in the message catalog. Credentials, full cookies, CSRF tokens and password hashes are never API/audit fields.

## Idempotency, timeouts, audit and metrics

Logout is idempotent for a known valid self context; ordinary replay after the cookie is no longer valid may return 401 and never resurrects it. Password changes use a command ID/body binding without storing raw password or a password-derived fingerprint in the idempotency table; retries resolve their recorded nonsecret command identity or require reauthentication. Offline enrollment is unique by normalized account identifier and provisioning manifest hash. Login is rate-limited and is not blindly retried by UI. Record safe success/denial/revocation/startup-generation events and bounded counts/latencies; username and principal identifiers are not unrestricted metric labels.

## Negative & recovery scenarios

Unknown account, credential stuffing, body subject substitution, cookie fixation/tampering, cross-origin and missing antiforgery requests, disabled principal, expiry equality, concurrent password-change/login, repeated logout, revoke/me interleavings, DB/audit ACK loss, restart, unavailable clock and safe logging. Wrong-tenant access is tested in the consumer and must remain denied regardless of a valid session. Expired deployment approval, worker crash and unknown Agent outcome have no deployment consumer here; applicable session/DB crashes are tested and the broader cases stay assigned to execution features.

## Acceptance criteria

| ID | Given / when | Then |
|---|---|---|
| AUTH-01 | Empty isolated IAM store; maintainer enrolls twice/concurrently | One principal for the normalized identifier; no default tenant/platform grant; conflict is safe and audited |
| AUTH-02 | Valid account and allowed origin; login then me | Fresh protected cookie, authoritative self identity and no role/tenant authority in claims |
| AUTH-03 | Unknown/wrong/disabled/locked account; repeated login | Uniform safe credential failure and enforced finite rate/lockout behavior |
| AUTH-04 | Missing CSRF, hostile Origin/Host, malformed body or oversized password | Rejected before mutation; no secret or account-existence response |
| AUTH-05 | Logout/password change/disable races a protected self operation | Current revoked authority cannot admit a new operation; password change cannot be undone by stale login |
| AUTH-06 | Audit/session commit has rejection/timeout/unknown outcome | No success cookie; reconcile recorded outcome without assuming success or replaying mutation |
| AUTH-07 | Restart the API with existing accounts/sessions | Account persists; prior cookie/session denies; new login succeeds; startup revocation is evidenced |
| AUTH-08 | Recover account offline, or replay a recovery command | API must be stopped; one auditable result; old sessions stay revoked; no plaintext credential output |
| AUTH-09 | Consumer requests another tenant/target using a valid session | FEAT-IAM-001 still denies; login has not bypassed inventory bootstrap |

## Source & test traceability

T01 persistence, T02 audit and T03 authentication behavior are approved for implementation under ADR-0012. No new product source/test mapping is claimed by this design commit. The implementation commits add exact mappings and validation evidence.

## Open decisions, ADR and debt

ADR-0012 is the concrete first implementation decision. No additional provider/library is left for Luna to invent. Package incompatibility, an unproved session/audit race or unsupported local HTTPS/key behavior blocks the affected task with evidence. Multi-host identity, persistent sessions across restart, public recovery and production MFA are later explicitly designed scope; this feature cannot establish production readiness. No existing debt is closed by this design.
