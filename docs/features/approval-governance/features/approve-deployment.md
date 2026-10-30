---
id: FEAT-APR-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-APR
feature_id: FEAT-APR-001
phase: MVP
source_paths: ["src/Host/Kordra.Api/OfflineAdministration/Approvals/ApprovalRecoveryPlan.cs", "src/Host/Kordra.Api/OfflineAdministration/Approvals/OfflineApprovalRecoveryCoordinator.cs", "src/Host/Kordra.Api/OfflineAdministration/Approvals/LocalApprovalRecoveryCommand.cs", "src/Modules/ApprovalGovernance/ApprovalRecoveryReceiptReader.cs", "src/Host/Kordra.Api/Endpoints/Approvals/ApprovalEndpoints.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalRuntimeServices.cs", "src/Modules/ProjectEnvironment/Contracts/CatalogContracts.cs", "src/Modules/ProjectEnvironment/ReleasePolicyEnvironmentReader.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalSchemaReadiness.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalMutationExecutionCoordinator.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalReadCoordinator.cs", "src/Host/Kordra.Api/Endpoints/Approvals/ApprovalCallerBoundResponseCommitter.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalPayloadBuilder.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalMutationDisclosure.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalEligibilityReader.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalMutationTransaction.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalSubjectReader.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalUseAdmission.cs", "src/Host/Kordra.Api/Endpoints/Approvals/ApprovalRequestParser.cs", "src/Modules/ApprovalGovernance/Domain/ApprovalDecisionTransition.cs", "src/Modules/ApprovalGovernance/Domain/ApprovalDeadlines.cs", "src/Modules/ApprovalGovernance/Domain/ApprovalEffectiveState.cs", "src/Modules/ApprovalGovernance/ApprovalRequestStore.cs", "src/Modules/ApprovalGovernance/ApprovalRequestQueryStore.cs", "src/Modules/ApprovalGovernance/ApprovalRequestReceiptStore.cs", "src/Modules/ApprovalGovernance/Infrastructure/ApprovalRequestEntities.cs", "src/Modules/ApprovalGovernance/Infrastructure/ApprovalDbContext.cs", "src/Modules/ApprovalGovernance/Infrastructure/ApprovalDbContextFactory.cs", "src/Modules/ApprovalGovernance/Infrastructure/Migrations/20261029074802_AddApprovalRequests.cs", "src/Modules/ApprovalGovernance/Infrastructure/Migrations/20261029074802_AddApprovalRequests.Designer.cs", "src/Modules/ApprovalGovernance/Infrastructure/Migrations/20261029075200_RequireApprovalDecisionConsistency.cs", "src/Modules/ApprovalGovernance/Infrastructure/Migrations/20261029075400_FixApprovalConsistencyTrigger.cs", "src/Modules/ApprovalGovernance/Infrastructure/Migrations/ApprovalDbContextModelSnapshot.cs", "src/Modules/IdentityAccess/Contracts/DurablePrincipal.cs", "src/Modules/IdentityAccess/Authentication/DurablePrincipalReader.cs", "src/Modules/IdentityAccess/Authorization/DurableApprovalAuthorityReader.cs", "src/Modules/IdentityAccess/Authorization/ApprovalAuthorityEvidence.cs", "src/Modules/IdentityAccess/Authorization/ApprovalAuthorityEvidenceCodec.cs", "src/Modules/IdentityAccess/Authorization/StoredApprovalAuthorityReader.cs", "src/Modules/IdentityAccess/Authorization/ApprovalAuthorityCapture.cs", "src/Host/Kordra.Api/Composition/Approvals/ApprovalEnvironmentAuthorityReader.cs"]
test_paths: ["tests/IdentityAccess.Tests/ReleaseMutationTransactionTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ApprovalRecoveryPlanTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ApprovalMutationExecutionCoordinatorTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ApprovalCallerBoundResponseCommitterTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ApprovalEligibilityReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ApprovalRequestParserTests.cs", "tests/ApprovalGovernance.Domain.Tests/ApprovalDecisionTransitionTests.cs", "tests/ApprovalGovernance.Domain.Tests/ApprovalDeadlinesTests.cs", "tests/ApprovalGovernance.Domain.Tests/ApprovalEffectiveStateTests.cs", "tests/ApprovalGovernance.Tests/ApprovalRequestPersistenceTests.cs", "tests/IdentityAccess.Tests/DurablePrincipalReaderTests.cs", "tests/IdentityAccess.Tests/DurableApprovalAuthorityReaderTests.cs", "tests/IdentityAccess.Tests/ApprovalAuthorityEvidenceTests.cs", "tests/IdentityAccess.Tests/ApprovalAuthorityEvidenceCodecTests.cs", "tests/IdentityAccess.Tests/ApprovalAuthorityCaptureTests.cs", "tests/IdentityAccess.Tests/ReleaseHttpTests.cs", "tests/ProjectEnvironment.Tests/ReleaseCatalogReaderTests.cs"]
planned_source_paths: ["src/Modules/ApprovalGovernance/Domain/", "src/Modules/ApprovalGovernance/Contracts/", "src/Modules/ApprovalGovernance/Kordra.Modules.ApprovalGovernance.csproj", "src/Modules/ApprovalGovernance/Infrastructure/", "src/Modules/ApprovalGovernance/ApprovalRequestStore.cs", "src/Modules/ApprovalGovernance/ApprovalEligibility.cs", "src/Host/Kordra.Api/Composition/Approvals/", "src/Host/Kordra.Api/Endpoints/Approvals/", "src/Host/Kordra.Api/OfflineAdministration/Approvals/"]
planned_test_paths: ["tests/ApprovalGovernance.Domain.Tests/", "tests/ApprovalGovernance.Tests/"]
---

