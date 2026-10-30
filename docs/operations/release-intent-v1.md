---
id: OPS-RELEASE-INTENT-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
---

# Local release intent operations

This runbook describes the T11.1 implementation in progress under ADR-0005 and the [release contract](../contracts/release-approval-v1.md). Local tests and an opt-in implementation do not establish production readiness or authorize a real migration, cluster, registry, deployment or secret access. A frozen release intent is not deployment authority.

## Prerequisites and activation

Complete the feature's deterministic gates and PR review first. For an installation, independently approve the exact operational database change, stop the API, confirm installation identity and custody, and take an appropriate verified backup. The owner may then use the existing DbMigrator with the private `KORDRA_RELEASE_MIGRATION_CONNECTION_STRING` locator for `release-management`; the role must be `kordra_admin` and the approved target must be loopback `kordra`. Never put locators or passwords in source, logs, or this runbook. Do not execute destructive down migrations or delete history.

The local-pilot API accepts paired private `KORDRA_RELEASE_CONNECTION_STRING` and `KORDRA_APPROVAL_CONNECTION_STRING` runtime locators, both pointing to the approved loopback `kordra` database as `kordra_app`. Omit both to leave routes disabled. Supplying only one is an error. The server checks minimum schema, guards and privileges without running DDL; missing readiness makes release requests unavailable. No permission assignment or approval policy is created automatically.

Use authenticated project-scoped release permissions and a current session. Unsafe requests require exact Origin, CSRF, a unique `Idempotency-Key`, and a strong `If-Match` where specified by the [OpenAPI](../contracts/openapi/release-intent-v1.openapi.json). Never reuse an idempotency key for different business input. A replay retrieves the current authorized state; it does not authorize execution.

## Unknown outcome and recovery

An unknown commit or handoff closes the in-process admission gate. Stop the affected API and retain the private original request identity, boot identity and process identifier. Do not retry a mutation until the owner and audit evidence has been reconciled. The stopped recovery plan contains only the exact identifiers and hashes specified in the contract; do not include a password, raw idempotency key, session token or client payload.

With separate operational authorization and canonical stopped-installation custody, run `release-recovery inspect <private-plan> <private-admin-locator>`. Inspect returns a bound plan and SHA-256 only when it can establish `confirmed` or `no_commit`. Preserve the bound plan privately, then run `release-recovery apply <private-bound-plan> <private-admin-locator> <approved-sha256>`. Apply rechecks stopped custody, exact owner state and AUD evidence; it reports `confirmed`, `no_commit`, `conflict` or `unknown` and never writes a release or creates an approval. A mismatched or missing audit/owner record remains `unknown`; do not treat it as permission to retry. The plan hash binds content and is not evidence of human approval.

## Verification limits

Local component tests use synthetic data in `kordra_test`. An injected client-stream fault discards an actual PostgreSQL commit response and verifies the durable owner/audit outcome through a separate connection; it is not a real network outage. Actual HTTPS tests use isolated loopback fixtures. The isolated macOS lock and real process checker are exercised with a test-owned child process before and after exit. Full command acceptance, fresh final gates, PR review and merge remain separate; this document does not establish completion.

## Approval routes in the in-progress local composition

The existing paired release/APR runtime opt-in now also composes `/api/v1/environments/{environmentId}/release-approvals` and its detail/grant/reject/revoke routes, as specified in the [approval OpenAPI](../contracts/openapi/release-approval-v1.openapi.json). PRJ resolves parent scope; callers do not supply a tenant or project. Existing session/CSRF/idempotency and exact `apr-v1` preconditions apply. Approval eligibility and audit are checked before response handoff; a stored Granted state is not execution authority.

The APR request/decision migrations must be applied only under separately authorized stopped-installation migration procedures. The runtime does not perform DDL. APR readiness checks schema guards, runtime ACLs and installation request/decision/receipt/AUD agreement and returns unavailable on mismatch; do not repair by deleting history. Canonical routes have isolated HTTPS evidence, while recovery/new-process/full feature conformance remains incomplete. No installation change is authorized by this runbook.

## Approval recovery implementation and remaining acceptance

The in-progress `approval-recovery inspect <private-plan> <private-admin-locator>` and `approval-recovery apply <private-bound-plan> <private-admin-locator> <approved-sha256>` entrypoints follow the same canonical stopped-installation custody and private-file requirements as release recovery. Use an APR action/route plan only; REL plans are rejected. Inspect binds exact APR owner/decision/receipt and AUD evidence or an integrity-checked no-commit snapshot. Apply rechecks the exact plan hash, old-process quiescence, custody and unchanged evidence; it never creates a decision or retries an operation.

The accepted plan hash domain remains `release-recovery-plan-v1`; APR receipt/audit evidence has its own version-one hash domains. Private receipt evidence binds the full retained APR owner/history plus referenced REL identity/state/spec hash. Missing/contradictory data stays unknown. Component/stopped-server fixture checks have passed, but installed CLI execution and native custody/new-process acceptance have not yet been completed; do not treat this section as operational authorization.

