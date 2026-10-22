---
id: FEAT-INV-001
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-20
module_id: MOD-INV
feature_id: FEAT-INV-001
phase: MVP
source_paths: [".github/workflows/product-ci.yml", "Kordra.slnx", "src/Host/Kordra.Api/CatalogMutationCoordinator.cs", "src/Host/Kordra.Api/Kordra.Api.csproj", "src/Host/Kordra.Api/LocalApiHost.cs", "src/Host/Kordra.Api/Program.cs", "src/Host/Kordra.Api/packages.lock.json", "src/Host/Kordra.DbMigrator/InfrastructureInventoryMigrationCommand.cs", "src/Host/Kordra.DbMigrator/Kordra.DbMigrator.csproj", "src/Host/Kordra.DbMigrator/Program.cs", "src/Host/Kordra.DbMigrator/packages.lock.json", "src/Modules/InfrastructureInventory/Contracts/IInventoryReader.cs", "src/Modules/InfrastructureInventory/Contracts/InventoryReadResult.cs", "src/Modules/InfrastructureInventory/Contracts/Kordra.Modules.InfrastructureInventory.Contracts.csproj", "src/Modules/InfrastructureInventory/Contracts/TargetMetadata.cs", "src/Modules/InfrastructureInventory/Contracts/packages.lock.json", "src/Modules/InfrastructureInventory/Infrastructure/InventoryDbContext.cs", "src/Modules/InfrastructureInventory/Infrastructure/InventoryDbContextFactory.cs", "src/Modules/InfrastructureInventory/Infrastructure/Migrations/20261022065737_InitialTargetRegistry.Designer.cs", "src/Modules/InfrastructureInventory/Infrastructure/Migrations/20261022065737_InitialTargetRegistry.cs", "src/Modules/InfrastructureInventory/Infrastructure/Migrations/InventoryDbContextModelSnapshot.cs", "src/Modules/InfrastructureInventory/Kordra.Modules.InfrastructureInventory.csproj", "src/Modules/InfrastructureInventory/TargetRegistryStore.cs", "src/Modules/InfrastructureInventory/UnavailableInventoryReader.cs", "src/Modules/InfrastructureInventory/packages.lock.json"]
test_paths: ["tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/InfrastructureInventory.Bootstrap.Tests.csproj", "tests/InfrastructureInventory.Bootstrap.Tests/InventoryReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/LocalApiHostTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/packages.lock.json", "tests/ProjectEnvironment.Tests/CatalogTestFixture.cs", "tests/ProjectEnvironment.Tests/ProjectCatalogTests.cs", "tests/ProjectEnvironment.Tests/ProjectEnvironment.Tests.csproj", "tests/ProjectEnvironment.Tests/TargetAssociationTests.cs", "tests/ProjectEnvironment.Tests/packages.lock.json"]
---

# FEAT-INV-001 — Register infrastructure target and discover capability

## Product delivery slices — T05/T07 approved; T09 remains proposed (2026-10-20)

The implementation handoff separates T05 registration, T07 enforced fake read and T09 real OP-P0 reads. Accepted ADR-0012 and authority/admission v1 adopt the T05/T07 metadata/read contracts. T05 creates stable Active/Disabled/Deleted target identity, exact tenant/project/environment and revision under target.manage; no credential/provider call. IDs are never reused. T07 requires trusted target.read, U/D audit and actual output-count race assertions; fakes never become production authority. These slices are approved for implementation, not yet implemented.

T09 consumes reviewed SEC binding and independently verified cluster/context identity. Approve exact Kubernetes/Helm resources/verbs/fields, supported SDK/CLI versions, TLS and secret-bearing metadata exclusions before provider integration. target.read implies no Secrets/exec/values/manifests/write permission. Proposed read envelope: schemaVersion=1, exact target/selection/binding, capturedAtUtc, coverage/freshness and bounded sanitized resources. Missing/partial/stale coverage is explicit; network/auth failure is not observed unhealthy. No namespace/context fallback.

Acceptance INV-F1: idempotent registration with foreign ownership/conflicting binding/stale ETag denial. INV-F2: actual HTTP enforcer with delayed provider, scope switch/revoke, oversize/audit failure releases no unauthorized output. INV-P0: zero mutation calls, exact target identity/allowlist, finite timeouts/freshness and secret-free errors, plus separately authorized live evidence for actual OP-P0. Preserve old denied paths/BCL consumers until explicit migration; versioned positive API examples and consumer tests are required. Live target access is not authorized by this design.


## User story / outcome
**Actor:** Operator / Tenant Admin. **Goal:** Register infrastructure target and discover capability. **Primary owner:** `MOD-INV`. **Phase:** MVP.

## Pre-conditions
Credential/agent binding verified; target ownership known.

## Main flow
Register target metadata → verify connection through scoped credential/agent → discover capabilities/namespace → store observed timestamp → set ownership mode.

## Alternatives / exceptional flow
Unreachable target becomes Pending/Unknown; stale discovery cannot support a successful preflight.

