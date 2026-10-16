---
id: ARCH-DEPENDENCIES
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Kordra — Dependency Direction & Boundary Policy

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

## 1. Core invariant
**Dependencies point inward to owned domain/contracts, never outward from Domain to infrastructure.** Một thành phần phụ thuộc vào hệ thống khác phải đi qua port/contract đã định nghĩa, có owner và compatibility policy.

## 2. Explicit allow/deny matrix
| From | Allowed dependency | Forbidden dependency |
|---|---|---|
| `Modules/X.Domain` | BCL, local domain types, stable Core primitives | Marten, Wolverine, SDK, HTTP, any other module implementation |
| `Modules/X.Features` | X Domain, X Contracts, approved public contracts/ports | Y Infrastructure, Y aggregate, Y persistence store |
| `Modules/X.Infrastructure` | X Domain, X Features ports, providers | Y Infrastructure, bypass auth/approval |
| `Modules/X.Contracts` | BCL + small shared contract primitives | X Domain/runtime, provider SDK, mutable storage models |
| `BuildingBlocks.*` | BCL, focused technical dependencies | `Modules.*`, UI, business tenant/approval logic |
| `Execution.Drivers.Helm` | Driver abstractions, Helm SDK/CLI adapter | API host, tenant policy internals, other driver implementations |
| `Execution.Protocols` | wire-safe contracts | database/session, business modules, provider SDK |
| `Kordra.Agent` | Protocols, agent runtime, selected drivers | identity or release business runtime, Control Plane DB |
| `Kordra.Api` | module registrations/contracts, middleware | direct long-running deployment execution |
| `Kordra.ControlWorker` | workflow modules, message transport, gateway client | UI implementations, secrets in logs |
| `clients/*` | public API/SDK client | database, direct kube operations on behalf of authenticated user |

## 3. Reference examples
```text
GOOD:
  Deployment.Features -> Deployment.Domain -> BuildingBlocks.Core
  Deployment.Infrastructure -> Deployment.Domain
  Agent -> Execution.Protocols + Execution.Drivers.Helm

BAD:
  Deployment.Domain -> Wolverine / Helm SDK
  TenantManagement -> DeploymentManagement.Infrastructure
  Kordra.Agent -> Marten tenant database
  Web UI -> prod cluster-admin kubeconfig
```

## 4. Module dependency edges
- `docs/architecture/bounded-contexts.md` defines conceptual ownership; adding code-level project references requires an explicit consumer/provider contract and test.
- Synchronous contracts: typed public application services/queries with explicit scope; failures/timeouts accounted.
- Asynchronous contracts: versioned event/command via [message catalog](../contracts/message-catalog.md), idempotency and delivery semantics documented.
- Per-module persistence owned; dashboard composite read model is **not** an authorization source of truth.
- Avoid generic repositories/services that hide ownership or validation; exception must be ADR-backed.

## 5. Cross-cutting exceptions
- Logging, clock, tracing, correlation, typed result may live in `BuildingBlocks` only if multiple modules genuinely use them.
- Permission evaluation and tenant guard interfaces may be shared, but role names and business policy mappings belong in their owning modules.
- Only a **named host composition root** may reference many module runtime assemblies to register dependencies; this does not authorize cross-module references inside domains.
- `DbMigrator` references registered migrations; it is privileged tooling, not an endpoint exposed to normal users.

## 6. Automated enforcement
- Assembly/project references: no forbidden namespaces/project dependencies; contract assembly has no runtime module references.
- Reflection-based smoke tests for public API/schema changes; compile-time boundaries plus runtime integration tests.
- Host-specific tests: Agent without core DB or cloud billing; Worker without UI; API without direct privileged driver dispatch.
- Branch/PR checks enforce changed source mapped to feature docs; new dependency declared in PR, reviewer validates tradeoffs.
- Test patterns and failure messages: [Architecture test rules](../engineering/architecture-test-rules.md).

## 7. Changes requiring ADR
Moving a public contract to another owner, adding shared business service, cross-module direct store access, introducing microservice or separate database, changing API/Worker/Agent trust model. **Do not mark exception accepted merely because build passes.**

**See also:** [ADR-0009](../decisions/0009-source-architecture.md), [Module boundaries](../engineering/module-boundaries.md), [Contracts](../contracts/README.md).
