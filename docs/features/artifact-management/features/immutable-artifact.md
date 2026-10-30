---
id: FEAT-ART-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-26
module_id: MOD-ART
feature_id: FEAT-ART-001
phase: MVP
source_paths: ["src/Modules/ArtifactManagement/", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Host/Kordra.DbMigrator/ArtifactManagementMigrationCommand.cs", "src/Host/Kordra.DbMigrator/README.md", "src/Host/Kordra.Api/Composition/Artifacts/", "src/Host/Kordra.Api/Endpoints/Artifacts/", "src/Host/Kordra.Api/OfflineAdministration/Artifacts/", "src/Host/Kordra.Api/Hosting/LocalApiHost.cs", "src/Host/Kordra.Api/Hosting/LocalPilotHostOptions.cs", "src/Host/Kordra.Api/Endpoints/LocalAuthenticationEndpoints.cs", "src/Host/Kordra.Api/Kordra.Api.csproj", "src/Host/Kordra.Api/packages.lock.json", "src/Host/Kordra.Api/Program.cs", "Kordra.slnx", "global.json", ".github/workflows/product-ci.yml"]
test_paths: ["tests/ArtifactManagement.Domain.Tests/", "tests/ArtifactManagement.Tests/", "tests/IdentityAccess.Tests/LocalAuthenticationTestHost.cs", "tests/IdentityAccess.Tests/TestDatabaseConfiguration.cs", "tests/IdentityAccess.Tests/ArtifactAuthorityBindingTests.cs", "tests/IdentityAccess.Tests/ArtifactOwnerTransactionTests.cs", "tests/IdentityAccess.Tests/ArtifactReadCoordinatorTests.cs", "tests/IdentityAccess.Tests/ArtifactCallerCommitterTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/packages.lock.json", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
planned_source_paths: ["src/Modules/ArtifactManagement/Domain/ArtifactRegistration.cs", "src/Modules/ArtifactManagement/Contracts/", "src/Modules/ArtifactManagement/Infrastructure/", "src/Modules/ArtifactManagement/ArtifactRegistrationStore.cs", "src/Modules/ArtifactManagement/Kordra.Modules.ArtifactManagement.csproj", "src/Modules/ArtifactManagement/packages.lock.json", "src/Host/Kordra.DbMigrator/"]
planned_test_paths: ["tests/ArtifactManagement.Domain.Tests/ArtifactRegistrationTests.cs", "tests/ArtifactManagement.Tests/", "tests/IdentityAccess.Tests/ArtifactAuthorityBindingTests.cs", "tests/IdentityAccess.Tests/ArtifactOwnerTransactionTests.cs", "tests/IdentityAccess.Tests/ArtifactReadCoordinatorTests.cs", "tests/IdentityAccess.Tests/ArtifactCallerCommitterTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/packages.lock.json", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs"]
planned_doc_paths: ["docs/features/artifact-management/features/immutable-artifact.md", "docs/engineering/traceability.md", "docs/engineering/change-history.md"]
---

# FEAT-ART-001 — Discover and pin immutable deployable artifacts

## T10 registration design — proposed 2026-10-25

The owner selected a private OCI/Docker Distribution registry. [ADR-0018](../../../decisions/0018-project-artifact-registration.md) and [Artifact registration v1](../../../contracts/artifact-registration-v1.md) now specify the exact proposed T10.1a–f scope, replacing the underspecified 2026-10-20 registration sketch. Exact design approval is pending; registry preference alone is not adoption. Actor: current local User; trusted PRJ tenant/project; new artifact.register / artifact.read at project scope. Proposed slice custodian: NukeGeng. No source/test path is added to frontmatter until created.

Deliverable: digest-first immutable project catalog, bounded manifest/index/config content verification, independent manual declarations, durable Pending/terminal receipts, current U/C/M/D and authorized detail/list/status/registry selectors. ART owns its new store and Contracts; Host composes IAM/TEN/PRJ/SEC/AUD. SEC gains typed registry selection/global native-reference allocation, preserving inventory hashes and existing broker/helper slots. Exact project/persistence/trust/auth/recovery choices, planned paths and ART10-01–16 negative/compatibility assertions are in the contract, not a default positive authorizer.

Preserve I1's 48 tests and exact digest behavior. No tag resolution, CI/webhook authenticity, layer availability, signature verification, release/approval, message/outbox, deploy or existing registry/Keychain use. contentVerification=manifest_config_hash_verified is explicitly narrower than full image/provenance/deploy verification. release-execution v1 and ADR-0005 remain proposed; neither is adopted by this slice. Implementation/DB/HTTP/native/live proof remains absent for T10.


## User story / outcome
**Actor:** Developer / CI Provider. **Goal:** Discover and pin immutable deployable artifacts. **Primary owner:** `MOD-ART`. **Phase:** MVP.

## Pre-conditions
Project and registry reference configured; artifact metadata passes validation.

### First bounded increment — I1, local digest validation
Owner module `MOD-ART`; implementation custodian engineering-maintainers pending assignment of the product owner. Goal mapping: GOAL outcome 2 → roadmap Foundation F0-ART-I1 → UC-002 → FEAT-ART-001/I1. This is a validation increment under [accepted ADR-0008](../../../decisions/0008-domain-bootstrap-validation.md), not adoption of a production architecture. Existing module/feature status stays proposed; complete registry-backed registration is not delivered by I1.

I1 actor: local module caller (future Developer/CI application handler). Scope: pure artifact content identity, no tenant data access. Permission: none for a pure value operation; future registration requires `artifact.register` on a trusted resource scope. Source: module-local domain library only; tests: isolated test project below. No side effects, credentials, domain messages or shared kernel.

## Main flow
The following is the longer-term proposed flow beyond the bounded T10 slice above.
Receive signed provider signal → resolve OCI digest → validate registry/platform/policy → create artifact record with source revision → notify release catalog.

## Alternatives / exceptional flow
Tag points to a new digest: register distinct artifact, never mutate prior approved identity.

## Messages and integration contracts
`artifact.discovered.v1; artifact.registered.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Digest mismatch, unsigned webhook, nonexistent registry digest, unsupported architecture.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

I1 tests reject null/empty, tags, whitespace, uppercase algorithm/hex, wrong digest length, unsupported algorithms, extra separators, invalid ASCII/Unicode and embedded newlines. Content mismatch must return false, including one-byte changes and wrong length. Equivalent values compare/hash consistently; values cannot be mutated or created through public constructors. Repeated and concurrent verification leaves identity unchanged. Verification errors never include caller-provided text in exception messages.

Wrong tenant, missing permission, expired approval, duplicate delivery, worker crash and unknown agent outcome are **not applicable to this pure value operation**: there is no subject/resource lookup, approval, delivery, worker, persistence or dispatch. They remain mandatory for later registration/release increments; no I1 result proves those properties.

## Acceptance criteria
Approved artifact hash remains unchanged from release through operation; mutation produces new identity.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

I1 acceptance: Given a supported lowercase OCI digest, parsing preserves its identity exactly; unsupported/invalid input cannot construct a value. Given the original raw bytes and declared byte count, verification succeeds only if size and hash match. Changing content produces a different identity and cannot modify an existing object. `TryParse` is nonthrowing for invalid strings and clears its output. `Parse` throws a safe argument/format error without echoing untrusted input. SHA-256 and SHA-512 are supported; other algorithms fail closed with no substitution. Verification does not imply provenance or registry existence. [Local contract v1](../../../contracts/artifact-identity.md).

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## Source & test traceability
- I1 source/test paths above were merged via PR #1. The first increment is implemented and locally validated; the feature remains proposed/partial because its registration/integration flow and broader architecture acceptance are incomplete.
- Actual local validation: .NET SDK 10.0.400; locked restore succeeded; Release build produced zero warnings/errors; `dotnet test Kordra.slnx --configuration Release --no-build --no-restore --logger trx` passed **48/48**, zero skipped; `dotnet format Kordra.slnx --verify-no-changes --no-restore` passed.
- Existing AMR gates passed strict documentation validation, 39 offline LAB registry cases, **47 Python tests**, locked product restore, product build and all 48 product tests with a stable source identity. These are local checks, not infrastructure/registry/tenant-isolation evidence.
- Independent native review: `codex exec ... review --uncommitted` completed with exit 0 and a terminal `turn.completed` event, reporting no actionable defects. Full write-producing Python/.NET checks were blocked in the reviewer's read-only sandbox; the separate AMR gate run executed them. Raw receipts remain private under `.amr-local/artifact-native-review*`; no provider automatic review is inferred.
- I1 implementation and regression tests originated in [b20090b186c5c5a7b76622df5bb6b877675e32eb](https://github.com/NukeGeng/KORDRA/commit/b20090b186c5c5a7b76622df5bb6b877675e32eb) and were merged through [PR #1](https://github.com/NukeGeng/KORDRA/pull/1) at `2aa156f4e6e3afe3cc648186ac6a3242781dc71b`. Rechecked on 2026-10-18: [docs CI](https://github.com/NukeGeng/KORDRA/actions/runs/39000192554) and [product CI](https://github.com/NukeGeng/KORDRA/actions/runs/39000192528) both succeeded on that exact merge SHA. This is a dated checkpoint, not a live CI claim. No registry, tenant-policy, durable messaging or driver integration is delivered.
- Open decisions: ADR-0008 is explicitly accepted for the scoped bootstrap. `[NEEDS-DECISION]`: product module owner; ADR-0001–0007 and registration dependencies remain proposed.


## Shared Foundation composition — T01/T02 review

T01 adds only IAM persistence and its separate migrator/test projects to the root solution. T02 adds AUD Contracts/persistence/tests and the AUD migrator command. Shared product CI uses a disposable loopback PostgreSQL fixture; the Artifact implementation and health-only/inventory-denied API behavior are preserved. The existing architecture tests constrain the exact owner-approved graph and pinned packages at each commit.

T03 adds `Kordra.BuildingBlocks.Security` to the co-owned `Kordra.slnx` for the technical authentication/admission gate accepted by ADR-0012. This solution membership change adds no Artifact dependency or artifact-registration behavior. The maintainer authorized this shared-owner traceability reconciliation on 2026-10-21; Artifact's existing 48 domain tests remain part of the Product gates.

## T04 solution co-ownership — 2026-10-22

The shared solution adds IAM Contracts, TEN Contracts/runtime and TEN tests under accepted ADR-0012. Artifact Domain source, dependencies and identity contract are unchanged. Existing 48 Artifact tests and actual assembly isolation checks remain part of Product validation; no Artifact feature expansion is claimed.

## T05 shared-owner integration — 2026-10-22

T05 adds the accepted PRJ catalog Contracts/runtime and scoped integration tests to the co-owned root solution. Artifact source/contract/dependencies are unchanged; its 48 tests and assembly isolation remain required.


### T08 shared implementation consumer — 2026-10-24

Accepted ADR-0015 adds the SEC owner runtime/Contracts/test graph and scoped Host/offline migration composition. The existing source/test mappings above cover the shared solution, authority/HTTP/audit/architecture consumers changed in the same increment. [FEAT-SEC-001](../../connection-secrets/features/secret-binding.md#t08-local-implementation--2026-10-24) owns exact new paths and local evidence. IAM neutral dependencies/current C intent do not add a circular SEC runtime edge; AUD keeps v1 stages/schema, metadata wire v1 and existing role permissions remain unchanged. Synthetic fixture/native proof does not authorize provider network access or change this module's business ownership.

### Exact T10.1a–f acceptance — 2026-10-25

NukeGeng approved exact ADR-0018 and Artifact registration v1, including this owner’s bounded design dependencies and NukeGeng’s ART/registry slice custody. This supersedes pending-approval language in the T10 proposal above only; wider feature/module status, existing accepted contracts and historical evidence remain unchanged. Implementation, ART10 tests, native/live scope, real provisioning and PR/merge evidence are still separate. No repeat design approval or extra Sol review is needed for covered T10.1a–f work.

## T10.1a identity implementation scope — 2026-10-25

Approved ADR-0018 / Artifact registration v1; custodian NukeGeng. First repository-only sub-increment implements immutable repository/platform/source declaration/project identity and canonical request/idempotency hashes in existing Domain/tests. Actor: local owner caller with already-resolved IDs; these values perform no authorization, provider use or database operation. The future registration handler still requires current artifact.register at exact project scope. New paths are planned above before creation. Tests cover exact accepted grammar/bounds, immutable identity, scoped keys, null/correlation semantics and independent canonical vectors; no permission/store/recovery claim from pure values. T10.1a persistence/migrations and T10.1b–f remain pending.

## T10.1a local identity/receipt code — 2026-10-25

Actual native gpt-6.1-sol/medium, explicitly allowed by the active owner GOAL, implemented the first repository-only sub-increment in ArtifactRegistration.cs and ArtifactRegistrationTests.cs (existing Domain/test projects, no package/graph change). Immutable exact repository/platform/source declaration/project identity, independent domain-separated canonical business/identity/scoped-key hashes and staged Pending→Registered/Failed/Abandoned receipt values now exist locally. Terminal values cannot reopen; receipt registration requires the same full identity and bounded UTC attempt times. No receipt proves a DB commit, authorization or offline recovery quiescence.

Targeted evidence: all 111 Domain tests passed (original 48 retained, 63 new); five existing assembly/XML architecture checks passed against the rebuilt Domain; project format verification passed. Golden hashes were calculated independently with Python compact JSON/SHA-256. This is local uncommitted partial T10.1a code, not a completed registry feature or ART10 matrix. EF owner store/migration, shared transactional U/C/M/D, authority/SEC/verifier/HTTP/recovery and T10.1b–f remain pending. Full Product gates run at the T10.1a/feature validation boundary rather than repeating unchanged integration suites for each pure value edit.

The existing CLI child plan KORDRA-T10A-IDENTITY-20261025 is retained pending: current-session step/finish still hard-codes Luna Max. Real authorized Medium evidence is private .amr-local/t10-implementation; no Luna/native-terminal receipt or CLI completion is fabricated and no AMR routing was changed. Historical Foundation checkpoints and unrelated drafts remain unchanged.

## T10.1a persistence implementation scope — 2026-10-25

Accepted ADR-0018 and Artifact registration v1 authorize ART Contracts/runtime/tests, EF/Npgsql owner store, additive migration/history and offline migration command. Actor is the trusted current-admission Host caller; tenant/project/principal IDs are already resolved and remain exact. Store methods stage changes only in the caller’s transaction; they cannot grant permission or commit audit independently. Planned paths above include only ART and the explicit shared migrator/graph consumers. Validation will use synthetic isolated kordra_test identities; no real installation migration, account, provider or secret lookup. Native profile remains explicitly authorized Sol 6.1 Medium.

## T10.1a local persistence implementation — 2026-10-25

ART now has the accepted Contracts/runtime/test projects, owner EF model/history/migration and ArtifactRegistrationStore. Mutations require an already enlisted, role-validated caller transaction; the store never commits or contacts a registry. SQL enforces immutable artifact/claim rows, one-way receipt transitions, scoped unique keys/content identities and tenant/project FKs. A deferred four-column FK additionally ties each claim to its exact registration/artifact after all M writes are staged. Host U/C/M/D, actual AUD/floor atomic integration and recovery custody still belong to T10.1b/e; a prepared store result proves no durable outcome or permission.

The offline artifact-management command reads the exact documented KORDRA_ARTIFACT_MIGRATION_CONNECTION_STRING under existing stopped-installation custody and rejects non-loopback/non-kordra_admin targets. It has not run on kordra. The separate runtime locator is not wired into the API yet. ART runtime explicitly exposes EF Relational 10.0.11 so consumer assemblies do not fall back to Npgsql's lower dependency minimum; existing approved EF/Npgsql versions are unchanged.

Targeted local proof: 31 ART persistence/boundary cases passed, no skips; updated actual project/assembly graph checks passed 6/6; root Release build has zero warnings/errors. Tests apply ART's migration only in kordra_test and roll back their own synthetic row transactions, retaining no immutable-record bypass or broad cleanup. Original 111 Domain cases remain separately source-bound evidence. Formatting and final full T10.1a gates are recorded privately after final source/doc updates; these targeted results alone do not qualify T10.1a–f or the ART10 matrix. No source commit, push, PR, additional AI review, real migration/registry/Keychain operation or debt change is claimed.

## T10.1d implementation scope — 2026-10-26

FEAT-ART-001 / MOD-ART, NukeGeng custody, accepted ADR-0018. Implement strict immutable registry trust and fixed-origin bounded manifest/index/config transport/verifier in ART Infrastructure/Registry with synthetic ART protocol/TLS tests. Actor is the eventual current project-authorized registration caller; configuration is not permission and this slice exposes no endpoint. Preserve own-module dependencies, exact identity/platform/digest and all accepted count/time/body/redirect/TLS/auth limits. No existing registry, Keychain, real provisioning or company access. T10.1e–f current-authority HTTP integration and full conformance remain separate.

### T10.1d verifier core — local evidence 2026-10-26

ART now owns immutable RegistryTrust/RegistryTrustConfiguration, invocation-owned RegistryReadOnlyTransport and ArtifactRegistryVerifier. Configuration validates exact closed JSON/file hash/private-file custody, canonical HTTPS/IP/CA/pin/repository/auth and duplicate connection identities. Production parsing has no loopback exception. Internal fixture construction is test-only. Transport uses fixed digest paths, direct anonymous/Basic/static-Bearer modes, TLS private trust plus SAN/EKU/pin, no redirect/challenge/proxy/retry fallback, bounded streamed bodies/headers and one-second requests. It sends no layer/tag/catalog request and never stores credential fields in shared client defaults.

Verifier checks raw hashes/descriptor sizes, supported manifest/index media, exact one platform match, config interpretation and at most 128 layer descriptors. It returns small private content facts and clears owned response arrays; it does not claim layer availability, signature/provenance or deployment verification. Both descriptor-asserted and config-asserted arm64/v8 evidence are explicit. Framework Authorization strings cannot be deterministically erased; the accepted post-C exception remains caller-controlled pending T10.1e U/C/M/D composition.

Current ART module tests: 135 passed, zero skipped, including the original 31 persistence tests, 52 trust/private-file cases, 30 transport/TLS/error/bounds cases and 23 projector cases. A public-verifier synthetic TLS case makes two exact GETs with request-local auth and rejects foreign scope before connecting. A fresh SEC migration conflict case also passed with the other two migration cases: corrupt history is simulated only in a fresh rollback-owned schema and is rejected without guessing ownership. Source-bound full gates from T10.1c remain historical; current full-feature validation, artifact HTTP/recovery, fixture/OpenAPI/ART10 closure and publication remain T10.1e–f. No real registry/Keychain or company access, extra AI review or debt change.

## T10.1e implementation scope — 2026-10-26

FEAT-ART-001 / MOD-ART with Host IAM/SEC/PRJ/TEN/AUD composition, accepted ADR-0018 and exact T10.1a–f. Actor current local tenant user with artifact.register or artifact.read on the exact active project, with same-principal receipt isolation. Add owner-scoped bounded queries, U/C/M/D binding/expiry, atomic Pending/result/AUD/floor transactions, caller-bound HTTP envelopes and stopped metadata-only recovery. Shared invocation/helper slots and existing source/architecture boundaries remain; historical replay has no registry/SEC dependency. Scoped test fixtures are synthetic kordra_test/TLS/fake helpers only. No real registry/Keychain/company or startup DDL; no automatic receipt takeover, task replay, review/model change or new design approval.

### T10.1e query/transaction components — local evidence 2026-10-26

Added ART-owned bounded queries and Host ArtifactAuthorityBinding/Resolver, ArtifactOwnerTransaction, ArtifactReadCoordinator and ArtifactCallerBoundResponseCommitter. Queries use current project U/D, own-principal receipt isolation, unchanged-candidate comparison and bounded payload hashes. Historical detail/list/status/replay has no registry/SEC requirement; selectors require explicit configuration but return only the accepted public selection fields. No artifact endpoint or runtime activation has been wired yet.

The Host transaction stages ART plus AUD and IAM floor in one connection/transaction, exposes no prepared result on uncertain commit and observes AUD's actual CommitAttempted phase. Setup/staging/save denial rolls back without closing shared admission; real lost-ACK commit closes it. The caller committer pins exact session/project/body/status/deadline, is single-use and rechecks immediately before headers/body. Shared sixteen-slot ownership is retained for unresolved query work/outcome.

Targeted local evidence: 5 owner query tests (including 53 tied-timestamp rows on real PostgreSQL), 6 real transaction/fault cases, 12 binding/expiry/canonicalization cases, 11 query U/D interleavings and 10 caller/pre-header component cases passed. Controlled authority-port tests are not an HTTPS/login or complete ART10 proof. Committed transaction fixtures retain their UUID-scoped immutable synthetic ART/AUD/floor history; tests do not disable/delete immutable guards. Other owner-query fixture rows roll back. Full current-tree gates are recorded separately. Remaining T10.1e includes new-registration U/C/M/D, minimum schema/locator activation guards, actual HTTP wiring/current-owner tests and stopped recovery; T10.1f/publication remain pending. No real registry/Keychain/company operation, new permission/ADR, independent AI review or debt change.

T10.1e capacity regression: a new test reproduced shared-pool exhaustion after sixteen completed failed HTTP handoffs. Wire delivery uncertainty alone does not imply an unresolved running task/commit. Query invocation state now retains capacity only for an unresolved task or actual unknown owner commit; completed/aborted handoffs release their slot after cleanup. Seventeen sequential failed handoffs now leave the shared pool usable, while the unknown-D-commit test still fences and aborts without a body. This is an internal unactivated-component fix, not a production incident or review approval.

### T10.1e new registration implementation scope — 2026-10-26

Continue the accepted U→helper→C→provider→M→D path, exact idempotent Pending ownership, trusted bounded failure bookkeeping and current-authority disclosure. Component tests use the existing ART test assembly's synthetic trust/facts fixture access and add only an explicit Host test reference; no additional production friend assembly or provider bypass is introduced. Enforce the accepted non-destructive ART rollback rule by refusing Down which erases catalog/receipt/declaration history; Up and applied schema stay unchanged. No runtime activation/real provider/secret/DDL authorization or new ADR decision is inferred.

### T10.1e registration core — local component evidence 2026-10-26

ArtifactRegistrationCoordinator now owns one exact keyed Pending attempt, durable U before any helper/provider, current C after confirmed helper exit, bounded fixed-origin verification outside admission/transactions, atomic M and current caller-bound D. It shares the existing sixteen-slot capacity and SEC helper reservation. Same-key historical replay releases its initial slot and obtains fresh query U/D without registry/SEC/provider reuse. Minimum ART/SEC v1 structural capability checks run through owner methods, using schema metadata without history-table grants, migrations or permission repair.

Known provider/content failures attempt exact Pending→Failed plus AUD Outcome/floor with a bounded independent cleanup token; failure to finish leaves Pending. A cancelled invocation allows bounded cleanup before fencing truly unresolved work. Actual unknown commits/unknown helper quiescence fence and return no body; confirmed M is preserved when D is denied. New authorized content rejection uses audited 422; transport/provider unavailability uses audited 503, with no raw exception or receipt in new-attempt error envelopes. Framework secret strings retain the already-accepted erasure limitation.

22 new synthetic component cases passed with real PostgreSQL commits and actual TLS: durable U/C/M/D, same-key replay/conflict, concurrent Pending without takeover, distinct-key shared artifact/independent declarations and timestamps, changed authority and lost ACK at each stage, original/shortened expiry, cancellation/failure bookkeeping, no provider I/O under the common fence, minimum schema and non-destructive ART Down. Authenticated entries without binding/helper never fall back to anonymous or create Pending. The successful provider integration here is anonymous; authenticated broker/native consumption, real login/HTTP wiring, complete ART10 matrix and offline recovery are still pending. Existing generic typed broker/header tests do not substitute for that remaining end-to-end proof. No real registry/Keychain/company operation, extra AI review or debt change.

### T10.1e actual HTTP, native fixture and recovery — local increment 2026-10-27

Artifact routes and optional runtime composition use the existing owner services. Strict parsing rejects duplicate/unknown fields, invalid IDs and keys, query overrides and bodies over 16 KiB. An approved isolated synthetic store verifies the actual macOS helper after C. The stopped recovery command inspects receipt/audit hashes, holds installation custody, verifies quiescence and applies an exact plan; a lost acknowledgement is fenced and reconciled against persisted facts. Two existing IAM HTTPS fixture source files are linked only into the artifact test assembly. See the [runbook](../../../operations/artifact-registration-v1.md) and [contract](../../../contracts/artifact-registration-v1.md). The full matrix, final gates and PR evidence remain pending.

Targeted local evidence for this increment: 217 artifact tests passed before the additional real-owner/concurrency cases; five real HTTPS owner-state denials and one concurrent distinct-key registration test then passed. Recovery tests independently exercised matching Registered M evidence, missing M audit, pre-commit failure and actual lost commit acknowledgement. These are local results for the current worktree; final full-gate and PR evidence are recorded separately.

The local full gate on 2026-10-27 passed 218 tooling tests and 1,158 Product tests; one separately scoped live Nomad test was skipped. The [ART10 evidence map](../../../contracts/artifact-registration-v1.md#t101f-local-conformance-evidence-map--2026-10-27) separates local coverage from hosted review and merge.

## T11 proposed consumer seam — 2026-10-27

ART consumer acknowledgment for [Release and approval v1](../../../contracts/release-approval-v1.md) / [ADR-0005](../../../decisions/0005-immutable-release.md), awaiting exact human acceptance. Add a neutral owner Contract/reader for exact trusted tenant/project/artifactId, returning only the closed immutable T10 ArtifactV1 facts. Do not borrow another registration provenance receipt, access SEC/registry or reverify historical records. Qualification remains manifest/config hash evidence with unattested provenance; layer/signature availability remains unverified.

This is planned additive scope, not implemented source/test evidence or a change to the accepted T01–T10 baseline. Preserve current source/test mappings; Developer adds precise new paths and negative/compatibility tests only when the owning slice exists. R11-02/03/10/12/15 are required as applicable. No real database/provider/secret operation is authorized.

## T11.1 shared graph acknowledgment — 2026-10-27

Accepted ADR-0005 adds a BCL-only REL Domain and test project to the solution and actual architecture graph. This graph change does not alter this owner’s runtime behavior or grant release authority. T11.1 owner ports, persistence and HTTP remain in progress.

## T11.1 neutral release artifact port — local increment

A BCL-only ART-owned IReleaseArtifactReader delegates to the existing exact tenant/project immutable artifact projection. It returns no declaration/provenance claim and performs no registry or credential access; the caller must hold admission and independently authorize release selection. Actual source and synthetic PostgreSQL tests remain owned by this feature; T11.1 remains incomplete.
