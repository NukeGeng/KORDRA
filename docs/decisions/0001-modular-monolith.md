---
id: ADR-0001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0001 — Modular Monolith with separate API/Worker/Agent process

**Decision status:** Proposed (not accepted). **Date:** 2026-10-15. **Reviewers:** Architecture owner + Security/Operations when relevant.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
Single process monolith; microservices per domain.

## Proposed decision
Choose logically modular .NET core; deploy API/worker/agent separately.

## Consequences / tradeoffs
Lower operational complexity while preserving independent execution. Risk: weak boundaries without tests. Revisit when organization scale demands independent deployment.

## Follow-up before acceptance
- Validate via proof of concept or spike; record source/tests/benchmarks.
- Review migration, compatibility and rollback of this decision.
- Update affected modules/features/contracts and mark accepted **only after review**.
