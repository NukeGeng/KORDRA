---
id: ARCH-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Architecture map

Kiến trúc **đề xuất**, chưa được mặc định là baseline. Thứ tự đọc:

1. [System context](system-context.md) → [Container topology](container-topology.md) → [Bounded contexts](bounded-contexts.md).
2. [Security & tenancy](security-multitenancy.md) → [Messaging](messaging.md) → [Workflow](workflow.md).
3. [Execution/Agents](execution-plane.md) → [State reconciliation](reconciliation.md) → [Data](data-architecture.md).
4. [Observability](observability.md), [Reliability](reliability.md), [Supply chain](artifact-supply-chain.md), [Deployment topology](deployment-topology.md).

## Proposed source and solution architecture
The v1.2 source-architecture set is linked here for review; every document below remains `proposed` until ADR-0009 is explicitly accepted. It describes a target, not the current source tree.

- [Repository structure](repository-structure.md) and [.NET solution](solution-architecture.md).
- [Module internals](module-internal-structure.md) and [dependency rules](dependency-rules.md).
- [Host composition](host-composition.md) and [extension architecture](extension-architecture.md).
- [ADR-0009 — source architecture proposal](../decisions/0009-source-architecture.md).
- [Integration audit and adoption steps](../engineering/source-architecture-adoption.md).

**Invariant xuyên suốt:** Control Plane là SoT cho intent/approval; target là SoT cho observed deployment; một target chỉ có một writer/controller được giao ownership; không tin message delivery là chứng minh side effect đã hoàn tất.

## Reference implementation / validation
- [LAB-01 reported infrastructure](../reference-environments/lab-01/README.md) is a testbed, **not** an accepted production architecture baseline for the product.