# FEAT-APR-001 — Decide an immutable release intent with separation of duties

## Goal / Actor

An authenticated requester queues one exact Frozen release revision; an authorized approver reviews its exact subject and grants/rejects; an authorized actor can revoke it. This accepted T11.2 slice under [ADR-0005](../../../decisions/0005-immutable-release.md) uses [Release and approval v1](../../../contracts/release-approval-v1.md), not the unaccepted earlier deployment.approve sketch. NukeGeng is the slice custodian under accepted ADR-0005.

## User story / outcome

The Goal / Actor and main flow define the user-visible outcome; proposed status does not imply implemented behavior.

## Preconditions

Real FEAT-APR-002 policy/binding and FEAT-REL-001 Frozen source, current IAM/TEN/PRJ/INV/ART owner ports and qualified Development/Test pilot selection. No fake dependency or target.read permit.

## Pre-conditions

The Preconditions above are mandatory and are revalidated at use, not replaced by cached test fixtures.

## Business rules and invariants

The contract alone defines request/decision identity, one request ever per revision, exact frozen subject/hash/scope/policy, immutable decisions and continuing current eligibility. Independent means different principal IDs; explicit self_allowed is Development/Test only and requires all scoped capabilities. T11 uses release.approve; every result declares executionAuthorized=false. Original deadlines never extend or resurrect. Historical recorded state and fresh effective state differ explicitly; dependency unavailability is never a Granted fallback. Sessions are necessary for HTTP actions, not durable worker credentials.

## Main flow

Current request/subject checks → one Pending request + atomic AUD/floor → fresh exact-subject grant/reject with If-Match and reason → bounded authorized queue/detail. Revoke is a separate current-permission action. Approved content cannot be substituted; changed/retired/policy/authority revisions invalidate eligibility.

## Alternative & failure flow

Wrong tenant/scope, missing/denied/expired/revoked grant, unsupported qualification, self-approval under independent policy, blocked window and stale CAS do not grant. Concurrent decisions serialize, one Pending winner; later explicit revoke can invalidate a previously committed grant. Same key cannot grant twice. After terminal/expired/invalidated state, another request requires a new reviewed release revision. Unknown commit preserves exact history and fences, never guesses Granted/Rejected.

## Authorization + tenant-scope table

| Action | Current exact scope |
|---|---|
| Request | release.request-approval environment plus release.read project |
| Queue/detail | release.approval.read environment; bounded exact subject only |
| Grant/reject | release.approve plus release.approval.read environment; extra grant-only policy/requester checks |
| Revoke | release.approval.revoke plus release.approval.read environment; no actor-label bypass |

Policy cannot create any grant. Unknown/missing/conflicting owner facts deny, and complete deny rules apply independently per action.

## Authorization / tenant-scope review

The scoped authorization table above is mandatory. Tenant comes from trusted owner/session facts, never client body or a role label; missing/foreign/unavailable authority cannot produce success.

## Commands, events, APIs & schema versions

Exact HTTP v1 approval routes, fields/envelopes/ETags/error/body limits are in contract section 7. No approval.granted.v1 message, deployment endpoint, worker permit or workflow release is published. Later execution plan approval requires separately adopted semantics and immutable plan binding.

