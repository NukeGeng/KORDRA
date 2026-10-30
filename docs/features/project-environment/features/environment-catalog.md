---
id: FEAT-PRJ-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-25
module_id: MOD-PRJ
feature_id: FEAT-PRJ-001
phase: MVP
source_paths: ["src/Modules/ProjectEnvironment/ReleaseCatalogReader.cs", "src/Modules/ProjectEnvironment/ReleasePolicyEnvironmentReader.cs", "Kordra.slnx", "src/Host/Kordra.Api/Composition/Catalog/CatalogClockFloorWriter.cs", "src/Host/Kordra.Api/Composition/Catalog/CatalogMutationCoordinator.cs", "src/Host/Kordra.Api/Endpoints/EnvironmentV2Endpoints.cs", "src/Host/Kordra.Api/Kordra.Api.csproj", "src/Host/Kordra.Api/OfflineAdministration/ResourcePolicy/OfflineResourcePolicyCoordinator.cs", "src/Host/Kordra.Api/OfflineAdministration/ResourcePolicy/ResourcePolicyManifest.cs", "src/Host/Kordra.Api/packages.lock.json", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/ProjectEnvironmentMigrationCommand.cs", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Modules/ProjectEnvironment/Contracts/CatalogContracts.cs", "src/Modules/ProjectEnvironment/Contracts/EnvironmentCreationV2.cs", "src/Modules/ProjectEnvironment/Contracts/Kordra.Modules.ProjectEnvironment.Contracts.csproj", "src/Modules/ProjectEnvironment/Contracts/PolicyBindings.cs", "src/Modules/ProjectEnvironment/Contracts/packages.lock.json", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/20261022065736_InitialProjectCatalog.Designer.cs", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/20261022065736_InitialProjectCatalog.cs", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/20261022085222_AddResourcePolicyBindings.Designer.cs", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/20261022085222_AddResourcePolicyBindings.cs", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/20261023020110_StrengthenPolicyBindingHistoryScope.Designer.cs", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/20261023020110_StrengthenPolicyBindingHistoryScope.cs", "src/Modules/ProjectEnvironment/Infrastructure/Migrations/ProjectDbContextModelSnapshot.cs", "src/Modules/ProjectEnvironment/Infrastructure/PolicyBindingRecords.cs", "src/Modules/ProjectEnvironment/Infrastructure/ProjectDbContext.cs", "src/Modules/ProjectEnvironment/Infrastructure/ProjectDbContextFactory.cs", "src/Modules/ProjectEnvironment/Kordra.Modules.ProjectEnvironment.csproj", "src/Modules/ProjectEnvironment/PolicyBindingStore.cs", "src/Modules/ProjectEnvironment/ProjectCatalogStore.cs", "src/Modules/ProjectEnvironment/packages.lock.json", "src/Modules/ProjectEnvironment/ProjectIdentityReader.cs"]
test_paths: ["tests/ProjectEnvironment.Tests/ReleaseCatalogReaderTests.cs", "tests/ProjectEnvironment.Tests/ReleasePolicyEnvironmentTests.cs", "tests/IdentityAccess.Tests/CurrentAuthorityHttpTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/ProjectEnvironment.Tests/CatalogTestFixture.cs", "tests/ProjectEnvironment.Tests/ProjectCatalogTests.cs", "tests/ProjectEnvironment.Tests/ProjectEnvironment.Tests.csproj", "tests/ProjectEnvironment.Tests/ResourcePolicyIntegrationTests.cs", "tests/ProjectEnvironment.Tests/TargetAssociationTests.cs", "tests/ProjectEnvironment.Tests/packages.lock.json", "tests/ProjectEnvironment.Tests/ArtifactProjectIdentityTests.cs"]
---

# FEAT-PRJ-001 — Create and govern deployment environments

## Foundation implementation slice — approved for implementation 2026-10-20

Luna T05 PRJ task follows TEN/audit under accepted ADR-0012 and authority/admission v1. Actor authenticated User; project.manage for catalog mutation, project.read for adopted scoped reads. Persist exact Project/Application/Environment ownership, Active/Suspended/Deleted lifecycle and revisions, explicit Development/Test/Staging/Production classification and policy association. ConnectionProfile stays SEC-owned. This approved slice is not implementation evidence.