## Messages and integration contracts
`target.registered.v1; target.capabilities.changed.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

### I2 runtime-authority handoff — proposed

For the bounded candidate in [FEAT-IAM-001/I2](../../identity-access/features/authorize-resource.md#i2--runtime-authority-contract-increment-proposed), MOD-INV remains the source of target identity, lifecycle eligibility, target revision and tenant/project/environment associations. Those metadata facts do not prove provider identity, credential validity or observed inventory; those remain separate owner decisions. Resolve the target side of each association against MOD-PRJ's authoritative result. Missing, disabled, ambiguous, stale or unavailable target evidence denies access and must not reveal cross-tenant existence.

At the original I2.7 proposal, formal ownership and exact lifecycle states eligible for read were unresolved. Accepted ADR-0012 assigns NukeGeng accountability for the approved Foundation slice and MOD-INV ownership of target identity/lifecycle facts. T09 provider access remains separately proposed; the existing local API inventory denial remains unchanged until T06/T07 implementation evidence passes.

## Negative & recovery scenarios
Bind agent from different org; namespace outside permission; insecure certificate bypass.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Every target has org, environment, mode and capability evidence before deployment.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## I0 — OP-P0 read-only workstation proposal

**Proposed only; no product implementation or operational approval.** This bounded increment prepares the inventory part of [OP-P0](../../../product/operator-pilot-plan.md) and [OP-01/02/07/09](../../../product/operator-workstation.md). It retains the feature's MVP phase and broader registration outcome above; I0 does not implement registration, credential binding, ownership-mode changes or deployment preflight. It does not broaden [OSS Beta acceptance](../../../product/oss-beta-acceptance.md).

All field names, result labels and contract shapes introduced below are **illustrative/proposed until approved** by the relevant owners. They are not accepted schemas, permission rules or a runtime state machine. Existing message candidates remain candidates; no new public API/event or consumer migration is adopted here.

### Proposed trusted target/context binding

- Resolve the authenticated principal and tenant/resource authorization from trusted identity and ownership information. A profile name, supplied tenant ID, endpoint alias or locally selected context is not proof of identity or access. Platform scope grants no implicit tenant inventory access.
- Associate the selected project/environment with a target reference, expected provider identity and bounded resource scope (for example, a namespace when applicable). Compare an independently observed provider identity with that trusted expectation before associating inventory with the target. The provider-specific identity mechanism and how it is trusted remain unresolved; an unavailable identity cannot be replaced by a display-name match.
- Bind each read attempt and its result to the target, context selection revision, tenant/environment scope and authorized credential reference. A context switch, changed binding or mismatched provider identity invalidates in-flight results for the active selection; do not silently retarget or merge results from another scope.
- Use only an approved read-only operation set through a scoped credential/agent boundary. No automatic fallback to another context, broader credential, namespace or TLS bypass. Local credential availability does not establish server-worker access. No real credentials, endpoint addresses or private topology belong in this specification or public evidence.

### Proposed inventory/capability evidence and freshness

An illustrative observation record could carry `targetRef`, `contextRevision`, trusted `scopeRef`, `providerIdentityRef`, `observerRef`, `readAttemptRef`, `capturedAt`, `receivedAt`, resource identity, observation coverage and sanitized evidence provenance. Capability evidence would identify the specific read operation and scope actually observed, its observation time and whether it was reported, observed, denied or unknown. These are proposed fields, not a storage or transport decision.

- Display only permitted inventory/capability evidence tied to the verified selection. Reported metadata remains labeled reported; provider feature advertisement is not proof of effective permission. Successful discovery of one resource does not prove complete inventory, general health or write capability.
- Evaluate freshness from capture time and a future owner-approved, signal-specific freshness policy; receipt time or reconnect alone must not refresh old evidence. Missing timestamps, untrusted/future timestamps, stale evidence and partial/missing coverage cannot produce a current healthy or deployment-ready claim. Exact clock tolerance, age limits and retention are `[NEEDS-DECISION]`.
- Keep last-known evidence and its original timestamp distinct from current observation availability, subject to current authorization. An empty authorized response is distinguishable from an absent response; a denied or incomplete collection cannot be shown as an empty healthy target.
- Local Mac sleep/closure or disconnection interrupts local observation. Reconnect requires scope/identity/permission revalidation and a new bounded read before current status is asserted. It cannot imply durable background execution or replay any mutation.

### Proposed result distinctions (illustrative labels, not transitions)

Connection evidence and observed resource health are separate dimensions; one aggregate success flag would lose the reason and coverage.

| Illustrative/proposed label | Evidence required / intended interpretation |
|---|---|
| `Reachable` | Authenticated, identity-matched read completed for the named scope; proves that read path only, not workload health or write authority. |
| `AuthDenied` | Explicit authentication/authorization rejection; no escalation or retry with broader authority. Distinguish local policy denial from provider rejection using sanitized reasons. |
| `TimedOut` | Read deadline elapsed without a conclusive response; server/workload health remains unknown. |
| `Unreachable` / `Disconnected` | Observed transport failure or loss of the client/service connection; no inference that the target is unhealthy. |
| `ObservedUnhealthy` | Fresh, identity-bound, authorized observation positively indicates a named resource is unhealthy within its observed coverage. A transport/authentication failure cannot supply this evidence. |
| `Unknown` / `NotConfigured` | Evidence missing, stale, unsupported, untrusted or incomplete, or required binding absent; retain the specific reason and last-known timestamp where authorized. |

A reachable provider may have an unhealthy workload; an unreachable client may have no current workload evidence. Late or out-of-order observations cannot overwrite a newer matching observation or cross into a newly selected context. Exact ordering/version and classification contracts require owner review before implementation.

### Proposed module seams (no ownership decision)

| Existing module / feature | Seam to discuss with its owner before implementation |
|---|---|
| [ProjectEnvironment / FEAT-PRJ-001](../../project-environment/features/environment-catalog.md) | Resolve project/environment ownership and target association through reviewed contracts. Inventory must not infer environment classification from a profile label or write ProjectEnvironment storage. Profile-to-target selection semantics remain proposed. |
| [Connections & Secrets / FEAT-SEC-001](../../connection-secrets/features/secret-binding.md) | Consume only an authorized connection/credential reference and bounded read result. Secret resolution, expiry/rotation and local-versus-worker trust belong to the credential seam; inventory does not read plaintext secrets or decide a secret provider. |
| [Identity / FEAT-IAM-001](../../identity-access/features/authorize-resource.md) | Obtain trusted subject/scope and current policy decisions; revalidate after revocation or reconnect. Existing catalog candidates such as `target.read` do not grant registration, credential binding or mutation. Exact action mapping and recheck policy require approval; no role-name authorization. |
| [Observability / FEAT-OBS-001](../../observability/features/health-verification.md) | Correlate scoped inventory/capability evidence with health observations and freshness through reviewed contracts. Inventory does not assume ownership of health policy, telemetry storage or status projections; observed inventory is not deployment verification. |

These discussions preserve the existing module definitions and dependency declarations; they do not authorize new project references, direct cross-module storage reads or new ownership. Audit correlation/denial evidence must be sanitized and scoped; its storage and retention are not selected here.

### Planned acceptance / negative matrix — not executed

All cases below are future unit/contract/integration acceptance plans using synthetic records, fake clocks and an instrumented fake read adapter. They require no live target or credential. Proposed assertions and labels remain subject to owner approval; no test files or passing results are claimed.

| Case / synthetic setup | Planned acceptance assertions |
|---|---|
| Valid selection and fresh read | Matching trusted tenant/environment/target/context and permitted read return scoped inventory with capture time, provenance and coverage; only exercised read capabilities are evidenced. No external write call is recorded. |
| Wrong target or context | Same display name with different provider identity, wrong namespace, or context changed during a delayed response: reject association, discard result for active selection, preserve a sanitized mismatch reason; do not fall back to another context. |
| Wrong tenant / platform-only principal | Forged body tenant ID, another tenant's target/credential reference, or platform permission without a tenant grant: deny before provider dispatch and expose no inventory, cached data or evidence from the other scope. |
| Missing permission | Authenticated principal lacks scoped read permission: no provider call, no protected cached inventory, scoped denial evidence; a role label cannot override denial. |
| Revoked/expired permission or credential | Revoke while a read is pending and before reconnect; late results are not exposed under invalid authority, cached protected data is withheld and a subsequent read is denied. No broader credential fallback. |
| Insecure TLS bypass | Invalid/untrusted certificate or hostname, or request to disable validation: reject the connection under the approved trust policy, record a sanitized trust failure, and make no inventory-success claim. No bypass retry. |
| Stale or missing observations | Advance fake clock beyond approved freshness, omit capture time, inject future time, partial coverage or no response: show reason/age and unknown current health; reconnect/receipt does not renew old evidence. Empty successful list remains distinct. |
| Timeout / transport loss / explicit auth denial | Inject each separately: retain deadline, connection or authorization classification; none produces observed-unhealthy or healthy status. Fresh explicit unhealthy evidence affects only the observed resource/coverage. |
| Disconnect / reconnect / context switch | Disconnect during a read, then reconnect with changed context or revoked grant: revalidate identity/scope/authority, perform a fresh permitted read only, and reject delayed prior-context results. No automatic operation replay or Mac-close survival claim. |
| Accidental write attempt | Request create/update/delete, ownership-mode change, deploy, scale, backup/restore, exec, arbitrary shell, broker consume/ACK/purge or MQTT publish: reject outside the I0 operation set before dispatch, even with an overprivileged fake credential; adapter mutation-call count remains zero. |
| Duplicate/out-of-order / observer crash | Duplicate response, older response arriving last, or local observer restart: no duplicate facts or false freshness; incomplete attempt remains unknown until a new authorized read, with no mutation replay. Dedup/order mechanism is still proposed. |
| Expired approval / unknown agent outcome | An expired write approval never enables an I0 write; reject regardless of approval. A lost agent result remains unknown and cannot imply success or trigger mutation retry. Read permission remains independently required. |
| Evidence redaction and isolation | Inject synthetic secret markers and cross-scope observations into errors/results: neither appears in logs, exports or another tenant's view; retain only authorized sanitized reason/correlation evidence. |

### Unresolved decisions and implementation boundary

- `[NEEDS-DECISION]` Assign module/feature owners and review the exact safe read operation/resource set for the owner-selected Kubernetes/Helm read-only OP-P0 target (2026-10-18). This target preference is distinct from credential-provider selection, RBAC or the write-capable Beta driver; the [A–F landscape](../../../product/technology-landscape.md) selects none of those.
- `[NEEDS-DECISION]` Trusted provider/context identity source, binding establishment, credential provenance and least-privilege scope, TLS trust provisioning, revocation/revalidation behavior and local-versus-remote secret access. Resolve with Identity and Connections/Secrets owners; this task does not accept a policy or access credentials.
- `[NEEDS-DECISION]` Architecture/host placement, persistence, observation contract ownership/consumers, freshness/ordering/retention and audit policy. [ADR-0009](../../../decisions/0009-source-architecture.md) remains proposed; [ADR-0008](../../../decisions/0008-domain-bootstrap-validation.md) accepts only the narrow artifact-domain/test bootstrap, not inventory runtime or new packages/projects.
- `[NEEDS-DECISION]` Rights-owner company access, permitted credential handling, private evidence storage, data/IP reuse and publication rights. No document or test plan supplies company authorization. No infrastructure access or provider probes occur in this proposal; any future live observation needs separate explicit authorization and applicable reference-environment safety controls.
- Completion of this specification task still requires coordinator gates, independent review and contributor triage against current content. Product implementation, tests, feature acceptance and operational readiness remain pending. No historical `validation_failed` task is replayed or promoted by this proposal.

## Source & test traceability
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.

## Proposed feasibility evidence — OPP0-ARCH-SPIKE-001

### Original native-sandbox attempt — blocked

Case `OPP0-ARCH-SPIKE-001`, task `F0-OPP0-ARCH-SPIKE`: actor `repository-contributor`, permission `repository.write (documentation only)`, scope `local-development` documentation/experiment. The owner selected **Kubernetes/Helm read-only** as the first OP-P0 design target; this grants no provider access or deployment permission. Canonical architecture adoption remains in [source architecture adoption](../../../engineering/source-architecture-adoption.md); no competing architecture or backlog is adopted.

At repository HEAD `2aa156f4e6e3afe3cc648186ac6a3242781dc71b` with pre-existing working changes preserved, disposable `Disposable.Proof.*` fixtures/scripts were prepared only under private `.amr-local/op-p0-architecture-spike/`. Static XML inspection found nine fixture projects and zero `PackageReference` entries. Installed SDK `10.0.400`, runtime `Microsoft.NETCore.App 10.0.11` and shared framework `Microsoft.AspNetCore.App 10.0.11` were observed. Offline restore timed out after 90 seconds (runner code `124`, script exit `1`); process inventory failed (`pgrep` exit `3`, sysmond unavailable). No retry, escalation or alternate process enumeration followed. Python terminated/waited for the direct restore child; descendant absence remains unverified. No fixture HTTP host started.

**No feasibility pass:** build, evaluated framework/package inventory, built assembly/project conformance, both forbidden-edge injections, restored-valid verification and HTTP liveness/tenant-query/mutation assertions did not run. Those planned fixtures cannot establish IAM/tenant policy, actual inventory, Agent isolation, product architecture or CI enforcement. No product host, dependency, contract, permission rule or ADR is adopted; source/test mappings remain empty. Broad gates, current task-receipt verification, independent native High review and exact-content contributor triage remain with the coordinator.

Private reproducibility/evidence: `source.sha256` digest `6db00aef492d2e08d3d4d44d73b86257c8c5587ea3e3bb76a610ad77c59de50d`; `evidence.sha256` digest `e6fe6984ebb4ba9440e3849f5bf4b146b71c16a592d5a78b2a70787fa88cbe5d`. These SHA-256 manifests bind prepared source and actual failed-attempt records, including the original runner; later runner safety/logging corrections have not executed. Initial timeout stdout was not retained. An SDK certificate sentinel appeared in the private CLI home; certificate/keychain side effects are unverified, and no store was inspected. The original runner omitted the certificate-generation disable flag; the unexecuted correction supplies it. These records prove neither completion nor approval.

`[NEEDS-DECISION]`: reconcile the local execution/cleanup blocker before any new authorized attempt; then review the missing host/boundary evidence. Exact Kubernetes/Helm resources/RBAC and metadata disclosure (including Secrets/values/manifests), trusted identity/bootstrap, credential/provider/storage choices, existing ADR-0003/0009 acceptance criteria and architecture/security/maintainer plus rights-owner approvals remain unresolved. This experiment supplies no company access or production authorization and does not satisfy ADR-0009 acceptance.

### Coordinator reconciliation — local feasibility, not product adoption

After the original native task completed, the coordinator separately reconciled the failed local attempt. The original runner/restore PIDs and direct children were absent in available process inventory; the original host had never started. Deeper-descendant visibility and the precise sandbox timeout cause remain unproved. Original failure artifacts/manifests above are retained unchanged. Subsequent runs used the active coordinator's existing authorized local runtime, **not a successful native-sandbox execution or permission escalation**; no company/provider operation or automatic replay occurred.

A fresh local artifact directory built successfully, then reproduced `OPP0-SPIKE-REFS-001`: Host source used InventoryContracts/ExecutionProtocol types not declared in its project/check allowlist. The repair added those two explicit public contract project references; Agent-to-ControlPlaneDomain and Contracts-to-implementation prohibitions were retained. That failed local attempt remains archived separately. The repaired fixture in private `.amr-local/op-p0-architecture-spike-fixed/` completed with script exit `0`:

| Disposable check | Observed result / limit |
|---|---|
| SDK/framework/package inventory | SDK 10.0.400; .NET/ASP.NET Core 10.0.11; nine fixture projects, zero PackageReference/restored packages. Evaluated package source is installed SDK local library-packs; no remote feed is configured. No K8s/Helm SDK selected. |
| Valid initial and restored graph | Release builds plus direct project and built PE-metadata reference checks passed. Injection source was removed and valid fixtures rebuilt. |
| Forbidden Agent → ControlPlaneDomain | Compiled typed edge emitted a real assembly reference; project and assembly checks rejected it with expected checker exit 1. |
| Forbidden Contracts → other module implementation | Compiled typed edge was rejected by both checks with expected checker exit 1. |
| Ephemeral IPv4 loopback host | 14 HTTP assertions: liveness, constant-denied inventory including forged tenant/platform queries, rejected mutation methods and unknown route. Listener stopped and refused connections before process exit. |

The HTTP fixture denies every inventory read: it proves **no working IAM/tenant authorization or positive read path**, cluster inventory, credential handling, TLS/bootstrap policy, durable execution or Mac-close survival. Synthetic direct-reference checks do not enforce actual product projects/CI or arbitrary evaluated MSBuild, reflection/transitive trust and runtime authority. No product source, host, contract or dependency is adopted. The user additionally selected **API local on Mac first** as a design preference; reads require Mac/API availability, while later long-running workflows still need independent hosted execution.

Reproduce the repaired disposable fixture with `python3 .amr-local/op-p0-architecture-spike-fixed/run_spike.py` in an authorized local runtime; preserve existing result manifests before intentionally starting a new measurement. Its copied README retains the original-attempt path; use this repaired command for this evidence. Source manifest SHA-256 `c33b51f34358392aa518e76a3ea859fae40bffc45af0a1809bc3b8bd9e31d3fc`; evidence manifest SHA-256 `2e8948fdce87275c89327721e07c134baa84ab3335383c14ddf402d76d54c45e`. The coordinator independently checked manifests, command outcomes, restored files and evaluated package inventory. Review must inspect the repaired artifact directory as well as the retained original failure; native review of the blocked attempt does not validate this later result. Current-content full gates, independent native High re-review and exact-content contributor triage are required for task acceptance. Canonical readiness remains in [source architecture adoption](../../../engineering/source-architecture-adoption.md).

Host placement is an owner design preference, not accepted architecture. Exact resources/fields/RBAC (especially Helm secret-bearing metadata), trusted principal/target bootstrap, credential provider/key custody, scoped authorization, storage/audit and owner approvals remain unresolved. This bounded feasibility result alone does not satisfy ADR-0003/0009 acceptance or authorize real infrastructure access.

## I1 — accepted local host/inventory bootstrap

Authority: scoped [accepted ADR-0010](../../../decisions/0010-local-readonly-bootstrap.md), explicitly approved by the maintainer on 2026-10-18. Primary owner FEAT-INV-001 / MOD-INV; implementation custodian engineering-maintainers. Use case: an actual local .NET host and inventory read seam with no real data/provider. Actor is a local developer/contributor; scope local-development/bootstrap; no tenant data permission is granted. `target.read` and trusted tenant/target authority remain prerequisites for later real reads. Full feature status stays proposed.

Task `F0-OPP0-BOOTSTRAP` wrote the existing eight mapped C# implementation/test files under the four ADR-0010 projects. Fresh task `F0-OPP0-BOOTSTRAP-FINALIZE`, in existing continuation `KORDRA-OP-P0-BOOTSTRAP-CONT-001`, inspects that actual source without recreating it or promoting the failed task. Finalization actor: `repository-contributor`; permission: `repository.write (documentation only)`; scope: `local-development`. At inspected HEAD `2aa156f4e6e3afe3cc648186ac6a3242781dc71b` plus pre-existing working-tree changes, this is local source implementation, **not Verified**, full feature completion or OP-P0 acceptance. Static inspection found no defect requiring a source/test edit; this finalization updates I1 evidence only.

The frontmatter enumerates 13 existing source/configuration paths and five existing test/project/lock paths for I1; the earlier unmapped I0/spike records describe their historical scope. Contracts has no project/package dependencies; Inventory runtime references only its own Contracts and excludes `Contracts/**/*.cs`, including nested generated C# files. Host references only Inventory runtime/Contracts. All three production lock files contain no package dependencies; test-only versions remain pinned to ADR-0010. Root `Kordra.slnx` contains the four bootstrap projects alongside the two Artifact projects. The existing `artifact-domain` CI job restores locked, builds and tests the root solution; this describes workflow configuration, not a current GitHub CI result. Artifact behavior is unchanged by this finalization. The shared solution/CI ownership correction belongs to FEAT-AMR-001 / ENG-TOOLING task `F0-OPP0-SHARED-OWNERSHIP-FIX`, whose checkpoint records completion; no changeguard behavior or ownership mapping is changed here.

### Implemented source shape and in-process contract consumers

- BCL-only Contracts defines `IInventoryReader.ReadAsync(CancellationToken)`, `InventoryReadResult` and immutable display-only `InventoryResource` metadata. Unavailable is distinct from an available empty collection. Available results defensively copy their input and reject null collections/entries; resource kind/name reject null/blank values. These fields establish no tenant/target identity, policy or health. No public HTTP inventory schema, event or message version is introduced; existing message candidates remain unimplemented.
- Inventory runtime consumes its own Contracts through `UnavailableInventoryReader`: every non-cancelled call returns unavailable with no resources, while a pre-cancelled call throws cancellation. The Host consumes runtime/Contracts solely for unavailable DI registration. `InventoryReaderTests` consumes the in-process seam with the only positive reader fixture; `LocalApiHostTests` verifies the production registration. HTTP inventory handling never invokes a reader or exposes fixture data. There are no other adopted product consumers or consumer migrations.
- `Program.Main` starts `LocalApiHost.Create()`, default port 5127; tests explicitly select port 0. The factory rejects ports outside 0–65535 and uses `WebApplication.CreateEmptyBuilder` with fixed application/environment/content-root settings, explicit Kestrel Core and routing, and no default environment/appsettings/command-line configuration or logging providers. It listens explicitly on `IPAddress.Loopback` with HTTP/1. No CLI/env/config provider can select a binding or fake provider through this bootstrap composition. An in-process application owner remains trusted; this is not isolation against arbitrary host-code replacement.
- `GET /health/live` returns constant process-only `alive` text. `/inventory` and `/api/inventory` (including child paths and any method) return empty 403 responses regardless of absent/forged tenant, platform, role, permission or approval inputs. Other non-GET methods return empty 405 with `Allow: GET`; unknown GET routes return empty 404. Loopback grants no authority. No IAM, positive HTTP inventory read, CORS proxy, credential/provider access, network client, persistence, messages, worker/Agent or UI is implemented.

### Authored checks — not executed in this native task

| Test file | Concrete cases / limits |
|---|---|
| `InventoryReaderTests.cs` | `MissingProvider_RepeatedAndParallelReads_RemainUnavailable`, `CancelledRead_ThrowsWithoutChangingSubsequentReads`, `PositiveFixture_IsOnlyAnInProcessTestConsumer`, `Result_DefensivelyCopiesInput_AndRejectsCollectionMutation`, `EmptyObservation_IsDistinctFromMissingObservation`, `InvalidResultInputs_AreRejectedPredictably`. No real provider or target. |
| `LocalApiHostTests.cs` | Real loopback HTTP through `Liveness_UsesRealIpv4Loopback_AndListenerIsReleased`; invalid-port rejection; missing/forged authority denial; repeated/parallel requests; mutation/unsupported methods and unknown routes; `HostileSyntheticEnvironment_CannotRebindOrEnableProvider`; `BindingConfigurationAddedAfterCreation_CannotOverrideExplicitListener`. Tests use ephemeral ports and proxy-disabled stdlib clients. Synthetic environment changes are serialized and restored. The retained parent correction allows a three-second bounded timeout only for non-primary IPv4/IPv6 loopback probes, which can be unrouted on macOS; a successful connection still fails. After stop, the primary IPv4 socket must throw `SocketException` (timeout is not accepted), and the same port must permit rebinding. These cases establish no actual tenant authorization. |
| `ArchitectureTests.cs` | Actual project XML, restored assets and root solution graph; actual PE references, cycles and duplicate Contracts type rejection; negative XML edges/packages/imports/cycles; `CompiledNegativeFixtures_RejectActualRuntimeAndArtifactAssemblyEdges`. The retained analyzer-compliant `Assert.DoesNotContain(inventory.Types.Intersect(contracts.Types), name => name != ".<Module>")` still rejects duplicate Contracts types in runtime. Fixture compilation uses only temporary projects, actual built product references, installed pinned SDK, an explicit empty NuGet source list, disabled certificate generation/build servers and bounded child-process cleanup. Its isolated child process gets existing private `HOME` and CLI-home directories; Linux also gets a private `XDG_RUNTIME_DIR` for the NuGet migration mutex. The execution fixture is a stand-in, not a real Agent; fixtures never build/write product projects. |

### Retained failure and verification boundary

Read-only inspection of private `.amr-local/goals/KORDRA-OP-P0-BOOTSTRAP-001.json` confirms the historical native `gpt-6.1-sol/high` execution completed with source retained, but task status remains `validation_failed` / control `blocked`. Its SHA-256 remains `5454dcd6b22630b2801b15641966560be7015517e0d44bf0131324f1036f8a4f`, matching `.amr-local/op-p0-bootstrap-scope-reconciliation.json`. The first gates failed on the unrelated shared-owner tooling fixture and an xUnit analyzer assertion. The two parent test corrections above are preserved; no failed checkpoint is edited, replayed or accepted by this documentation update.

Historical coordinator evidence, **not a fresh all-suite pass for this finalization**:

- `.amr-local/op-p0-bootstrap-targeted-results.json` records Release root build exit 0 and root test exit 1. Scope reconciliation records 48 Artifact tests passed and 18 inventory tests passed / one non-primary loopback timeout failed before the probe correction.
- `.amr-local/op-p0-bootstrap-liveness-reproduction.log` records the corrected single liveness case passed (one test, zero failures). It does not establish a full-suite pass.
- The completed dependency's checkpoint separately records passing gates, stable independent native High review and contributor triage for the shared-owner fixture correction. That review covers the tooling fixture; it is not independent review or acceptance of this bootstrap finalization.

The required legacy `python3 docs/tooling/amr.py goal status --id KORDRA-FOUNDATION` returned exit 2, `another goal executor is live in this checkout`. Read-only inspection of the existing continuation finds `F0-OPP0-SHARED-OWNERSHIP-FIX` `completed`, control `active` and `F0-OPP0-BOOTSTRAP-FINALIZE` `running`; no checkpoint is changed or executor bypassed. The continuation's caller-reported High selection remains unverified here; historical receipts do not prove this turn's model/effort. The coordinator must bind the current native execution and terminal receipt before acceptance.

Local finalization static checks: `python3 docs/tooling/doccheck.py --strict-sources` passed (170 Markdown files, 152 tracked IDs, 21 modules; zero errors/warnings); scoped `git diff --check` passed. All 18 mapped files exist; the four project XML files, root solution XML and four lock JSON files parse. A before/after content snapshot of tracked and non-ignored untracked files identifies only this feature document as changed by finalization. These are documentation/structure checks, not compilation or runtime evidence.

Per this task's restrictions, no restore/build/test, HTTP host execution or process probe runs in this sandbox. Current full AMR gates (which invoke restore/build/test), independent High review, exact-content contributor triage, acceptance and roadmap/traceability/change-history reconciliation remain coordinator steps. Static documentation validation cannot establish runtime correctness, and dependency/historical results cannot satisfy current-content gates. The coordinator must run `python3 docs/tooling/amr.py gates --base 2aa156f4e6e3afe3cc648186ac6a3242781dc71b`, including strict docs/source mapping, locked restore and Release root build/test with the existing Artifact suite, against final content. No fresh product-test pass, finalization review or current GitHub CI is claimed.

Duplicate messages, worker crash and unknown outcome have no write/durable workflow to exercise in this bootstrap; the broader I0 matrix and trusted IAM/target/credential/provider decisions remain pending. No debt is added/closed or ADR accepted by this documentation edit. ADR-0003/0009 remain proposed; real targets, credential/provider/IAM runtime and company operations remain outside approved scope. Only after current bootstrap gates, independent review, triage and acceptance may the coordinator update the roadmap and begin the requested folder framework while preserving current source/solution paths. Full FEAT-INV-001, OP-P0 and the thread GOAL remain incomplete.

### Coordinator final acceptance — 2026-10-18

The preceding finalization narrative describes what the restricted native editing task could observe before its parent validation. Subsequent **current-content** gates passed: strict doccheck (170 Markdown/152 IDs/21 module entries at review), scope registry, all 114 tooling tests, locked restore, Release build (zero warnings/errors), 48 Artifact + 19 bootstrap tests (zero failures/skips), and working-tree ownership (20 changed source/test/build paths, zero problems).

`F0-OPP0-BOOTSTRAP-FINALIZE` in the existing local continuation `KORDRA-OP-P0-BOOTSTRAP-CONT-001` was accepted after independent native `gpt-6.1-sol/high` Code Review completed on stable content `98ae0ffb57ddd4b6b74931721996ffc5e0b1f5ceaa8761c9c40263852a7bb17a`. Review thread `01a14d9b-9ebb-7913-8810-35dcfd358a6a`, turn `01a14d9b-a222-7c52-b983-842f37b3f3ff`; no actionable findings. Contributor triage validated both test repairs against the full suite and review; the old failed checkpoint remains unchanged. Native execution/review observations prove those task sessions, not desktop model selection. Private receipts: `.amr-local/op-p0-bootstrap-finalize-step.json` and `op-p0-bootstrap-finalize-triage.json`.

A separate coordinator smoke ran the actual built `Program` with hostile synthetic CLI/environment URLs and fake-provider flags: liveness 200, both inventory prefixes 403, unsupported liveness POST 405, graceful exit 0 and listener released. Evidence is private `.amr-local/op-p0-bootstrap-entrypoint-evidence.json`; it reads no real credentials or targets. Local verified increment only: source is uncommitted, new GitHub CI/provider automatic review and full feature/OP-P0 completion remain unverified. The subsequently requested [folder framework](../../../engineering/repository-folder-framework.md) adds reservations only and does not change this project graph or runtime behavior.

The finalization above is **local task acceptance** only. ADR-0010's clean GitHub CI criterion remains pending until this content is published through an authorized PR and checked on its exact SHA; no full remote acceptance or merge permission is inferred.

### Linux fixture first-run recovery — 2026-10-19

PR #2's first `artifact-domain` run failed in `CompiledNegativeFixtures_RejectActualRuntimeAndArtifactAssemblyEdges` before fixture compilation: the sanitized child process pointed `HOME` at a path that did not exist, and Linux .NET could not initialize NuGet's named migration mutex without a valid runtime directory ([failed CI run](https://github.com/NukeGeng/KORDRA/actions/runs/39188376089)). The fixture now creates its isolated HOME and CLI-home first, then supplies a user-only `XDG_RUNTIME_DIR` on Linux. Its empty NuGet source list, product assembly inputs, forbidden-edge assertions and cleanup bounds are unchanged. The upstream runtime behavior is tracked in [dotnet/runtime #135726](https://github.com/dotnet/runtime/issues/135726).

Current local verification: the targeted fixture passed on macOS. A sanitized tracked-file checkout with the corrected test passed locked restore, Release build (zero warnings/errors), and the exact root test command in `mcr.microsoft.com/dotnet/sdk:10.0.400` on Linux: Artifact 48/48 and Inventory Bootstrap 85/85, zero skipped. Hosted `docs-integrity` and `artifact-domain` checks then passed on implementation commit [`38da931`](https://github.com/NukeGeng/KORDRA/commit/38da931edae3c36182c46712502b242598f9e0da): [documentation run](https://github.com/NukeGeng/KORDRA/actions/runs/39188847531) and [product run](https://github.com/NukeGeng/KORDRA/actions/runs/39188847534). This validates the bootstrap increment on that SHA; it does not complete full FEAT-INV-001 registration.


## Shared Foundation composition — T01–T03 implementation boundary

T01 adds IAM persistence and its separate migrator/tests; T02 adds AUD Contracts/persistence/tests and the AUD migrator command. T03 adds IAM/AUD/Security project composition for the explicitly selected local-pilot authentication profile. The default API profile remains health-only with inventory routes denied, and the local-pilot profile also denies inventory routes. Shared product CI uses disposable loopback PostgreSQL fixtures. Architecture tests constrain the exact project and assembly graph and verify that `dotnet sln list` sees every solution project.

## T04 shared composition boundary — 2026-10-22

The shared root solution/API graph and restored lock files now include IAM definition Contracts and TEN persistence for accepted ADR-0012. ArchitectureTests explicitly checks their allowed XML/restored/PE dependencies and Contracts isolation while retaining compiled forbidden-edge fixtures. No inventory source, positive route, provider registration or protected response behavior changed. TEN requires a future trusted PRJ/INV scope-owner adapter; there is no permissive substitute. Existing denied inventory HTTP tests remain required.

## T04 shared PostgreSQL fixture scheduling — 2026-10-22

The canonical Product CI and existing amr.py deterministic gate now pass MSBuild -m:1 to the solution test command, so separate Product test projects run sequentially. AUD saturation tests deliberately acquire ACCESS EXCLUSIVE on the shared isolated aud_intents table; running new TEN fixture provisioning concurrently produced legitimate bounded unavailable results. Project serialization preserves every assertion, timeout, failure fixture and test count, and changes no product runtime or AMR routing/review/checkpoint policy. No Website process, service or test is involved.

## T05 active implementation plan — 2026-10-22

Owner INV, actor current trusted User, exact tenant scope. Project mutations require project.manage at catalog scope; target mutations require target.manage at exact project/environment. Required trusted authority and environment-policy ports have no positive runtime default. Planned source: src/Modules/InfrastructureInventory/Contracts/TargetMetadata.cs, src/Modules/InfrastructureInventory/Infrastructure/InventoryDbContext.cs, src/Modules/InfrastructureInventory/TargetRegistryStore.cs, src/Host/Kordra.Api/CatalogMutationCoordinator.cs. Planned tests: tests/ProjectEnvironment.Tests/TargetAssociationTests.cs. Implement owner-only persistence with approved EF/Npgsql pins, revision/idempotency/lifecycle checks, same-transaction PRJ/INV binding plus AUD, and negative/rollback/unknown-commit tests. No new HTTP route, provider read, real provisioning or runtime migration. Preserve accepted architecture and T04 evidence.

## T05 INV owner implementation — local 2026-10-22

FEAT-INV-001 / MOD-INV adds metadata-only target persistence, its own migration/history, typed internal commands/facts and TargetRegistryStore. Existing IInventoryReader/UnavailableInventoryReader behavior and denied inventory HTTP ingress remain unchanged. The runtime consumes PRJ BCL Contracts, never PRJ runtime or its tables. Host alone composes both runtime owners in a shared PostgreSQL transaction.

Target registration resolves current tenant/project/environment through PRJ, generates a stable target ID, stages the matching PRJ association and INV target/receipt, and commits AUD atomically under the common writer gate. Unknown/missing/conflicting ownership is denied; project-only absence is explicitly resolved, with all environment facts absent. Existing target facts require the exact matching association/revision and current active parent/policy. Scope moves are rejected; disable/delete invalidate facts, and reactivation/change-back increments target revision. Kind is token-shaped metadata only and establishes no driver capability or provider support.

TargetAssociationTests covers project-only and environment targets, exact replay/conflict, no implicit target grant, foreign/missing scope, malformed absence, missing/conflicting association/revision, composite binding FK, lifecycle/stale revisions, revoked policy/authority, setup failure, two-owner rollback and unknown commit both before and after durability. The rollback test uses a GUID-named trigger on only its synthetic inv_targets row in kordra_test to fail INV save after PRJ save; trigger/function are removed in finally, and no runtime database or external service is changed. Audit evidence is retained, not deleted.

ReadCurrentAsync yields owner facts to a trusted caller already holding admission; it is not authorization or HTTP disclosure. T06 must supply current subject/permission/policy adapters and U/D; T07 verifies actual HTTP handoff with a test-only provider. No real Kubernetes/Helm read, credential lookup, provider registration, runtime migration or workspace provisioning occurred.
