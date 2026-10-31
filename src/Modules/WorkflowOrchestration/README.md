---
id: LAYOUT-SRC-MODULES-WORKFLOWORCHESTRATION
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-18
---

# src/Modules/WorkflowOrchestration

T12.2a now adds BCL-only Domain and Contracts libraries plus pure WorkflowOrchestration.Tests under accepted ADR-0021. Scoped identity, structural state/version guards and canonical checkpoint specification have local source/tests. Persistence, transaction integration, HTTP/worker activation and independent-host qualification remain pending; this is not a deployed workflow engine.

Product ownership: [canonical module specification](../../../docs/features/workflow-orchestration/module.md).

See [current folder framework](../../../docs/engineering/repository-folder-framework.md) for implemented paths and exceptions. The [source architecture ADR-0009](../../../docs/decisions/0009-source-architecture.md) remains proposed; this reservation does not accept its technology, trust boundaries or integrations. Add source only for an approved, mapped feature with applicable tests and review.