## Data ownership & migration

Second APR migration adds scoped requests, append-only decisions and command receipts to its own DbContext; REL/APR runtime references cannot form a cycle. Host maps neutral Contracts and controls one owner/AUD/floor commit. SQL enforces one request per revision, CAS/state/immutable decisions and tenant consistency.

## Idempotency, timeouts, compensation

Use contract-scoped canonical key/business hashes, exact receipt reconciliation and bounded current U/D. Confirmed M stays committed when D denies. Pre-commit setup failure leaves auth usable; actual unknown commit fences. No scheduler, automatic replay, grant restoration, rollback-to-old-policy or provider compensation.

## Health, audit, metrics, tracing

AUD stores safe decision/subject hashes and fixed codes only. No cookie, raw key, spec body, credential or private infrastructure detail. Expose honest effective eligibility and original deadlines, not deployable/healthy/productionApproved.

## Acceptance criteria (Given/When/Then)

- APR-F1: exact Frozen content, requester/approver current grants, policy/binding and original deadlines are bound to one durable request/decision.
- APR-F2: independent and explicit solo pilot paths behave as selected; Staging/Production qualification and execution stay blocked.
- APR-F3: changed owner/security revision, expiry equality, blocked window, restart and change-and-restore cannot resurrect eligibility.
- APR-F4: actual competing decisions, duplicate keys, partial rollback and true lost ACK have one authoritative history; no guessed success.
- APR-F5: real-session/current-owner HTTPS queue/detail and pre-header interleavings with exact schema/size/cursor/foreign-record checks pass; current gates and independent PR review precede human merge.

## Test cases (positive, negative, duplicate, crash, tenant isolation)

R11-02/03 and R11-05–16 in the contract, including stale assignment/role, retired release, unavailable policy, creator/requester/approver identity separation, clock rollback and both commit orderings. Future worker crash/delivery/dispatch tests remain T12/T13; do not simulate a fake worker success and claim them here.

## Negative & recovery scenarios

The alternative/failure flow and referenced R11 cases cover wrong tenant, missing/expired/revoked authority, duplicates, interrupted writes and unknown commit; worker/dispatch cases remain later scope. No destructive rollback or blind replay is permitted.

## Source/test/evidence mapping

2026-10-29 local T11.2 increment: the pure recorded-state transition table and closed reason-code validation are implemented with focused domain tests. These rules do not authorize, check current eligibility or time, persist decisions, create requests, expose HTTP, or approve execution. T11.2 remains incomplete and unpublished; its remaining owner, transactional, recovery, and HTTP requirements still apply.

The local Domain increment also calculates exclusive pending/grant deadlines from the applicable pilot policy, original assignment expiries and blocked windows, including independent versus explicitly self-allowed principals. Null expiry means an already-verified non-expiring assignment; this calculation does not prove that an assignment exists or remains valid. Owner evidence, trusted-clock monotonicity, durable eligibility, storage and enforcement are still required before using the result for a decision.

The local effective-state projection distinguishes terminal history, exclusive expiry, known invalidation, blocked windows and unavailable evidence. It takes already-resolved facts and never authorizes a reader or issues an execution permit. Current owner resolution, durable snapshots and HTTP composition remain unimplemented.

IAM now has a local neutral durable principal reader: under caller-held admission it returns the current principal revision, active status and a domain-separated security revision hash, without a session or raw security stamp. Known absence returns null; storage and malformed evidence failures propagate. Isolated PostgreSQL tests cover no session, disable/restore revision changes and unavailable storage. It is not wired to approval runtime; TEN grant snapshots, current authority comparison and enforcement remain outstanding.

The local IAM comparison reader now compares previously captured principal and two exact grant snapshots, verifies current role rules and original expiry, and never mints a sessionless action permit. A TEN-owned comparison port detects known absent or changed organization, membership, selected assignment and observed revoked, expired, absent or revision-changed provenance ancestors; malformed or ambiguous provenance remains unavailable. A separate IAM role evidence reader distinguishes missing, advanced or retired definitions from malformed rules and feeds the TEN/IAM comparison without changing existing role-read behavior. Isolated PostgreSQL tests exercise TEN cases, role changes and retirement, and the combined real IAM/TEN path for an active subject and two grants, then revocation of either grant or disabling the subject. Durable persisted snapshots and runtime wiring remain outstanding.