The contract owns command DTOs and transactional two-owner target binding. Project-only target requires resolved environment absence; unknown lookup never supplies it. First slice rejects tenant/project moves rather than relabeling IDs. Creation grants no implicit target/deployment rights. INV association updates enlist PRJ/INV owner handlers and AUD in one transaction under the writer gate, never cross-owner table access.

Acceptance PRJ-F1: create catalog objects once per idempotency key with audit and stable owner references. PRJ-F2: foreign tenant/project/target, stale revision, invalid policy/classification and name-based downgrade reject. PRJ-F3: crash between association updates rolls back both; no reader sees a half-binding. PRJ-F4: suspended/deleted/unknown/conflicting owner facts deny U/D, including change-and-change-back. Planned roots: existing PRJ reservation and tests/ProjectEnvironment.Tests/; no speculative frontmatter mappings.


## User story / outcome
**Actor:** Tenant Admin / Project Owner. **Goal:** Create and govern deployment environments. **Primary owner:** `MOD-PRJ`. **Phase:** MVP.

## Pre-conditions
Tenant active và actor có quyền quản lý project.

## Main flow
Create project/application → create classified environment → bind approved target → assign policy → activate.

## Alternatives / exceptional flow
Rename environment không đổi security classification; policy change requires audit/review.

## Messages and integration contracts
`environment.classification.changed.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), MOD-PRJ remains the source of project-to-tenant and environment-to-project/tenant ownership, classification, target binding and applicable policy association. Future owner evidence must bind the exact lookup and current owner revision. A project-only target requires an authoritative, resolved absence of an environment; failed or unavailable environment lookup is not absence. Names, labels and read-model projections cannot establish ownership. Missing, stale, conflicting or unavailable ownership/policy evidence denies access.

At the original I2.7 proposal, formal ownership and concrete policy assembly were unresolved. Accepted ADR-0012 assigns NukeGeng accountability for the approved Foundation slice and MOD-PRJ ownership of its project/environment facts; the broader module remains proposed. The prior I2.7 approval alone adopted no source mapping or runtime behavior.

## Negative & recovery scenarios
Attach target của org B; downgrade production classification without approval; duplicate resource id.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Environment always has explicit classification and policy; no name-based production checks.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## I0 — OP-P0 connection-profile/environment seam proposal

**Proposed only; source not implemented, no operational approval.** This bounded increment prepares the profile/environment part of [OP-P0](../../../product/operator-pilot-plan.md) and [OP-01](../../../product/operator-workstation.md), following the updated [FEAT-INV-001/I0 inventory proposal](../../infrastructure-inventory/features/register-target.md). The original MVP phase and broader environment-governance flow above remain; I0 proposes read-only selection and display, not target registration, classification/policy changes, credential binding or deployment. It does not broaden [OSS Beta acceptance](../../../product/oss-beta-acceptance.md).

All metadata names, labels and associations below are illustrative proposals pending owner review, not adopted storage schemas, public contracts, permission rules or runtime state transitions. This task is `F0-OPP0-PRJ-SPEC`; contributor scope is local documentation under `repository.write`, not a product or infrastructure grant.

### Proposed profile metadata and associations

The existing [MOD-SEC module](../../connection-secrets/module.md) owns the `ConnectionProfile` aggregate and authoritative connection metadata. MOD-PRJ contributes its project/application/environment classification and authorized target associations to an assembled read view; it does not create or persist a second profile aggregate. Unresolved storage technology or local/server placement does not reopen that domain ownership.

- An illustrative assembled profile/environment view could expose an authorized `ConnectionProfile` reference/revision and descriptive labels supplied by MOD-SEC, together with distinct trusted organization, project, environment and selected target references supplied by their owners. Optional source-system, Git/CI, registry and provider-context aliases remain descriptive metadata only; they cannot select broader access or establish target identity. Real addresses, company identifiers and private profiles stay outside public repository evidence.
- Project/application/environment ownership remains in MOD-PRJ. Resolve organization/project/environment associations from trusted ownership information, with an explicit environment classification and associated policy reference. Classification is independent of profile/environment names: renaming a production profile to “dev” leaves its production classification, policy and visible production indicator intact. I0 cannot downgrade classification or change policy.
- A selected target must have an authorized association with that environment and the same trusted tenant scope. The profile references the inventory-owned target and its trusted identity expectation/resource scope; it does not create, transfer or validate target ownership itself. A matching alias or endpoint label cannot replace independently checked provider identity. Unknown identity remains unknown, not a verified target.
- Carry only an opaque credential-binding/reference identifier for the selected scope, subject to disclosure authorization. [Connections & Secrets / FEAT-SEC-001](../../connection-secrets/features/secret-binding.md) retains secret resolution, provider selection, expiry, rotation and binding validity. No token, password, kubeconfig or secret value belongs in profile metadata, logs, events or UI. A reference is neither a credential nor permission; local credential availability grants no server-worker access.
- Profile revision and selection context identify which association a read/display concerns. Profile metadata freshness is separate from inventory observation freshness; saving, renaming or reconnecting cannot refresh target evidence. Revision, expiry and freshness mechanisms remain owner decisions before implementation.

### Proposed active-target visibility and selection/reconnect behavior

1. Show the selected profile plus organization/project/environment, explicit classification (including a production indicator), target reference/identity summary and bounded resource scope throughout target-specific views. Display metadata as reported/unverified until trusted association and identity checks succeed; disclose identifiers only within current permission. Keep client connection availability separate from observed workload health and show evidence time/coverage supplied by its owner.
2. Before a bounded read, resolve the authenticated principal, trusted tenant ownership, environment-target association and current scoped permission through the owning modules. Profile labels, role labels, aliases and caller `tenantId` never authorize. Deny by default; platform scope grants no implicit tenant access. Missing/expired/revoked binding or unknown/mismatched target identity prevents dispatch; do not fall back to another profile, target, namespace, credential or TLS bypass.
3. On profile/target selection change, invalidate prior in-flight results for the active view and revalidate the new association. Correlate reads/results with tenant, environment, target and selection revision as proposed in FEAT-INV-001/I0; late results from the previous selection cannot populate the new view. Cached evidence is accessible only under current authorization and remains tied to its original target, capture time and coverage.
4. A disconnected Mac/client means current observation is unavailable; it does not prove an unhealthy workload. A fresh authorized, identity-bound observation may independently show an unhealthy workload even when its read path is reachable. Keep these dimensions and their reasons distinct; missing, stale or untrusted evidence stays unknown. MOD-PRJ displays owner-supplied evidence, not a locally inferred cluster-health decision.
5. After reconnect, Mac wake or client restart, a remembered profile is only a selection hint. Revalidate identity, associations, permissions and credential binding, then obtain a new bounded read before displaying current observed status. Do not silently retarget, upgrade old timestamps, replay mutations or claim background execution survives Mac closure. Durable execution/host decisions remain outside I0.

### Proposed module seams — existing ownership retained

| Owner | Profile/environment seam for later contract review |
|---|---|
| MOD-PRJ / this feature | Project/application/environment associations and explicit classification contributed to an assembled selection view. No ConnectionProfile aggregate ownership/persistence and no direct reads of another module's internal storage. |
| [MOD-INV / FEAT-INV-001](../../infrastructure-inventory/features/register-target.md) | Owns target registration, target identity/capability and inventory evidence. Consume reviewed references/results; profile selection cannot fabricate observed cluster state or registration approval. |
| [MOD-SEC / FEAT-SEC-001](../../connection-secrets/features/secret-binding.md) | Retains ConnectionProfile, authoritative connection metadata, SecretReference, CredentialBinding and RotationStatus ownership. Consume reviewed authorized profile/reference/validity results only; provider, persistence technology, local/server placement and local-versus-worker trust are not selected here. |
| [MOD-IAM / FEAT-IAM-001](../../identity-access/features/authorize-resource.md) | Owns authorization decisions for trusted principal and resource scope. Existing `project.read`, `project.manage` or `target.read` catalog candidates are not adopted as a complete profile action mapping. |
| [MOD-OBS / FEAT-OBS-001](../../observability/features/health-verification.md) | Owns health verification semantics with scoped evidence/freshness; transport loss cannot become a workload-health finding in the profile view. |

No new dependency, host, API/event contract or ownership transfer is adopted. Existing message candidates above remain candidates; provider secrets, authorization decisions, observed cluster state and inventory stay with their existing owners.

### Planned acceptance / negative matrix — not executed

Future unit/contract/integration cases use synthetic profiles, fake identities/authorization, fake clocks and an instrumented read adapter. These are planned assertions, not test files, executed product tests or passing results; no live target or credential is needed.

| Case / synthetic setup | Planned assertions |
|---|---|
| Valid read-only selection | Trusted same-tenant organization/project/environment/target association, explicit classification, current permission and valid binding expose the permitted active-target summary; a fresh identity-matched read is tied to selection revision and original capture time. No mutation call or secret value is emitted. |
| Wrong tenant or target association | Forged caller `tenantId`, another tenant's target/credential ref, platform-only principal, or same-tenant target bound to a different environment is denied before provider dispatch. No protected cached evidence or identifier leaks; matching labels/aliases cannot override association checks. |
| Production profile renamed | Rename label/alias to “dev”: stable environment classification, production indicator, policy reference and access requirements remain unchanged; no policy downgrade or extra authority. |
| Missing permission or missing/expired binding | Deny the read and protected cache access when scoped permission is absent/revoked; missing/expired/revoked credential binding prevents dispatch and yields a sanitized unavailable/denied reason. A saved selection, role label or local credential cannot bypass either check. Revalidate after expiry during reconnect. |
| Disconnected client versus unhealthy workload | Client transport loss with no fresh workload evidence shows disconnected/unknown observation, not unhealthy workload; an independently fresh unhealthy observation remains a separate evidence-backed signal even with a reachable read path. Preserve authorized last-known evidence timestamps. |
| Stale profile or association changed | An obsolete profile revision or removed/rebound environment-target association cannot dispatch using cached validity. Require refreshed trusted association; do not silently choose a replacement target or refresh observation time. |
| Unknown or mismatched target evidence | Missing provider identity, stale/missing timestamps, partial evidence or an identity mismatch cannot produce a verified target/current healthy claim. Keep sanitized reason and evidence provenance; no alias matching or broader-context fallback. |
| Selection switch / delayed duplicate result | Switch context during a delayed read, then receive duplicate/out-of-order old results: old evidence cannot overwrite the active target view or merge tenant scopes. Repeated selection does not authorize mutation or create target/credential bindings. |
| Restart / reconnect / revoked access | Remembered selection after restart requires current scope, permission, binding and identity checks plus a fresh read; revoked access hides protected cached data. No mutation replay, automatic remote job claim or implied Mac-close survival. |
| Module ownership isolation | Build the view from reviewed owner references/results; no MOD-PRJ profile aggregate or direct connection-secret storage read is introduced. ConnectionProfile updates remain in MOD-SEC and cannot be implied by changing an environment view. |

### Unresolved decisions and evidence boundary

- `[NEEDS-DECISION]` Assign owners; select IdP/trusted tenant resolution and approve exact profile/read permissions, scope checks, revocation/recheck semantics and audit disclosure/retention with IAM/security owners. No role-name rule or new policy engine is chosen.
- `[NEEDS-DECISION]` Persistence technology and profile revision/freshness rules; local-only versus server/team-managed storage and synchronization/conflict handling within the existing MOD-SEC profile ownership, plus reviewed read-view/association contracts with MOD-PRJ. No database, Keychain/provider choice, domain ownership transfer or local-to-worker credential transport is adopted.
- `[NEEDS-DECISION]` First supported target and trustworthy provider-identity mechanism, bounded read capability set and observation freshness/ordering contracts with inventory/observability owners. Do not infer Kubernetes or Compose/SSH selection from examples.
- `[NEEDS-DECISION]` UI and host architecture/ADR: browser client versus native packaging, API/worker topology and durable state location. [ADR-0009](../../../decisions/0009-source-architecture.md) remains proposed; [ADR-0008](../../../decisions/0008-domain-bootstrap-validation.md) remains accepted only for the narrow artifact-domain/test bootstrap. Neither authorizes a new profile implementation, host or dependency here.
- Product source/tests for this seam are **not implemented**. Planned cases above have not run. Documentation checks, task-bound execution receipt, independent native review and contributor triage must be recorded separately by the coordinator for the exact current content; none constitutes product verification or company-resource approval. Roadmap/evidence reconciliation and the subsequent credential-seam task remain coordinator work after applicable gates/review/triage; no historical `validation_failed` task is replayed.

## Source & test traceability
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.

## T05 active implementation plan — 2026-10-22

Owner PRJ, actor current trusted User, exact tenant scope. Project mutations require project.manage at catalog scope; target mutations require target.manage at exact project/environment. Required trusted authority and environment-policy ports have no positive runtime default. Planned source: src/Modules/ProjectEnvironment/Contracts/CatalogContracts.cs, src/Modules/ProjectEnvironment/Infrastructure/ProjectDbContext.cs, src/Modules/ProjectEnvironment/ProjectCatalogStore.cs, src/Host/Kordra.Api/Composition/Catalog/CatalogMutationCoordinator.cs. Planned tests: tests/ProjectEnvironment.Tests/ProjectCatalogTests.cs. Implement owner-only persistence with approved EF/Npgsql pins, revision/idempotency/lifecycle checks, same-transaction PRJ/INV binding plus AUD, and negative/rollback/unknown-commit tests. No new HTTP route, provider read, real provisioning or runtime migration. Preserve accepted architecture and T04 evidence.

## T05 PRJ owner implementation — local 2026-10-22

FEAT-PRJ-001 / MOD-PRJ now owns Project/Application/Environment, lifecycle/revisions, explicit classification/policy association and its side of target binding. The context, additive migration/history and BCL-only Contracts are mapped above. Composite tenant/project/environment foreign keys prevent foreign links. Names never supply classification; rename leaves classification/policy unchanged. First-slice commands reject classification/policy replacement, tenant/project moves, client-generated create IDs and stale revisions. Deleted identities are retained and cannot be revived; changing state back still increases the revision.

ProjectCatalogStore prepares/persists only its own rows in a transaction supplied by Host; it never commits independently. CatalogMutationCoordinator acquires the existing shared writer gate, resolves current trusted subject/permission, hashes command+subject for owner receipts, stages AUD, revalidates authority/policy and commits both owners and AUD once. Enlistment/setup failure cannot poison admission; unknown commit closes it. There is no default positive ICatalogAuthority or IEnvironmentPolicyReader implementation/registration. Tests use actual IAM/TEN grants and an explicitly synthetic policy allowlist; no platform fallback or implicit project-to-target rights.

ProjectCatalogTests covers idempotent project/application creation, environment classification/policy, name-based downgrade rejection, current subject/tenant/project owner, stale revision and suspension/deletion, policy/subject loss at AUD staging and database FK denial. TargetAssociationTests co-tests cross-owner atomicity and association correctness. Suspended project management recovery cannot bypass invalid current project authority; production restoration/owner-fact adapters and U/D disclosure remain T06 wiring, not a permissive test authorizer. Owner preparation supports lifecycle transitions only when its caller supplies valid applicable authority.

No PRJ HTTP endpoint or list-disclosure boundary is registered. Whole-workspace offline manifest/resource/grant composition and current policy persistence/adapters are not a delivered CLI here; they must be wired/proven at T06 under accepted contracts before real provisioning. Policy replacement/downgrade needs its own accepted command/approval semantics. Full module and Operator Pilot readiness are not claimed.

## PR13 migrator locator correction — 2026-10-23

FEAT-PRJ-001 / MOD-PRJ; finding 4345619894. The offline migration command reads the documented `KORDRA_PROJECTENVIRONMENT_MIGRATION_CONNECTION_STRING`. A regression supplies a synthetic test identity via that exact variable and verifies the existing offline-target guard rejects it before opening a connection, rather than reporting a missing locator. It fails before the spelling correction and passes after. Target/role validation, migration approval, runtime architecture and schemas are unchanged; no real migration is executed.

## Historical T06 resource-policy design input — resolved by accepted ADR-0014 on 2026-10-22

The accepted authority/admission v1 requires complete applicable policy/deny/condition coverage and exact current resource-policy revision. T05 stores environment classification/policyRevision and requires IEnvironmentPolicyReader, but deliberately supplies no positive runtime policy implementation. The approved request shape names only policyRevision; no normative resource-policy document/schema, revision lookup identity or complete applicability/deny source is defined in the current accepted package. IAM RoleDefinition permission/scope/effect interpretation is implemented and cannot silently be treated as the environment resource policy. Copying the synthetic policy allowlist or treating a positive revision integer as allow would violate fail-closed policy completeness.

Required bounded clarification: identify the already approved resource-policy source, payload and exact revision/applicability semantics, or design/approve that narrow addition before production policy wiring. Do not reopen accepted ADR-0012 technology/admission decisions or launch a full GOAL/PR Sol review. Session/floor/owner-fact/U-D engine and transport proof can proceed independently, with unknown resource policy denied and positive runtime ingress closed. This records a missing normative input, not implementation evidence or implicit ADR acceptance.

### Four additional resource-policy design clarifications — proposed 2026-10-22

The maintainer requested that these four points be added to the bounded T06 design inputs. Recording them does not approve the resource-policy schema, change accepted contracts or authorize implementation. The Architect must resolve them in the exact design submitted for human approval, preserving IAM policy ownership, PRJ binding ownership and the existing authority/admission protocol.

1. **Authoritative current revision.** Specify the IAM-owned authoritative head for each policy identity and how its current revision is read under admission. Do not infer current authority from `MAX(revision)`, client-supplied references or merely the existence of an immutable revision. The proposed rule is that a binding to a non-current revision denies; define status and effective-time checks explicitly, including future-dated revisions. Revisions cannot be reused after change-and-change-back. Acceptance must cover stale bindings, unavailable head resolution and policy replacement between U and D.

2. **Atomic publication and binding updates.** Define the transaction boundary for publishing a current policy revision, updating the selected PRJ bindings and appending durable AUD evidence. The recommended operation uses owner handlers enlisted in one transaction under the existing writer gate; Host must not read or write owner tables directly. For a policy shared by multiple environments, the command must explicitly identify which bindings move. Bindings left on an older revision deny; no implicit bulk upgrade or fallback is permitted. Specify idempotent replay and unknown-commit reconciliation. Acceptance must prove rollback after a failure between owner writes and prevent admission against a partially published binding.

3. **Explicit project-without-environment applicability.** Define a distinct `project_without_environment` case, requiring matching PRJ/INV resolved absence and exactly one explicit project-level policy binding. There is no environment classification in this case: do not invent Development, skip applicability checks or fall back to another policy. The policy must explicitly support this scope; an environment-classification-only rule cannot implicitly allow it. Acceptance must distinguish legitimate absence from lookup failure and reject missing, ambiguous or environment-only bindings.

4. **One unambiguous rule representation.** Resolve the overlap between the reference proposal's `allowed_classifications` and `rules`. Prefer one canonical fixed representation for v1; if both fields remain, define their combination, conflict handling and validation precisely. Specify supported allow/deny predicates, explicit-deny precedence and deny for no matching allow. Unknown fields, actions, scope kinds or condition operators must not be ignored. No arbitrary expressions, inheritance or multi-policy composition are introduced. Acceptance must cover contradictory declarations, unsupported conditions and deterministic evaluation at both U and D.

These are design and acceptance inputs, not executed test evidence. The scope remains the missing resource-policy addition for `target.read`; T01–T05, provider access and deployment authorization are not reopened. Exact design approval remains pending, and unresolved resource policy continues to deny.

### Bounded design accepted — 2026-10-22

The four points above are resolved in the maintainer-accepted [Resource Policy v1 contract](../../../contracts/resource-policy-v1.md) and [ADR-0014](../../../decisions/0014-resource-policy-v1.md). PRJ owns unique current bindings and append-only binding history; IAM supplies exact policy identity/current revision. Legacy policyRevision values are preserved as non-authoritative nullable data, never automatically upgraded to Allow. Initial environment binding uses existing catalog authority and a validated current policy; replacement/publication remains offline-only. The contract explicitly identifies DTO, migration, privilege and future schemaVersion 2 request changes. The maintainer approved ADR-0014 and this contract for T06 on 2026-10-22, resolving the design gate recorded historically above. Implementation/verification remain pending, including the database/HTTP proof; no earlier NEEDS-DECISION or proposed wording in that historical discussion reopens the accepted choice.

## T06 implementation evidence — 2026-10-23

Local source now implements unique explicit bindings/history, exact initial environment binding under v2, offline-only replacement through the policy coordinator, current owner-selection ports and compatible nullable legacy policyRevision. The derived scope_id check/FK makes a history row bind to the exact immutable environment/absence scope; it is no second authority value and existing rows are derived without Allow backfill. Existing T05 data, ownership and integer-only test fixtures remain historical; production legacy policy reader always denies.

Environment v2 validates exact current IAM policy identity/revision/schema/scope but does not turn a read allow into project.manage. A deny-only read policy can be intentionally associated under an independently valid management grant. Mutation and clock floor/AUD commit precede 201/ETag; exact authenticated subject/body-bound receipt replay precedes mutable grant checks, preserves result identity and never grants new reads. Policy head publication and explicitly selected bindings/history/AUD use one transaction; unknown ACK is fenced/reconciled by exact receipt, known setup/save failures roll back without poisoning admission. Targeted PostgreSQL/HTTP and schema/grant tests provide local proof; exact final gates belong to the private T06 evidence. The [runbook](../../../operations/resource-policy-v1.md) covers operator approval/configuration/rollback. Broader workspace/catalog/list capability and real provisioning remain pending as recorded in the IAM owner slice.

## PR14 rejected bind atomicity — 2026-10-23

FEAT-PRJ-001 / MOD-PRJ co-owns correction 4355763870. New and replacement offline binds reject inactive, expired, expiry-equality and not-yet-effective policy heads. A second trusted-time check after AUD staging prevents committing a bind that expires during setup. Denial retains prior binding/revision/history and leaves no new IAM command receipt or AUD intent; known rollback does not close the gate. ResourcePolicyIntegrationTests reproduces all ten new failures before repair and now passes all 19 policy integration cases, including committed-bind replay after expiry/head deactivation and successful inactive publication. Future-head fixtures use only synthetic owned data; no scheduled publication or live mutation is added.


### T07 bounded-read consumer acknowledgment — 2026-10-23

Current PRJ ownership, exact project/environment scope and accepted policy binding remain authoritative. T07 tests exercise both scopes through the real enforcer; delayed-result binding changes deny disclosure. No PRJ owner API or data model change. Shared Host composition and the actual-owner HTTP fixture are mapped here; the new adapter/probe paths belong to [FEAT-INV-001](../../infrastructure-inventory/features/register-target.md). T07 keeps the existing seven-field metadata v1 wire shape.


### T08 shared implementation consumer — 2026-10-24

Accepted ADR-0015 adds the SEC owner runtime/Contracts/test graph and scoped Host/offline migration composition. The existing source/test mappings above cover the shared solution, authority/HTTP/audit/architecture consumers changed in the same increment. [FEAT-SEC-001](../../connection-secrets/features/secret-binding.md#t08-local-implementation--2026-10-24) owns exact new paths and local evidence. IAM neutral dependencies/current C intent do not add a circular SEC runtime edge; AUD keeps v1 stages/schema, metadata wire v1 and existing role permissions remain unchanged. Synthetic fixture/native proof does not authorize provider network access or change this module's business ownership.

## T10 project identity consumer proposal — 2026-10-25

[Artifact registration v1](../../../contracts/artifact-registration-v1.md) under proposed [ADR-0018](../../../decisions/0018-project-artifact-registration.md) needs an additive PRJ-owned current project-to-tenant/revision reader under admission. Existing ownership lookup expects a known tenant; ART must not supply one from its request/config or query PRJ tables. The proposed reader rejects inactive/missing/conflicting projects and preserves all catalog/environment/target/policy behavior and digests. No new PRJ persistence ownership, public project API or live operation; implementation and ART10-02/03/12/14 proof await exact acceptance.

### Exact T10.1a–f acceptance — 2026-10-25

NukeGeng approved exact ADR-0018 and Artifact registration v1, including this owner’s bounded design dependencies and NukeGeng’s ART/registry slice custody. This supersedes pending-approval language in the T10 proposal above only; wider feature/module status, existing accepted contracts and historical evidence remain unchanged. Implementation, ART10 tests, native/live scope, real provisioning and PR/merge evidence are still separate. No repeat design approval or extra Sol review is needed for covered T10.1a–f work.

## T10.1b project identity scope — 2026-10-25

Accepted ADR-0018 / Artifact registration v1; custodian NukeGeng. PRJ adds current projectId-to-tenant/revision lookup through its own port. Fetch at most two matching identities across all statuses, deny ambiguity/inactive/missing facts, and trust no body/config tenant. Existing project/environment/target behavior is unchanged. Actor: trusted current local subject; scope: exact current project; action: artifact.register or artifact.read. Source/tests are declared in private t10b-scope.json before edits. Negative tests cover wrong tenant/action/scope, missing/expired/changed owner facts and mixed inventory/artifact saturation; native Medium is explicitly authorized.

## T10.1b current project identity — local 2026-10-25

PRJ's new IProjectIdentityReader/ProjectIdentityReader resolves projectId without a caller tenant. At most two rows across all statuses detect ambiguous IDs under the historical tenant/project PK; inactive/deleted collisions cannot silently pick an active tenant. Host adapters call the owner port instead of querying PRJ rows directly. Four isolated SQL/real-role tests passed: exact owner projection, inactive cross-tenant collision denial, explicit artifact grant with project suspend/change-back and current membership revoke. A synthetic subject reference isolates owner behavior; these tests do not prove a browser session or HTTP artifact admission. No project data migration or environment/target behavior change.

## T11 proposed consumer seam — 2026-10-27

PRJ consumer acknowledgment for [Release and approval v1](../../../contracts/release-approval-v1.md) / [ADR-0005](../../../decisions/0005-immutable-release.md), awaiting exact human acceptance. Add neutral active project/application/environment/exact target-association Contracts/readers for a trusted release scope. Return actual revisions and classification; application/environment are independent project children. Do not require target.read policy to resolve release catalog identity, expose owner tables, modify existing read-policy hashes/bindings or silently create a new application/environment relation.

This is planned additive scope, not implemented source/test evidence or a change to the accepted T01–T10 baseline. Preserve current source/test mappings; Developer adds precise new paths and negative/compatibility tests only when the owning slice exists. R11-02/03/10/12/15 are required as applicable. No real database/provider/secret operation is authorized.

## T11.0 neutral policy environment reader — local increment

The local T11.0 increment adds a PRJ-owned neutral environment fact contract/reader for current exact tenant/project/environment, revisions and classification. It rejects inactive, mismatched and globally ambiguous IDs; caller holds admission or stopped custody and still owns all permission/policy checks. It does not use target.read policy, grant authority or access a provider. Real PostgreSQL owner tests use only the isolated kordra_test fixture. APR policy storage and binding commands remain unfinished.

## T11.1 shared graph acknowledgment — 2026-10-27

Accepted ADR-0005 adds a BCL-only REL Domain and test project to the solution and actual architecture graph. This graph change does not alter this owner’s runtime behavior or grant release authority. T11.1 owner ports, persistence and HTTP remain in progress.

## T11.1 neutral release catalog port — local increment

IReleaseCatalogReader reads active same-tenant project, application, environment and exact target association under caller-held admission. It rejects ambiguous IDs and mismatched environment; application and environment independently belong to the project. This port grants no permission and does not replace INV target identity agreement.

### T11 approval environment identity consumer

The additive internal IApprovalEnvironmentIdentityReader port resolves only PRJ-owned active environment/project facts from a unique environment ID, with no caller-supplied tenant or project. Existing scoped owner validation rejects collisions even when another tenant copy is inactive. Approval Host consumes the port under shared admission/capacity and independently resolves permission. Two isolated PostgreSQL owner/adapter cases pass. Existing catalog HTTP schemas and accepted ownership remain unchanged.
