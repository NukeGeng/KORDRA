---
id: ADR-0003
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0003 — Resource-scope permission model independent of role names

**Decision status:** Proposed (not accepted). **Date:** 2026-10-15. **Reviewers:** Architecture owner + Security/Operations when relevant.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
Hard-coded Admin/SuperAdmin role checks; broad global admin.

## Proposed decision
Authorize using principal + permission + resource scope + context; platform super-admin not implicit tenant data access.

## Consequences / tradeoffs
Extensible roles and explicit cross-tenant support. Cost: more comprehensive policy tests, migration and UI admin flows.

## Follow-up before acceptance
- Validate via proof of concept or spike; record source/tests/benchmarks.
- Review migration, compatibility and rollback of this decision.
- Update affected modules/features/contracts and mark accepted **only after review**.