The stored-evidence reader decodes canonical bytes for the expected scope/principal and rechecks the exact persisted principal and grants through current IAM/TEN owner readers; malformed or mismatched evidence is unavailable. This input is not a sessionless permit. The local evidence shape validates exact requester project/environment and approver environment scope and rejects mixed tenant, principal, membership and revision facts. It is not a trusted capture mechanism by itself: session, held admission, current owner reads and authorization must precede its use, and product runtime composition remains incomplete.

A local versioned session-free evidence codec round-trips scoped facts, rejects unknown, duplicate, missing and noncanonical input, and matches an independently calculated domain-separated hash vector. A test-only HTTPS capture using real IAM/TEN/PRJ owners stages and reloads the evidence in an isolated PostgreSQL transaction before rollback. The codec does not itself prove current authority; production transaction composition and schema evolution remain outstanding.

Requester, approver and transient revoker capture now read the current session, project, environment, principal, both scoped grants and role definitions under caller-held admission and cap the handoff time. Focused capture cases use owner doubles; isolated PostgreSQL separately checks the PRJ adapter's active, foreign and deleted environment behavior. A test-only HTTPS probe exercises both captures with real session and IAM/TEN/PRJ owners and denies an anonymous caller. Transient capture also retains exact current subject, project and environment revisions for U/M/D comparison; recapture rejects a replaced session, changed project/environment revision or different operation. Session facts remain outside persisted evidence. Revoker capture requires exact release.approval.revoke plus release.approval.read grants and is excluded from durable requester/approver encoding. This is not a product endpoint, and the adapter is not yet registered. Transaction/audit, negative real-owner interleavings, owner consistency through commit and runtime composition remain outstanding; the capture result is not a permit.

The local APR second migration adds request, immutable decision and command-receipt tables with direct scope keys, version/uniqueness constraints and SQL history guards. Four isolated PostgreSQL tests cover duplicate revision, initial state, allowed grant/revoke and immutable owner evidence. This is an incomplete storage increment: coordinated decision/head consistency, canonical evidence encoding, cross-owner/audit transaction, concurrent/ACL/rollback verification and live readiness are still required. No product migration has run and no routes are enabled.

The owner store now stages request and decision/head changes only inside a caller-provided transaction. The APR context can enlist in the caller-owned connection after enforcing the existing local runtime/test target and role restrictions; isolated tests confirm shared rollback and reject three wrong target/role combinations. An isolated test with two database transactions confirms only one competing decision commits; PostgreSQL reports the losing insert as a unique-constraint violation, so the future coordinator must roll back and translate that exact race to conflict. Deferred SQL constraint triggers now reject an unmatched decision or request transition; a follow-up migration fixes a trigger defect observed in the isolated test database. Eleven storage tests pass locally. The owner receipt store stages scoped command evidence in the caller transaction and returns only candidate, absent, conflict or inconsistent; isolated PostgreSQL verifies owner/payload matching and cloned hashes. A candidate is not proof of audit, authority or commit. Store methods do not authorize, save or commit; shared AUD/floor, coordinator integration, deeper history/actor invariants, ACL and recovery remain outstanding.

A local owner query store now provides detached exact-scope request/history snapshots and pages of at most 50 requests, rejecting foreign cursors and using timestamp/ID ordering. Isolated PostgreSQL verifies scope, tied timestamps, pagination, decision history and detached evidence. These internal snapshots are not wire responses; current eligibility, reader authorization, bounded serialization and audit remain Host responsibilities.

The local approval parser implements closed request/grant/reject/revoke bodies, exact request-bound apr-v1 ETags, canonical routes, reason catalogs and body hashes using the accepted release-command-v1 scheme. Five focused parser cases include independent request/grant hash vectors and reject mixed-operation reasons, duplicate/unknown fields and wrong preconditions. This parser is not an enabled endpoint.

Local ApprovalUseAdmission now stages exact invocation/action/environment U evidence and the IAM clock floor on one transaction, rechecks the original authority selection and time, and returns an internally constructed one-use ticket only after durable U. The real HTTPS/PostgreSQL fixture verifies no ticket for missing permission, durable U, one-use consumption, healthy admission after precommit setup failure, and fencing after a simulated unknown result from an actually committed U. This is not a network-loss test. No APR mutation is performed by U, and M/D/HTTP composition remains incomplete.

