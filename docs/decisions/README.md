---
id: ADR-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Architecture Decision Records (ADRs)

ADR tracks **why** a design was chosen; status initially `proposed`, so not a finalized baseline. `0001`–`0005` are decision proposals with alternatives. New decision → copy [`templates/adr.md`](../templates/adr.md); don't overwrite history; use `supersedes` when replacing prior choice.

| ADR | Decision candidate |
|---|---|
| [0001](0001-modular-monolith.md) | Modular Monolith split API/Worker/Agent processes |
| [0002](0002-messaging-durability.md) | Wolverine + PostgreSQL durability first |
| [0003](0003-tenant-authorization.md) | Resource-scope authorization, no role-name hardcode |
| [0004](0004-single-writer-reconciliation.md) | Single desired-state writer/controller owner |
| [0005](0005-immutable-release.md) | Approval pinned to artifact/spec digest |
| [0006](0006-driver-capabilities.md) | Adapter capability model |

| [0007](0007-reference-testbed-not-product.md) | Reference environment is a bounded testbed, not product architecture |
| [0008](0008-domain-bootstrap-validation.md) | Accepted scoped .NET/test bootstrap; other architecture and merge approvals remain separate |
