---
id: ADR-0012
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-20
---

# ADR-0012 — Local identity and scoped Control Plane runtime foundation

**Status: Accepted for the exact Foundation T01–T07 scope on 2026-10-20.** The maintainer approved this ADR, local-authentication-v1, authority-admission-v1 and the corresponding Foundation owner slices T01–T07. This adopts the listed identity, persistence, local-session, ownership and single-process admission choices for implementation. It does not accept broad ADR-0009, release/execution-v1, messaging/Agent/provider implementations, production access, or the full OSS architecture.

## Context and alternatives

Kordra has a denied-ingress API and a test-only policy proof. The [delivery design](../architecture/control-plane-delivery-design.md) needs a real identity/authority foundation while preserving accepted ADR-0003, ADR-0010 and I2.7. Authentication is a separate feature, [FEAT-IAM-002](../features/identity-access/features/local-account-session.md).

| Decision | Alternatives and cost | Accepted choice |
|---|---|---|
| Local identity | Bespoke password/session stack increases security maintenance; external IdP adds deployment/account-provider dependency; custom Identity/Marten stores add adapter correctness work | ASP.NET Core Identity account/password facilities and framework cookie/Data Protection, with explicit Kordra server-side session checks |
| First persistence | Process memory loses accounts/audit; adopting Marten plus custom Identity stores adds security adapter scope; EF plus Marten immediately adds two persistence stacks | EF Core/Npgsql for first IAM/TEN/PRJ/INV/AUD records in PostgreSQL; other domain persistence remains undecided |
| Admission/revoke | Sequential rechecks/TTL do not close races; distributed fencing requires a larger topology/proof | One enforced API process, fair process-wide reader/writer admission gate and owner ports as specified in authority-admission-v1 |
| Browser transport | Bearer tokens in browser storage increase exposure; broad cross-origin cookies complicate trust and CSRF | Same-origin HTTPS, framework-protected HttpOnly cookie, server-side session authority, anti-forgery on unsafe requests |
| Recovery and key persistence | Public password recovery requires separate delivery identity; persisted key custody expands initial SEC scope | Offline local recovery with API stopped; boot-scoped cookie keys; all sessions invalidated on API restart, accounts/audit retained |

This accepted EF choice differs from the broader **proposed** Marten direction. No accepted persistence decision is replaced. It intentionally postpones Marten adoption until a business feature demonstrates a concrete need; Luna must not silently add both engines. Revisit when workflow persistence, cross-process authority or high-volume events are designed.

## Accepted decisions and ownership

1. Keep .NET 10, root solution and accepted source paths. Use these initial direct package versions: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` **10.0.11**, `Microsoft.EntityFrameworkCore.Design` **10.0.11** (development/migration assets only), `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**. Their public NuGet indexes were consulted on 2026-10-20; existence is not restore, compatibility or security evidence. T01 must lock the full transitive graph and run advisory/license/compatibility checks before merge. A failing baseline blocks merge; changing these packages needs an explicit decision.
2. Reuse the local ADR-0011 database/role boundaries. Schema `app`, table prefixes `iam_`, `ten_`, `prj_`, `inv_`, `aud_`; one owner DbContext per module. Migrations run offline with `kordra_admin`; runtime uses `kordra_app`; tests use an explicitly authorized isolated `kordra_test` target. No runtime startup migration, superuser connection, broad database reset or creation of other services. Credentials remain outside tracked files and command logs.
3. IAM owns accounts/sessions/RoleDefinition/policy evaluation. TEN owns membership and **scoped role assignments**; IAM interprets exact definition revisions. PRJ owns project/environment and target association; INV owns target identity/lifecycle and its side of that association. AUD owns append/read storage. Host composes contracts without circular runtime references or cross-owner table reads.
4. NukeGeng is accountable for the affected Foundation module/feature slices T01–T07. Earlier human custodian assignment is retained as history. This resolves responsibility in one decision rather than repeatedly requesting the same name; it does not claim independent multi-person security review.
5. Create only the runtime/Contracts projects needed by the current task under existing `src/Modules/<Module>/`, plus `tests/<Module>.Tests/` and a scoped `Kordra.DbMigrator` host when migrations are implemented. Framework/provider dependencies stay in owning runtime infrastructure; public Contracts remain BCL-only. Preserve Artifact Domain and Inventory Contracts/runtime behavior. A technical admission gate belongs in `src/BuildingBlocks/Security/` only when IAM enforcement needs it; it contains no business/policy resolution.
6. `Kordra.Api` retains existing default bootstrap behavior: HTTP loopback port 5127, health-only and inventory denied. An explicitly selected local-pilot profile adds HTTPS loopback on port 5128 with exact authority `localhost:5128`; it requires an operator-provisioned certificate and refuses missing/untrusted configuration rather than downgrading TLS. No wildcard bind, trusted forwarded-header shortcut, remote ingress or automatic certificate trust. No listener is started by this design task.
7. Session cookie contains protected identity/session references only, no membership or permissions. IAM reads current principal/session state on every protected request. No cookie refresh can resurrect a revoked/expired session. Restart invalidates prior sessions before the pilot listener admits requests. [Authentication contract](../contracts/local-authentication-v1.md) owns numeric/session/CSRF rules.
8. The first runtime consistency protocol is [authority/admission v1](../contracts/authority-admission-v1.md): all protected U/D boundaries and all invalidating writers participate in one process gate; expiry is checked at the commitment boundary. No API replica, independent authority-writing worker, remote recovery command, distributed lease claim or manual live DB mutation is supported by this protocol. It requires a replacement decision before those writers are introduced.
9. Mandatory audit intent is durably confirmed before U and D; it proves intent/eligibility only. The caller-bound bounded response handoff at D may leave bytes in transport after a later revocation. Approving this ADR explicitly accepts that residual and the bounded delay of an invalidating writer while an earlier D holds admission. There is no post-revocation new admission or stale-permit grace. Application streaming is excluded.
10. Offline provisioning/recovery is a distinct trusted local-maintainer operation with the API stopped and a local exclusive lock. It cannot be reached over HTTP. Bootstrap creates an account without implicit tenant/platform rights; workspace initialization assigns only explicitly listed catalog grants for explicitly listed resources, through owner handlers and audit. It is idempotent on manifest ID/hash. The ordinary assignment API cannot invoke founding authority or exceed its grantor ceiling.
11. First pilot data isolation is application-enforced with tenant-aware owner repositories and composite tenant/resource constraints; no PostgreSQL RLS is silently selected. Missing trusted tenant context fails closed; ORM query filters alone are insufficient authority. IAM accounts/sessions are explicitly global/self-scoped; tenant audit queries exclude global authentication/startup records. Database administrators remain trusted. Review stronger database isolation before public multi-tenant production adoption.

