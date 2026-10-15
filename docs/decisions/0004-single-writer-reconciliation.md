---
id: ADR-0004
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0004 — Single-writer ownership for desired state

**Decision status:** Proposed (not accepted). **Date:** 2026-10-15. **Reviewers:** Architecture owner + Security/Operations when relevant.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
Allow both direct kubectl and Argo CD to auto-reconcile same app.

## Proposed decision
Each target declares managed-direct/external-gitops/external-ci/observe-only; only owner mutates desired state.

## Consequences / tradeoffs
Avoid controller fights; GitOps targets require PR/repo integration rather than direct override.

## Follow-up before acceptance
- Validate via proof of concept or spike; record source/tests/benchmarks.
- Review migration, compatibility and rollback of this decision.
- Update affected modules/features/contracts and mark accepted **only after review**.
