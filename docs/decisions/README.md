---
id: ADR-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-22
---

# Architecture Decision Records (ADRs)

ADR tracks **why** a design was chosen; status initially `proposed`, so not a finalized baseline. Each file owns its status; ADR-0003 is accepted only for its narrow policy baseline, while ADR-0001/0002/0004/0005 remain proposals with alternatives. New decision → copy [`templates/adr.md`](../templates/adr.md); don't overwrite history; use `supersedes` when replacing prior choice.

| ADR | Decision candidate |
|---|---|
| [0001](0001-modular-monolith.md) | Modular Monolith split API/Worker/Agent processes |
| [0002](0002-messaging-durability.md) | Wolverine + PostgreSQL durability first |
| [0003](0003-tenant-authorization.md) | Accepted narrow permission/trusted-scope baseline; no IAM/login/storage runtime adoption |
| [0004](0004-single-writer-reconciliation.md) | Single desired-state writer/controller owner |
| [0005](0005-immutable-release.md) | Approval pinned to artifact/spec digest |
| [0006](0006-driver-capabilities.md) | Adapter capability model |

| [0007](0007-reference-testbed-not-product.md) | Reference environment is a bounded testbed, not product architecture |
| [0008](0008-domain-bootstrap-validation.md) | Accepted scoped .NET/test bootstrap; other architecture and merge approvals remain separate |
| [0009](0009-source-architecture.md) | Proposed Kordra source/solution layout; requires architecture and Agent-boundary review |

| [0010](0010-local-readonly-bootstrap.md) | Accepted scoped Mac-local host/inventory bootstrap; synthetic tests only, denied ingress until identity/provider decisions |
| [0011](0011-local-postgresql-roles.md) | Accepted local-only PostgreSQL database/role layout; does not adopt product persistence or messaging technology |
| [0012](0012-operator-pilot-runtime-foundation.md) | Accepted exact Foundation T01–T07 scope: Identity, EF/Npgsql, owner-specific identity/authority/audit persistence, local sessions and single-process protected admission; no provider, distributed worker or production access |

| [0013](0013-clients-git-submodule.md) | Accepted narrow independent Clients repository/submodule at the original root path; no UI stack or broader ADR-0009 acceptance |
| [0014](0014-resource-policy-v1.md) | Accepted exact T06 resource-policy supplement on 2026-10-22: IAM immutable policy/head, exact PRJ binding, fixed target.read eligibility and U/D compatibility; implementation evidence pending |

| [0015](0015-credential-boundary-preparation.md) | Accepted exact T08 credential boundary, offline SEC metadata and bounded native macOS Keychain child; synthetic fixture separately authorized, implementation pending |