The local request/grant subject reader resolves a scoped REL snapshot and fresh owner selection, requiring Frozen state and an unchanged spec hash; it returns the exact current permitting policy used by selection. It must not be used to require old policy permission for terminal/history reads. The real HTTPS/PostgreSQL fixture checks Draft/Retired rejection, foreign scope, eligible Frozen content and invalidation after environment revision change. This reader is an input to M, not an action permit or completed mutation coordinator.

The local unregistered M coordinator consumes the exact U ticket, matches scoped receipt/AUD replay evidence, stages request/grant/reject/revoke and receipt/AUD/floor on one shared transaction, and rechecks current actor, subject and grant deadlines before commit. APR uses approval-m- audit reasons so REL readiness does not mistake APR intents for orphan REL commits. The real-owner HTTPS/PostgreSQL fixture exercises request, exact replay, stale version, self-allowed grant, revoke and subsequent REL retirement, asserting retained decisions/receipts. Failure/interleaving coverage and readiness, D, HTTP and recovery composition remain incomplete.

The local eligibility reader now projects current state under caller-held admission from original deadlines, current exact subject/policy and durable requester/approver evidence, retaining terminal Rejected/Revoked and expiry-first behavior. It fails closed on invalid scope/time, unavailable facts and crossed policy-window boundaries, and returns a display validity boundary for later D. The real-owner fixture checks Pending/Granted/Invalidated/Revoked; focused cases check expiry and terminal states without requiring old dependencies and clock-before-creation denial. This is display eligibility, not execution or response authority; audited D and public endpoints remain uncomposed.

The local D component now builds a closed detail payload, rechecks the exact actor and fresh effective state, commits payload hash/size AUD evidence and the floor, and holds admission through a bounded response sink. Mutation outcomes have internal construction and bind their original U ticket, preventing a caller-created outcome from borrowing another U for disclosure. A test-owned sink verifies request 201/replay 200, closed non-authority payload, retained Revoked history and body withholding when the clock advances past authority validity immediately after D audit commit. This is an internal D/sink fixture, not the public caller-bound HTTP committer; wire schemas, route activation, read paths, failure coverage and recovery remain incomplete.

The caller-bound approval committer is now implemented for exact session/boot/environment/route/query/hash/deadline, monotonic preheader clock checks and one-shot handoff. Twelve component cases cover success and rejection; the real-owner fixture sends a replay detail through actual HTTPS using a test-only probe route, checking ETag, no-store and closed envelope. This is transport evidence for the committer, not activation of the contract approval routes, read/list coordination or complete wire conformance.

The unregistered read coordinator now uses one exact environment approval-read grant, fresh U/D authority and effective-state projection, bounded shared capacity and audited caller-bound handoff. The payload builder supports exact detail and at most 50 closed list items, omitting subject and authority snapshots from lists. Reader capture is transient and cannot be encoded as durable requester/approver evidence. Thirty-one focused/real-owner cases pass, including list/detail over the test-only HTTPS probe. The earlier fixture-only readiness adapter has been removed. The fixture now uses ApprovalSchemaReadiness for enabled guards, deferred consistency, validated constraints/uniqueness, least-privilege runtime columns and exact installation request/decision/receipt/AUD matching, including orphan intents. Valid history passes; missing receipt and missing audit in separate synthetic installation history fail closed. These read-only checks run without migration or repair. Contract routes and broader failure/scale conformance are still required.

ApprovalMutationExecutionCoordinator now bounds the complete U/M/D invocation with existing shared capacity, stage deadlines, readiness and unknown-outcome fencing. The real HTTPS fixture runs the full coordinator for replay. Two focused cases verify capacity/readiness denial and a still-running synthetic readiness task on cancellation; they do not constitute full M/D fault or concurrency coverage.

The approved local-pilot release opt-in now registers APR services and canonical approval routes, with source-bound OpenAPI/field fixtures. PRJ resolves unique active environment ownership without caller tenant/project under capacity/admission; collisions, including inactive foreign copies, deny. The real HTTPS fixture now uses product routes for request/list/detail/grant/revoke and rejects duplicate requests, terminal reject, missing session/CSRF, unknown environment and extra query scope. Probe-only evidence is not substituted for these route checks. Startup/new-process, recovery, full error/concurrency/scale conformance and final feature gates remain incomplete.