## Rollout, compatibility and rollback

- Existing clients have no adopted positive inventory endpoint. Keep old denied paths; the accepted new API contracts do not authorize a bypass. Positive ingress remains disabled until the T06/T07 enforcement proof passes.
- Implement accounts/audit first, then ownership/grants/enforcement, then credentials/read adapters. The [handoff](../engineering/luna-implementation-handoff.md) supplies exact dependencies, write boundaries and acceptance.
- Apply additive reviewed migrations to isolated test storage first. Preserve existing records and audit history; a migration failure leaves the pilot listener closed. Rollback can disable the local-pilot profile and restore the existing bootstrap without dropping databases or recreating revoked sessions.
- Restoring old authority/session data needs an offline security-generation change and forced reauthentication; database restore is not a valid way to reinstate revoked authority.
- Pin/review actual transitive packages and notices. No new provider SDK, Marten, Wolverine, broker, service, container or production schema is approved by this ADR.

## Required validation before implementation completion

T01–T07 must provide locked restore/build, meaningful account/tenant/authority/audit integration and concurrency/crash tests, public API fixtures, boundary tests, migrations/restart/rollback evidence, and the applicable deterministic gates. The admission protocol must demonstrate both revoke orderings, writer fairness, deadline expiry, audit ACK loss and response failure; an unproved handoff remains denied. Independent PR review is Codex Auto PR Review under AMR v4, separate from exact human design approval and human merge.

Adoption does not claim production security, tamper-proof storage against a DBA, constant-time network denial, or distributed revocation. Revisit before multi-host API, authority writers outside the process, ServiceAccount/Agent identity, public ingress, persistent sessions across restarts, streaming or production readiness.

## Decision record

**Accepted by the maintainer on 2026-10-20:** “Duyệt đúng phạm vi ADR-0012 và Foundation T01–T07”. The approval covers this ADR, local-authentication-v1, authority-admission-v1, and the T01–T07 slices in FEAT-IAM-001, FEAT-IAM-002, FEAT-TEN-001, FEAT-PRJ-001, FEAT-INV-001 and FEAT-AUD-001. Exact pre-acceptance SHA-256 values are retained in `.amr-local/approvals/ADR-0012-foundation-t01-t07-acceptance.json`; the original design handoff manifest also records all reviewed document hashes. This record changes decision status only; it is not source, test, migration, runtime, CI or deployment evidence. NukeGeng is accountable for these approved Foundation owner slices under decision 4; this does not claim independent multi-person review. The client handoff supplies a proposed integration boundary but was not included in the exact approval. Release-execution-v1, later provider/distributed-worker choices and ADR-0001/0002/0004/0005/0006/0009 remain separately proposed; this decision cannot accept them by reference. Do not request the already-given strategic or exact-scope approval again.

## Primary technical references

- [Microsoft Identity and configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0): framework account/lockout/hasher configuration; defaults are not the complete Kordra policy.
- [Microsoft cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0): current server validation is separate from possession of a cookie.
- [Microsoft antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0): browser credential requests require explicit CSRF handling.
- [Npgsql EF Core 10](https://www.npgsql.org/efcore/release-notes/10.0.html): provider family exists; Kordra integration remains unexecuted.
