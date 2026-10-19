---
id: ADR-0010
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-18
---

# ADR-0010 — Scoped local OP-P0 host and inventory bootstrap

**Decision status: Accepted for this exact scoped bootstrap (2026-10-18).** The repository maintainer explicitly approved: “Chấp thuận đúng phạm vi ADR-0010; tiếp tục triển khai bootstrap.” This follows independent native Sol 6.1 High advisory review and passing gates on the reviewed proposal. Approval covers only the boundary below; it is not product completion, merge permission, real credential/cluster access or acceptance of ADR-0003/0009.

## Context and scope

At HEAD `2aa156f4e6e3afe3cc648186ac6a3242781dc71b` plus local documentation/tooling changes, the product has only the ArtifactManagement Domain library and its tests. ADR-0008 accepts that narrow bootstrap; ADR-0009's broader source/technology proposal remains unaccepted. The three proposed OP-P0 feature seams are prepared, but no API, inventory adapter, IAM, secret provider or UI exists.

The disposable [OPP0-ARCH-SPIKE-001](../features/infrastructure-inventory/features/register-target.md#proposed-feasibility-evidence--opp0-arch-spike-001) has a retained native-sandbox restore failure and a separately reconciled local pass: Release build, real assembly-reference inspection, two compiled forbidden-reference cases, valid graph restoration and 14 loopback HTTP assertions. Its total-denial fixture is not an authorization implementation or product CI gate. Source/evidence digests and limitations are recorded there; raw artifacts remain local. This ADR proposes the next bounded product increment, not acceptance of that temporary source as production code.

## Alternatives and trade-offs

| Option | Benefit | Cost / limitation |
|---|---|---|
| Wait for full ADR-0009, IAM, persistence and provider decisions | Broader baseline settled first | Blocks actual host/assembly/CI feedback on the local pilot |
| Adopt all proposed hosts, Marten/Wolverine and real cluster reads now | Faster apparent breadth | Unreviewed technology/trust/data choices; not authorized by the current evidence |
| **Accepted scope: bounded local host and test-only synthetic inventory seam** | Real product graph/build/CI evidence without real credentials or infrastructure | Delivers bootstrap only; another decision is needed before useful protected cluster reads |

## Accepted decision — exact adoption boundary

1. Retain root `Kordra.slnx`, `global.json`, existing `src/Modules/ArtifactManagement/Domain`, root `tests/` and `docs/tooling/`. No source relocation or adoption of ADR-0009's entire target tree.
2. Add only the following projects when implementation is authorized and needed by this increment. These are **future paths**, not existing source mappings:

| Proposed path | Owner / responsibility | Allowed product references |
|---|---|---|
| `src/Modules/InfrastructureInventory/Contracts/Kordra.Modules.InfrastructureInventory.Contracts.csproj` | MOD-INV / FEAT-INV-001; minimal in-process read seam | BCL only; no other module, provider or persistence types |
| `src/Modules/InfrastructureInventory/Kordra.Modules.InfrastructureInventory.csproj` | MOD-INV / FEAT-INV-001; inventory read boundary with no real provider | Own Contracts only; exclude the nested Contracts source from runtime compilation |
| `src/Host/Kordra.Api/Kordra.Api.csproj` | FEAT-INV-001 bootstrap composition only | Inventory runtime/Contracts; no Artifact Domain, worker, privileged adapter or storage |
| `tests/InfrastructureInventory.Bootstrap.Tests/InfrastructureInventory.Bootstrap.Tests.csproj` | FEAT-INV-001 bootstrap unit/host/architecture assertions | The above projects as needed for tests; existing Artifact tests unchanged |

3. Use installed/pinned SDK 10.0.400 and `net10.0`; nullable enabled and warnings treated as errors. The only additional runtime surface is **Microsoft.NET.Sdk.Web / Microsoft.AspNetCore.App shared framework**, observed locally at 10.0.11. Add no runtime PackageReference. Test-only packages are the existing pinned Microsoft.NET.Test.Sdk 18.10.1, xUnit 2.9.3 and xunit.runner.visualstudio 3.1.5; commit lock files and restore locked. No mocking/architecture-test/provider package is selected. A clean CI runner must verify restore/build compatibility; installed local framework availability alone does not prove CI support.
4. Bind explicitly to IPv4 loopback on the Mac (ephemeral port in host tests). Do not accept an override exposing other interfaces. Expose only constant process liveness; inventory requests remain uniformly denied until separate trusted identity/authorization/provider decisions are accepted. Liveness says nothing about a cluster. Do not add an inventory endpoint that exposes synthetic or real tenant data to anonymous callers.
5. Any positive inventory fixture belongs in tests only, through the owning in-process seam. Production composition must not register a permissive fake authorizer/provider or enable one by environment/config. Missing identity/authority/provider stays denied/unavailable; a request tenant ID, profile label, platform role or loopback address grants no read permission. Test fixtures cannot establish real Kordra tenant ownership.
6. Read no kubeconfig, Keychain, credential environment variables, secret provider, Helm storage, cluster API or external endpoint. No shell executor, deployment/write operation, mutation approval, persistence, message/workflow state machine, Worker/Agent, Vue/Tauri, Marten/Wolverine/Aspire or new shared kernel. Those choices require separate accepted decisions. Do not copy temporary fixture source wholesale or add empty speculative projects.

## Verification required before calling this bootstrap complete

- Feature source/test mappings enumerate real paths only after code exists. Update feature evidence, roadmap, contracts if actually introduced, traceability/change history and CI in the same increment.
- Deterministic project and actual built-assembly checks enforce the approved graph, including Contracts isolation and no cross-module internals. Negative fixtures prove forbidden edges are rejected; tests must not merely mirror an allowlist without inspecting source/binaries. No claim of real Agent isolation while no Agent exists.
- Host tests exercise liveness, forged/missing tenant or platform scope with denied inventory, unsupported/mutation methods, absence of production fake registration, loopback-only binding and listener cleanup. These are bootstrap constraints, not the full IAM/security acceptance suite.
- Locked restore, Release build/test, existing 48 Artifact tests, tooling tests, strict doccheck, source mapping and AMR gates with verified Git base must pass. Existing product CI must run the real added projects/tests; no green local-only synthetic check substitutes for CI.
- Independent critical review, finding reproduction/triage and human merge authority remain required. An approval of this ADR alone is neither code-review approval nor merge permission.

## Rollout, reversal and remaining decisions

Implementation follows one dependency-ordered FEAT-INV-001 increment; add no project before its concrete source/test use. Reversal removes only the newly added projects/references/mappings and keeps the existing Artifact domain/CI intact. There is no data migration or infrastructure rollback because this scope stores no product data and contacts no target.

Before protected or real reads: accept trusted principal/tenant/target binding and permission semantics with IAM/security owners; choose credential custody/provider and bootstrap/TLS trust; agree exact Kubernetes/Helm resources, fields, least-privilege RBAC, redaction and audit. Helm metadata does not grant access to all Secrets, values or manifests. Company rights-owner approval and reference-environment controls remain separate. Later long-running workflows require independent durable execution; Mac-local read availability proves none of that.

## Acceptance record

Explicit human approval was received on 2026-10-18 in this thread, for the exact reviewed proposal SHA-256 `d1862d77b3195473fd6e623301482309dc9c8f44e4c2d1bb5b41748d5686abc2` (stable checkout content `ddbb9ec70d1d89c81df1f3aad5d3c9aa5e1ef429f032366cd507b132f6e6dab0`). Native advisory review evidence: `native:01a14d64-0496-74c1-af8e-6bb2672416c6:01a14d64-07e9-7220-a877-11561155d7f5`. The human instruction, rather than model review or green checks, authorizes this scoped adoption. Implementation/build/test/CI evidence must still be recorded separately. Broader company, credential, IAM/provider and production decisions remain unapproved. This ADR neither supersedes ADR-0008 nor accepts/supersedes ADR-0003 or ADR-0009. The full GOAL and OP-P0 remain unfinished after this bootstrap.

Related: [Pilot](../product/operator-pilot-plan.md), [INV](../features/infrastructure-inventory/features/register-target.md), [PRJ](../features/project-environment/features/environment-catalog.md), [SEC](../features/connection-secrets/features/secret-binding.md), [adoption assessment](../engineering/source-architecture-adoption.md).

## Implementation evidence update — 2026-10-18

The context and future-path wording above describe the reviewed pre-implementation proposal and remain historical decision context. The four scoped projects now exist in the local working tree with exact mappings in [FEAT-INV-001/I1](../features/infrastructure-inventory/features/register-target.md). Local locked restore/Release build, 67 product tests, 114 tooling tests, actual Program smoke, stable independent native High review and contributor triage passed. No broader decision changed. Clean GitHub CI on this unpublished increment is still pending: local task acceptance is not full verification of this ADR's CI criterion or authorization to merge/publish.