Local approval recovery now has a closed owner-specific plan parser, custody-bound receipt reader, read-only inspection/apply coordinator and command entrypoint. The plan keeps the accepted release-recovery-plan-v1 domain while its action/route family is restricted to APR. Exact owner/history/receipt/AUD and installation readiness are required; inconsistent history cannot certify no_commit. Seven parser/vector cases and a stopped-API PostgreSQL fixture pass for confirmed/no_commit, incorrect evidence and denied hash. Custody/process doubles are explicit in this fixture; installed-command/native stopped-custody acceptance, new-process startup and broader failure/concurrency conformance remain pending. Recovery does not create decisions, refresh grants or replay commands.

The canonical HTTPS flow now also injects a lost PostgreSQL COMMIT reply specifically for APR mutation, reusing the existing isolated REL transport fixture. It verifies COMMIT was sent and its actual socket reply discarded before driver acknowledgment, no HTTP success body, fenced admission and exactly one persisted grant/decision/receipt pair. After stopping the API fixture, approval recovery confirms the retained mutation without replay. Both ordinary and lost-reply variants pass. This is controlled client-stream injection, not an external network outage; native custody/process doubles remain explicit in recovery.

The canonical HTTPS fixture now concurrently issues grant/reject at one expected version and verifies one successful decision, one stale precondition, one version increment and no duplicate receipt/decision. A precommit audit-stage exception keeps Pending state, writes no decision/receipt and leaves admission healthy; stopped recovery reports no_commit. Four normal/lost-reply/race/precommit-failure variants pass. On macOS the normal case additionally exercises a UUID-isolated native installation lock and actual child-process quiescence, denying recovery while that child lives and confirming after exit. This isolated native fixture is not installed-command or fresh-process API acceptance.

No APR request/decision runtime exists at baseline. The exact design is accepted under ADR-0005; subsequent files and tests remain subject to its real policy/REL dependencies. Product/CI/review evidence for the complete feature remains pending.

## Source & test traceability

The actual mappings identify only the local domain increment; planned paths remain unimplemented until independently evidenced. Implementation must attach real commands, source revisions and test results to this feature and the shared traceability log before completion.

## Open decisions, linked ADR and technical debt

The bounded design is accepted under ADR-0005 for T11.0–T11.3; complete feature implementation remains unverified. Deployment approval/override/worker authority remains a separate future decision. No debt opened/closed merely by documenting it.

## Local wire implementation artifacts

The in-progress runtime is described by the [approval OpenAPI](../../../contracts/openapi/release-approval-v1.openapi.json) and [closed field fixture](../../../contracts/fixtures/release-approval-v1.http.json). These source-bound artifacts do not replace the acceptance cases or hosted review.

## Full-gate assessment checkpoint

The initial full assessment built successfully and passed docs/scope/mapping and 218 tooling tests. Product failures identified a missing Audit test locator in the invocation and stale host source hashes in T09 wire artifacts. With the explicit private kordra_test Audit locator, all 39 Audit cases pass; refreshing only the changed host hashes restores all five affected T09 cases. A fresh complete assessment remains required; partial fixes do not certify the feature. Native installed acceptance is also not established: read-only metadata shows no REL/APR tables in kordra and no local installation identity is configured. No real database migration or installation provisioning was performed.


T11 local contract conformance follow-up (2026-10-29): approval mutations now return 404 for a scoped absent/foreign request or absent release subject only after parent authorization. Stale/non-Frozen subjects return 409, policy/current-authority denial returns 403, and 422 remains reserved for qualification rejection; unrecognized eligibility failures remain unavailable. Canonical HTTPS variants cover all three absent decision routes, absent request subject and retired-subject conflict. No schemaVersion, permission, ADR or operational authority changed. The earlier full local gate passed (218 tooling; 1,555 Product passed and one optional live Nomad test skipped); final evidence must follow the changed source revision, not reuse that older success as current.


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


## PR21-REL-POLICY-001 — freeze error classification

Codex Auto PR Review identified that REL freeze flattened missing/expired/denying policy into qualification rejection. Four actual HTTPS regression cases reproduced three wrong 422 responses while the Staging qualification case correctly stayed 422. The scoped fix preserves missing policy as 409 conflict, expired/denying policy as 403 denial and qualification/profile rejection as 422; unrecognized eligibility failure stays unavailable. It does not create a revision mutation or extra command receipt on rejection. Policy bodies/hashes, permissions and ADR-0005 remain unchanged; the published wire schema and its existing source bindings remain unchanged; the HTTPS regressions verify the accepted error behavior. Current regression/gate evidence and hosted follow-up are recorded separately; the finding is not declared independently reviewed or resolved merely because a fix is present.
