---
id: ENG-ARCH-TESTS
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Kordra — Architecture & Source Boundary Test Rules

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

## 1. Purpose and test layers
Static documentation checks are not sufficient. Tests must protect **actual assembly dependencies, module ownership, contracts, and runtime isolation** as Kordra evolves.

| Gate | What is checked | Trigger |
|---|---|---|
| Doc integrity | IDs, ownership metadata, local links | every PR |
| Source mapping | changed `src/` paths ↔ owning feature docs | every PR after mappings are populated |
| Project reference graph | forbidden project/namespace edges, cycles | every PR |
| Assembly architecture | Domain isolation, Contracts isolation, module internals | every PR |
| Host composition | correct DI registrations; no duplicate consumers | when host/modules change |
| Contract compatibility | API, message, agent protocol versioning | when contracts change |
| Multi-tenancy/security | wrong-tenant/role grants and service principal scope | when access/tenant changes; critical release |
| Execution safety | idempotency, lease/fence, Unknown outcome, timeout, rollback | deployment/driver changes |

## 2. Current executable baseline
The current product test suite includes `DomainAssemblyDoesNotDependOnOtherModulesOrAdapters` in `tests/ArtifactManagement.Domain.Tests/OCIImageDigestTests.cs`. The `product-ci` workflow runs `dotnet test Kordra.slnx`, so that narrow Domain assembly check executes in product CI. It verifies the current assembly reference set only; it does not establish the proposed cross-module project graph, Contracts isolation, host registration, Agent isolation or multi-tenant runtime behavior. Keep the broader rules below proposed until their projects exist and their owner-approved assertions run in CI.

## 3. Enforceable boundary assertions
- Domain assembly does not reference `Marten`, `Wolverine`, `Microsoft.AspNetCore`, `Kubernetes`, `Helm`, or module `Infrastructure`.
- `<Module>.Contracts` does not reference `<Module>` runtime, persistence SDK or a different module's internals.
- No Module X runtime references Module Y runtime without explicitly accepted exception; prefer `.Contracts`.
- Agent assembly does not reference `Kordra.Modules.*`, Control Plane persistence or API host.
- `BuildingBlocks.*` do not reference application Modules; no cycles.
- API host must not register privileged direct target driver execution for regular requests.

## 4. Example pseudo-assertions (adapt to real assembly names)
```csharp
// Pseudocode — translate into your chosen test library, not executable as-is.
AssertNoReference("Kordra.Modules.*.Domain", "Marten");
AssertNoReference("Kordra.Modules.*.Domain", "Wolverine");
AssertNoReference("Kordra.Agent", "Kordra.Modules.*");
AssertNoReference("Kordra.Modules.*.Contracts", "Kordra.Modules.*.Infrastructure");
AssertNoCrossModuleRuntimeReferenceExceptApproved();
AssertNoProjectReferenceCycles();
```
Do not copy these examples into production tests without implementing real graph checks. Prefer a small deterministic `.csproj` XML/solution dependency test before adopting extra test libraries.

## 5. Required behavioral invariants
- Cross-tenant read/write/list/export/websocket denial, incl. platform-admin vs tenant-admin grant ceilings.
- AuthZ enforced in worker execution path, not just API middleware.
- Workflow side effect replay is idempotent; duplicates don't produce duplicate deploy; ACK is not success.
- Driver failures produce observed outcomes; unsupported rollback does not get marked successful.
- Single-writer target / lease ownership rules and stale-agent fencing.
- Event catalog schemas are backward-compatible or have tested migration/consumer version plan.

## 6. CI baseline (example order)
```text
Doccheck
  -> Source Ownership / Changeguard
  -> dotnet restore + build
  -> Static Dependency Graph / Architecture Tests
  -> Unit Tests
  -> Contract Tests
  -> Integration Tests (isolated services)
  -> Security & Driver Conformance (when relevant)
  -> PR Code Review + Human Approval
```
Critical gate failures must block merge; optional slow tests can run nightly but release governance must record remaining risk. GitHub AI review is supplemental, not a substitute for deterministic gates.

## 7. Handling violations
1. Report exact violating assembly, ref edge, feature, commit and test output.
2. Either fix path/reference or draft ADR exception with expiration/review owner.
3. Do not suppress rule globally to let one task pass.
4. Fix source/docs mapping, rerun dependency + behavior tests.

## 8. Acceptance evidence
Store test command, exit code, Git SHA, affected assembly, CI job, and review outcome; no claim of `verified` without executed checks. New assembly/project must add at least one dependency conformance test.

**See also:** [Dependency rules](../architecture/dependency-rules.md), [Test strategy](testing-strategy.md), [Traceability](traceability.md), [ADR-0009](../decisions/0009-source-architecture.md).
