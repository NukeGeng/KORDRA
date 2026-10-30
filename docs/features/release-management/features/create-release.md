---
id: FEAT-REL-001
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-REL
feature_id: FEAT-REL-001
phase: MVP
source_paths: ["src/Modules/ReleaseManagement/ReleaseStore.cs", "src/Modules/ReleaseManagement/ReleaseIntegrity.cs", "src/Modules/ReleaseManagement/ReleaseQueryStore.cs", "src/Modules/ReleaseManagement/ReleaseMutationStore.cs", "src/Modules/ReleaseManagement/ReleaseReceiptStore.cs", "src/Modules/ReleaseManagement/ReleaseRecoveryReceiptReader.cs", "src/Modules/ReleaseManagement/Contracts/", "src/Modules/ReleaseManagement/Infrastructure/", "src/Modules/ReleaseManagement/Kordra.Modules.ReleaseManagement.csproj", "src/Modules/ReleaseManagement/packages.lock.json", "src/Modules/ReleaseManagement/Domain/", "src/Host/Kordra.Api/Composition/Releases/", "src/Host/Kordra.Api/Endpoints/Releases/", "src/Host/Kordra.Api/OfflineAdministration/Releases/", "src/Host/Kordra.Api/Program.cs", "src/Host/Kordra.Api/Hosting/LocalApiHost.cs", "src/Host/Kordra.Api/Hosting/LocalPilotHostOptions.cs", "src/Host/Kordra.DbMigrator/ReleaseManagementMigrationCommand.cs", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/packages.lock.json"]
test_paths: ["tests/ReleaseManagement.Tests/", "tests/ReleaseManagement.Domain.Tests/", "tests/InfrastructureInventory.Bootstrap.Tests/ReleaseSelectionTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ReleaseRequestParserTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ReleaseCallerBoundResponseCommitterTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ReleaseRuntimeConfigurationTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ReleaseRecoveryPlanTests.cs", "tests/IdentityAccess.Tests/ReleaseMutationTransactionTests.cs", "tests/IdentityAccess.Tests/ReleaseHttpTests.cs"]
planned_source_paths: ["src/Modules/ReleaseManagement/Domain/", "src/Modules/ReleaseManagement/Contracts/", "src/Modules/ReleaseManagement/Kordra.Modules.ReleaseManagement.csproj", "src/Modules/ReleaseManagement/ReleaseStore.cs", "src/Modules/ReleaseManagement/ReleaseIntegrity.cs", "src/Modules/ReleaseManagement/ReleaseQueryStore.cs", "src/Modules/ReleaseManagement/ReleaseMutationStore.cs", "src/Modules/ReleaseManagement/ReleaseReceiptStore.cs", "src/Modules/ReleaseManagement/ReleaseRecoveryReceiptReader.cs", "src/Modules/ReleaseManagement/Infrastructure/", "src/Host/Kordra.Api/Composition/Releases/", "src/Host/Kordra.Api/Endpoints/Releases/", "src/Host/Kordra.Api/OfflineAdministration/Releases/", "src/Host/Kordra.DbMigrator/ReleaseManagementMigrationCommand.cs"]
planned_test_paths: ["tests/ReleaseManagement.Domain.Tests/", "tests/ReleaseManagement.Tests/"]
planned_doc_paths: ["docs/operations/release-intent-v1.md", "docs/contracts/openapi/release-intent-v1.openapi.json", "docs/contracts/fixtures/release-intent-v1.http.json", "docs/features/release-management/features/create-release.md", "docs/engineering/traceability.md", "docs/engineering/change-history.md"]
---

# FEAT-REL-001 — Review and freeze an immutable release intent

## Goal / Actor

User creates a project release Draft, reviews the current artifact/catalog/policy selection, freezes exact bytes and can explicitly retire obsolete revisions. T11.1 design was accepted under [ADR-0005](../../../decisions/0005-immutable-release.md) on 2026-10-27; implementation is in progress. [Release and approval v1](../../../contracts/release-approval-v1.md) is the sole normative T11 rule/HTTP source; future execution remains in the separate proposed release/execution contract.

## User story / outcome

The Goal / Actor and main flow define the user-visible outcome; proposed status does not imply implemented behavior.

## Preconditions

Merged T10 ART registration; current IAM/TEN and active PRJ application/environment/INV target chain; real APR policy feature T11.0 before a positive freeze. Draft selection may be recorded while policy/qualification is ineligible. Existing target.read policy/permit is not release authority. NukeGeng is proposed slice custodian, effective only on exact design acceptance.

## Pre-conditions

The Preconditions above are mandatory and are revalidated at use, not replaced by cached test fixtures.

## Business rules and invariants

The contract defines lineage/revision IDs, one Draft per lineage, creator-only Draft edit/freeze, exact CAS/preview hash, Draft → Frozen → Retired, byte-exact canonical hash and fixed qualification. Copy only immutable ART facts; no claim that source provenance, layers, signature, live availability or an execution plan is verified. No tag resolution, registry/Keychain read, configuration bag or dispatch. Frozen bytes/hash never change. Newer revisions never redirect an old approval; explicit retirement invalidates its current eligibility.

## Main flow

Create lineage/Draft → review server preview → optionally replace with exact If-Match → freeze with expectedSelectionHash → read exact Frozen subject/specHash. A change creates another exact revision. Retire only with explicit current permission and reason code. API response always states executionAuthorized=false.

## Alternative & failure flow

Missing/foreign/ambiguous owner data denies; unavailable data is unavailable, not empty. Owner/policy change since preview returns conflict. Invalid qualification blocks freeze; Staging/Production is not silently downgraded. Stale CAS or changed idempotent input is conflict. Unknown real commit fences; confirmed M survives a denied D and exact stopped receipt reconciliation reports only known facts.

## Authorization + tenant-scope table

| Action | Exact current permission / constraint |
|---|---|
| Create/next Draft | release.create project; trusted same-project owner selection |
| Replace/freeze | release.create project and current Draft creator; If-Match; no role shortcut |
| Read/list | release.read project; tenant from owner |
| Retire | release.retire project; explicit CAS/reason; no resurrection |

The contract defines use-specific neutral owner reads; no implicit catalog/provider read grant.

## Authorization / tenant-scope review

The scoped authorization table above is mandatory. Tenant comes from trusted owner/session facts, never client body or a role label; missing/foreign/unavailable authority cannot produce success.

## Commands, events, APIs & schema versions

New release HTTP v1 routes, exact envelopes, body/ETag/hash rules and bounds are in contract section 7. No event/outbox/message is published in T11. release.created.v1/deployment.requested.v1 remain future candidates; no 202 workflow promise.

## Data ownership & migration

REL owns lineage/revision/canonical bytes/hash/command receipts in its new owner DbContext. Reuse exact approved-on-acceptance EF/Npgsql versions only; pure Domain/BCL Contracts and Host owner composition. Additive isolated migration/ACL/immutable-SQL tests precede route activation. No runtime DDL or destructive Down.

## Idempotency, timeouts, compensation

Use exact contract domains, subject/action/scope/key hashes, one owner/AUD/floor transaction, current authority and caller-bound D. Preserve original receipts and frozen content; no blind replay, rollback of historical approval, remote compensation or background job.

## Health, audit, metrics, tracing

AUD contains safe IDs, intent/result hashes and fixed reasons, never canonical spec/body/session/key. Existing monotonic trusted time and bounded admission apply. No new monitoring service or performance claim; default health-only host stays compatible.

## Acceptance criteria (Given/When/Then)

- REL-F1: given eligible current selection, freeze produces independently reproducible exact bytes/hash and immutable scope/artifact/policy binding.
- REL-F2: given changed/foreign/tag/malformed/oversized/duplicate selection or policy, no Frozen result or provider call occurs; a changed preview requires review again.
- REL-F3: concurrent creator edits/freeze/next-revision/retire respect exact CAS, one Draft and immutable history; new revision does not inherit approval.
- REL-F4: duplicate/ACK-loss/pre-commit failure has one known durable owner/AUD/floor outcome or fenced uncertainty; actual HTTPS D cannot disclose stale authority.
- REL-F5: current source-bound OpenAPI/fixtures, graph, migrations, T01–T10 regressions and gates pass before complete feature PR.

## Test cases (positive, negative, duplicate, crash, tenant isolation)

R11-01–05 and R11-10–16 in the contract, including true PostgreSQL ACK loss, actual session/current owner HTTPS, pre-header expiry, SQL immutability, supported rollback and no registry/secret requirement. A fake authorizer is never registered by production composition.

## Negative & recovery scenarios

The alternative/failure flow and referenced R11 cases cover wrong tenant, missing/expired/revoked authority, duplicates, interrupted writes and unknown commit; worker/dispatch cases remain later scope. No destructive rollback or blind replay is permitted.

## Source/test/evidence mapping

At baseline bc91c2a REL had only a README. The current uncommitted T11.1a increment adds Domain, neutral Contracts, an isolated PostgreSQL store and migrations, owner-read seams, and targeted tests. Its owner operations only stage changes; they do not constitute an authorized runtime, durable shared AUD/floor commit, or HTTP API. Full T11.1a–b validation, feature PR and runtime wiring remain pending. Test evidence is recorded privately with the exact local revision and fixture scope.

### Local T11.1 progress — 2026-10-27

The uncommitted implementation now includes closed command identities, canonical intent bytes, SQL-protected revision history, an opt-in Host transaction and HTTP endpoints. That component stages REL, command receipt, AUD and IAM floor together; it rechecks owner and subject facts before commit. Exact replay is only a candidate for later fresh disclosure. Isolated synthetic PostgreSQL tests cover confirmed commit, pre-commit failure, stale version, changed selection, expiry and AUD disagreement. A transport fixture discards the actual PostgreSQL `COMMIT` response before Npgsql processes it: the outcome is unknown, and independent receipt inspection confirms the durable mutation. This is injected loss at the client stream boundary, not a real network outage. Real HTTPS fixture tests exercise session/CSRF, owner-scoped read, Draft creation/edit/replay, policy-backed freeze/retire, request limits and authority expiry after the disclosure audit but before headers. A stopped-custody recovery component, CLI and private plan reconcile exact owner/AUD evidence without mutation; tests use an isolated OS lock and the real process checker against a test-owned child before and after exit. On 2026-10-27 the full local gate passed with 218 tooling tests and 1,408 Product tests; one separately scoped live Nomad test was skipped. Full installed-command and new-process startup acceptance, PR review and human merge remain separate. Runtime route registration is opt-in and requires paired private locators and schema readiness; no real migration has been executed.

## Source & test traceability

Actual mappings above cover the local increment; planned paths also identify remaining work. Attach real commands, source revisions and complete gate results to this feature and the shared traceability log before completion.

## Open decisions, linked ADR and technical debt

The complete proposal resolves T11 choices for human review; approval of ADR-0005/contract/feature was granted on 2026-10-27; implementation remains incomplete. Execution plan/driver/worker/live qualification are explicitly later gates, not missing defaults to implement. No debt is opened or closed merely by authoring this design.

## T11.1 local draft increment — 2026-10-27

On 2026-10-29 an additional isolated integration test combined a discarded PostgreSQL commit response with the real installation custody lock and process-quiescence check. It denied recovery while the test-owned process was alive, then confirmed the exact durable release and audit evidence after that process exited. This is an injected stream fault and test-owned process, not an installed-command or real outage test.

Accepted ADR-0005/Release and approval v1: BCL-only REL Domain and tests now validate draft IDs, NFC/scalar-bound labels, immutable replacement with creator/CAS checks, and the domain-separated preview comparison vector. These are value semantics only: they do not issue authorization, resolve owners, persist, freeze or expose HTTP. Approved T11.1 still requires owner ports, canonical Frozen subject, persistence/migrations, shared audit/U/D, recovery, real HTTPS, feature gates and PR.

The current T11.1 local increment also adds neutral owner Contracts/readers in ART, PRJ and INV. ART returns the bounded immutable T10 ArtifactV1 facts without source claim, registry or secret access. PRJ resolves active same-tenant project/application/environment and an exact target association; INV separately resolves an active registered target in that exact scope. Host must compare PRJ and INV revisions and enforce current permissions: the ports do not grant access. Targeted evidence: 8 REL Domain, 1 ART isolated PostgreSQL and 3 PRJ/INV isolated PostgreSQL cases passed; 6 solution/assembly architecture tests and locked restore passed. This is not a complete REL feature: Frozen canonical subject, durable storage, authorization/U/D/HTTP/recovery, full gates and PR remain outstanding.

### Approval consumer hash compatibility

The local release-command-v1 hash implementation now recognizes the accepted request/grant/reject/revoke environment routes and closed business shapes from Release and approval v1. Approval decisions require apr-v1 preconditions, while REL operations retain rel-v1. The original nine release hash tests pass unchanged; five approval parser cases include independent request/grant vectors. No approval route is enabled by this utility change.

The local selection result now carries the already parsed exact permitting policy for the approval Host consumer, avoiding a separate mutable policy read for deadline calculation. It remains owner evidence only. The real HTTPS freeze/retire fixture verifies the approval subject consumer against Frozen/Retired state and changed environment revision.

The release recovery parser now explicitly rejects approval actions, preserving owner isolation after the shared command hash utility gained APR route support. ApprovalRecoveryPlanTests verifies both directions and retains the existing release vector.


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
