---
id: ADR-0009
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# ADR-0009 — Native Kordra Source Architecture, FSH-Inspired Structure

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

**Decision status:** Proposed (not accepted). **Date:** 2026-10-16. **Decision owner:** unassigned. **Approvers required:** architecture owner; security reviewer for Agent boundaries; project maintainer.

## Decision identifier and existing scope
The supplied v1.2 bundle named this proposal ADR-0008. This repository already uses ADR-0008 for the explicitly accepted, narrowly scoped .NET 10/domain-test bootstrap. To preserve immutable decision history and avoid duplicate IDs, this source-architecture proposal is filed as **ADR-0009**. It does not amend, expand or supersede the accepted ADR-0008. Importing this proposal does not accept it.

## Context
Kordra là open-source DevOps Control Plane với API, durable worker, agent/driver, Vue + Tauri, OSS-first / potentially enterprise later. V1.1 already proposes Modular Monolith and runtime separation, but did not define an authoritative solution or module folder shape. Without a baseline, autonomous coding agents can introduce drift and untraceable feature ownership.

## Decision drivers
- Correctness of tenant/permission/release invariants.
- Reuse .NET 10/Marten/Wolverine without reworking a starter kit's infrastructure dependencies.
- Maintainable boundaries and source→feature→test traceability.
- Deploy worker/agent separately and enable drivers without copying product logic.
- Avoid premature microservices and speculative commercial infrastructure.

## Options considered
| Option | Benefits | Risks / tradeoffs |
|---|---|---|
| A. Fork FullStackHero wholesale | fast standard host/bootstrap | replacing EF Core / mediator / job stack; inherited unused modules; hard to own architecture |
| B. Build fully custom flat monolith | very flexible initial structure | hidden ownership and dependency drift, maintenance cost |
| **C. Kordra native solution inspired by FSH organization** | explicit module/host/contract/driver boundaries, preserves chosen stack | requires maintaining lean conventions/tests |

## Proposed decision
If accepted after the evidence below, adopt **Option C** for the following bounded source-architecture scope:
1. `src/BuildingBlocks`, `src/Modules`, `src/Execution`, `src/Host`, `src/Tests` with `src/Kordra.slnx`.
2. DDD + feature slices where needed, public `.Contracts` distinct from internal module; host composition only at executables.
3. Marten/PostgreSQL for owned persistence/events; Wolverine for message and workflow processing as justified; no default EF Core or generic repository copy.
4. Control Plane API and Worker independent execution responsibilities; Agent independent trust/domain boundary.
5. `clients/`, `docs/`, `deploy/`, `samples/`, `tools/`; optional extension contract when use case requires.
6. `docs/architecture/repository-structure.md`, `solution-architecture.md`, `module-internal-structure.md`, `dependency-rules.md`, `host-composition.md` specify technical constraints when accepted.

This proposal would establish the target layout, project/dependency boundaries, candidate persistence/message technologies and host responsibilities listed above. It does not accept product permission rules, tenant/platform scope, deployment state machines, customer-broker inventory ownership, OSS licensing or production operations. The current implementation remains the two-project bootstrap under the root solution until a separately reviewed migration moves it; any such move must update README, CI, AMR gate discovery, changeguard, feature mappings and tests in one increment.

## Relationship to existing ADRs
- Complements [ADR-0001](0001-modular-monolith.md); does **not** silently supersede it.
- Must remain consistent with ADR-0002 durable delivery, ADR-0003 resource authorization, ADR-0004 reconciliation ownership, ADR-0006 driver capabilities, ADR-0007 testbed isolation.

## Consequences
**Positive:** clear assembly/feature ownership; independent host deployments; less starter-kit baggage; extensibility for OSS+commercial packages. **Negative:** initial effort establishing conventions and architecture checks; risk of too many project abstractions if unrestrained.

## Reversibility & migration
1. Audit current repository: project refs, test coverage, host registrations, contracts and documentation.
2. Prepare mapping old→target paths and preserve Git history; move one vertical slice at a time.
3. Avoid public namespace/contract breaking changes without compatibility plan.
4. Architecture, contract and integration tests must pass before each increment is complete.
5. No big-bang reorganization of working code solely to match scaffold.

## Evidence required before acceptance
- Record concrete current repo Git SHA; target package list; minimal host PoC; Module→Contract and Agent isolation tests; affected features and change plan.
- Document architecture/security approval (date, approver); then switch `status` and `Decision status` to `accepted` in an explicit PR.

## Open decisions
- Exact package/project granularity per module, including whether the current standalone Domain assembly remains separate from module Features/Infrastructure; process packaging by deployment tier; extensibility runtime strategy; package signing; OSS license (separate legal decision).
- `[NEEDS-DECISION]` Ownership for customer external-broker inventory/topology metadata. MOD-MSG owns internal Kordra delivery only; do not derive product ownership from LAB-01.

**Detailed specs:** [Repository](../architecture/repository-structure.md), [Solution](../architecture/solution-architecture.md), [Module](../architecture/module-internal-structure.md), [Dependency](../architecture/dependency-rules.md), [Hosts](../architecture/host-composition.md), [Extensions](../architecture/extension-architecture.md).
