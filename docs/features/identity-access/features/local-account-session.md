---
id: FEAT-IAM-002
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-20
module_id: MOD-IAM
feature_id: FEAT-IAM-002
phase: Foundation / M1-A
source_paths: [".github/workflows/product-ci.yml", "Kordra.slnx", "src/Modules/IdentityAccess/Kordra.Modules.IdentityAccess.csproj", "src/Modules/IdentityAccess/packages.lock.json", "src/Modules/IdentityAccess/Infrastructure/KordraIdentityDbContext.cs", "src/Modules/IdentityAccess/Infrastructure/KordraIdentityUser.cs", "src/Modules/IdentityAccess/Infrastructure/KordraSession.cs", "src/Modules/IdentityAccess/Infrastructure/IdentityAccessDbContextFactory.cs", "src/Modules/IdentityAccess/Infrastructure/Migrations/InitialIdentityPersistence.cs", "src/Modules/IdentityAccess/Infrastructure/Migrations/RestrictIdentityMigrationHistory.cs", "src/Modules/IdentityAccess/Infrastructure/Migrations/KordraIdentityDbContextModelSnapshot.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/IdentityAccessMigrationCommand.cs"]
test_paths: ["tests/IdentityAccess.Tests/IdentityAccess.Tests.csproj", "tests/IdentityAccess.Tests/packages.lock.json", "tests/IdentityAccess.Tests/IdentityAccessPersistenceTests.cs", "tests/IdentityAccess.Tests/TestDatabaseConfiguration.cs"]
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

T01 implements the exact IAM persistence model, additive migration/snapshot, local role-checked DbContext factory, and separate offline IAM migrator listed in `source_paths`. The schema is `app`; IAM tables use the `iam_` prefix and contain no tenant, assignment, or audit-owned records. `Kordra.Api` remains unchanged and does not reference IAM persistence or the migrator. The migration rollback path refuses to drop account/session data.

`IdentityAccessPersistenceTests.cs` maps the allowed local database target, IAM-only model, API/migrator assembly boundary, role separation, isolated PostgreSQL round trip, and a read-only privilege probe for the runtime role. Locked restore and Release build passed with zero warnings/errors; the 16 IAM tests passed against the isolated `kordra_test` database, including the runtime-role catalog probe. The round-trip test applies the additive migration and uses a transaction with synthetic rows; it never resets the database. `dotnet-ef migrations has-pending-model-changes` found no unscaffolded model changes. The full local AMR gates also passed: 159 tooling tests, 48 Artifact tests, 85 Inventory Bootstrap tests, 16 IAM tests, strict docs, scope, and source mapping. This is local source/test evidence, not hosted CI. The original attempted native T01 checkpoint remains failed and unreplayed; the separate [T01 evidence-recovery successor](../../../engineering/traceability.md#feat-iam-002-t01-persistence--current-implementation-evidence-2026-10-20) is completed from exact current artifacts and gate evidence. Login/session behavior, audit writes, tenant/project/target ownership, authorization, and positive inventory remain unimplemented and out of T01 scope. Relevant [Luna tasks T01–T03](../../../engineering/luna-implementation-handoff.md) retain their dependency order.

The T01 follow-up adds a model-only regression for the exact IAM entity/column allowlist and ignored Identity fields, bringing the suite to 17 cases. Missing private database locators fail explicitly rather than using an unsupported dynamic-skip marker under xUnit 2.9; database and runtime-privilege verification remain mandatory. The maintainer requested direct continuation with `gpt-6.1-sol/low` for this task only. Current deterministic results are retained separately from the historical failed native checkpoints; this does not change AMR routing, contracts, production behavior or dependency scope.

## Open decisions, ADR and debt

ADR-0012 is the concrete first implementation decision. No additional provider/library is left for Luna to invent. Package incompatibility, an unproved session/audit race or unsupported local HTTPS/key behavior blocks the affected task with evidence. Multi-host identity, persistent sessions across restart, public recovery and production MFA are later explicitly designed scope; this feature cannot establish production readiness. No existing debt is closed by this design.


## Review commit boundaries — T01 and T02

T01 adds only IAM principal/session persistence, the explicit IAM migration command and its 17 tests. Product CI provisions a disposable PostgreSQL 15 fixture with separate synthetic roles/databases; trust authentication is restricted to that ephemeral runner service, bound to loopback, and requires no committed password or developer credential. Local tests retain the private locator and exact-target guards. Hosted CI execution remains pending until the commits are pushed.

T02 adds the independent AUD migration command and project reference to the shared migrator while preserving IAM command/model ownership. The API remains without an IAM/AUD runtime dependency. Source commits are separated by their independently validated project graphs; broader authentication behavior starts at T03.

## PR #9 correction — IAM migration-history ACL

PR9-IAM-HISTORY-001 addresses inherited runtime DML grants on `app.iam_ef_migrations_history`. A new additive corrective migration revokes all table privileges from PUBLIC and `kordra_app`, including databases that already applied the initial migration. IAM account/session data, the migration owner and authentication v1 semantics are unchanged. The regression recreates leaked ACLs inside a rolled-back isolated-test transaction, executes the corrective migration SQL, checks all seven table privileges, verifies owner access and repeated migration application. This is a scoped correction under ADR-0012, not a new permission model or production migration.
