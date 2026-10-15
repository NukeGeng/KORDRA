---
id: ADR-0005
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0005 — Freeze approved artifact and deployment specification

**Decision status:** Proposed (not accepted). **Date:** 2026-10-15. **Reviewers:** Architecture owner + Security/Operations when relevant.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
Approve mutable tag and re-resolve latest during dispatch.

## Proposed decision
Pin OCI digest and deploymentSpecHash with approval/policy version; material change invalidates approval.

## Consequences / tradeoffs
Prevents TOCTOU/release substitution; requires artifact catalog and careful spec canonicalization.

## Follow-up before acceptance
- Validate via proof of concept or spike; record source/tests/benchmarks.
- Review migration, compatibility and rollback of this decision.
- Update affected modules/features/contracts and mark accepted **only after review**.