The isolated APR acceptance fixture now drops the actual PostgreSQL grant COMMIT reply at the client-stream boundary, verifies the API returns no success body and fences admission, then stops the API fixture and reconciles the durable grant without replay. This does not prove behavior under every real network failure or replace the still-required native installed-command/custody and new-process acceptance.

Additional isolated acceptance now covers concurrent grant/reject (one winner), precommit audit failure (no decision, healthy admission, no_commit recovery) and a macOS native fixture installation lock with real child-process alive/exited checks. These improve component/native-lock evidence but do not constitute successful installed-command execution or a fresh API process startup test.

For full isolated Product validation, provide both `KORDRA_IAM_TEST_CONNECTION_STRING` and `KORDRA_AUDIT_TEST_CONNECTION_STRING` with the approved exact private kordra_test locator; neither is a runtime or migration credential. Native Keychain validation additionally requires its explicitly approved UUID fixture root. An omitted Audit locator causes required tests to fail and must not be treated as a skipped check.


## Authorized local bootstrap evidence — 2026-10-29

After the owner's separate explicit local operational approval, the existing DbMigrator applied all nine accepted owner migration sets to the approved loopback `kordra` target as `kordra_admin`, under a newly created private canonical installation identity and exclusive migration custody. Preflight found no app tables and no other database sessions. A private custom-format backup was listed and fully extracted successfully before the change; this is archive verification, not a restore rehearsal. No destructive Down, trigger bypass, role creation, user enrollment, grant, provider connection or existing Keychain secret access occurred.

Native installed REL/APR recovery executables now have successful `inspect`/hash-bound `apply` evidence for synthetic `no_commit` plans. Both reject a live test-owned old process and an incorrect approved hash. This closes the previously missing installed-command no-commit path; confirmed mutation recovery remains covered by the isolated durable-owner fixture, not a live installation mutation. Runtime catalog probes confirm no database/schema CREATE rights and no immutable REL/APR-history mutation rights; both owner datasets remain empty.

Fresh installed API startup correctly rejects a new self-signed localhost certificate that macOS does not trust. Native trust validation remains unchanged. A separate owner decision on temporary SSL/localhost-only user trust is pending; no trust-store change has occurred. Successful installed startup/restart and the final conformance/publication gate therefore remain pending. The temporary configuration has a synthetic startup-only denylist, permits no account enrollment and enables no external provider. It is acceptance configuration, not a production-ready identity installation. Private bootstrap/backup/CLI receipts are retained outside Git; certificate/private locator contents and installation identity are not published.


## Installed certificate verification follow-up — 2026-10-30

The separately approved temporary user-domain SSL/localhost trust operation completed after native macOS authentication. Native SSL verification then passed, but the unchanged installed .NET certificate-chain check rejected startup with `UntrustedRoot`; the certificate had a private key and matched localhost. This is not a completed startup/restart test. .NET 10's [Apple chain implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/native/libs/System.Security.Cryptography.Native.Apple/pal_x509chain.c) creates a Basic X.509 policy; the observed SSL-only trust result must not be substituted for a successful .NET chain result.

The temporary SSL trust was removed successfully and native verification rejected the certificate again; the same no-trust state was rechecked on resume. No trust operation or API remains running. The private certificate/configuration is retained for bounded acceptance, with no account enrollment, grant, external provider or production claim. Historical pending decision, resolved by the subsequent explicit owner approval: the owner had not approved temporary Basic X.509 trust: the previous response requested a pause. Ordinary GOAL continuation does not expand trust authority. Keep startup/restart and final feature publication pending; do not disable validation, silently broaden trust, rerun applied migrations, or treat old deterministic receipts as fresh evidence.


## Current local installed acceptance — 2026-10-30

The owner explicitly approved temporary Basic X.509 user trust for the exact new, non-CA localhost acceptance certificate after the SSL-only trust limitation was demonstrated. With the unchanged native chain/hostname/EKU/validity checks, the installed `Kordra.Api --profile local-pilot` executable completed two distinct process starts and clean shutdowns. Each served verified HTTPS `/health/live` (200), denied anonymous REL/APR route requests (401), and excluded recovery while runtime custody was held. Read-only durable evidence shows two distinct startup audit invocations and one persisted installation clock floor; no principals, grants, release revisions or approval requests were created. Authenticated business transitions remain evidenced by the isolated HTTPS/current-owner tests, not these anonymous installation probes.

The exact temporary trust was removed immediately afterward. Native Basic and SSL verification both reject the certificate again, and no API listener remains. The private startup-only configuration/certificate remains offline; it is not a usable unattended or production-ready installation. This resolves the earlier pending Basic-trust decision and native startup/restart evidence gap without modifying TLS validation or runtime architecture. Earlier progress paragraphs are dated evidence snapshots, not current blockers. The separately scoped live Nomad case remains outside this acceptance. Final content gates, hosted CI/Codex Auto PR Review and human merge are separate and must not be inferred from these operational checks.
