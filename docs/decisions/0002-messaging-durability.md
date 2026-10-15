---
id: ADR-0002
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0002 — Wolverine + PostgreSQL durable messaging first

**Decision status:** Proposed (not accepted). **Date:** 2026-10-15. **Reviewers:** Architecture owner + Security/Operations when relevant.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
RabbitMQ from day one; in-memory queues only.

## Proposed decision
Use PostgreSQL durable inbox/outbox/scheduled handlers initially; preserve broker-agnostic message contract; add RabbitMQ via measured need.

## Consequences / tradeoffs
Fewer moving parts for prototype. Risk: Postgres contention; benchmark queue depth/latency and add transport before violating SLO.

## Follow-up before acceptance
- Validate via proof of concept or spike; record source/tests/benchmarks.
- Review migration, compatibility and rollback of this decision.
- Update affected modules/features/contracts and mark accepted **only after review**.
