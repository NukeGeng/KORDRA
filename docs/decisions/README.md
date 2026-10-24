---
id: ADR-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-24
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

## T09.1 Kubernetes workload proposal — preparation before acceptance, 2026-10-24

[ADR-0016](0016-kubernetes-workload-observation.md) remains proposed: workload-first namespace read, exact provider trust/RBAC, bounded transport and explicit T08 managed-header supplement. The owner's sequencing choice is not ADR acceptance or live-target authorization.


Accepted scope update — 2026-10-24: NukeGeng approved exact ADR-0016 and Kubernetes workloads v1 for T09.1a–d, including the post-C managed-header exception, offline private-PKI residual, exact Namespace identity GET and additive config/scope/freshness caps. This supersedes pending-design approval wording in this section only. Runtime implementation and K09-01–12 verification remain pending. T09.1e, existing secrets, real clusters, Helm and OBS runtime remain excluded; historical checkpoints are preserved without replay.

## Nomad provider accepted design — 2026-10-24

[ADR-0017](0017-nomad-workload-observation.md) accepts T09.2a–d Nomad-first implementation after the owner reported a platform change. It preserves accepted ADR-0016 and records Nomad identity, credential-header and personal-loopback differences. NukeGeng separately approved the exact ADR/contract on 2026-10-24; creating the personal lab alone did not confer Product adoption.
