---
id: ADR-0006
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0006 — Capability-driven deployment adapters

**Decision status:** Proposed (not accepted). **Date:** 2026-10-15. **Reviewers:** Architecture owner + Security/Operations when relevant.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
Single Deploy/Rollback service with universal semantic assumptions.

## Proposed decision
Require Validate/Plan/Execute/Observe and optional Rollback; expose capabilities and conformance suite.

## Consequences / tradeoffs
Add technologies without modifying domain core. Cost: more explicit error/capability contracts.

## Follow-up before acceptance
- Validate via proof of concept or spike; record source/tests/benchmarks.
- Review migration, compatibility and rollback of this decision.
- Update affected modules/features/contracts and mark accepted **only after review**.
