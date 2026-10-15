---
id: ENG-AI-MODULE
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
module_id: ENG-TOOLING
---

# Developer engineering tooling

Owns local AI routing, context snapshots, review receipts and deterministic repository checks. This is an engineering ownership unit, outside the product bounded contexts; it adds no backend module, tenant store, permission or messaging channel.

Actors are repository contributors and reviewers. Local filesystem access is governed by the coding runtime; production authorization remains with the existing approval process. Native review findings are advisory.

Feature registry:

- [FEAT-AMR-001 — Quality-first AMR](features/quality-first-amr.md)

Existing sources: `docs/tooling/`, the [AI review protocol](../../governance/ai-review-protocol.md), root AGENTS and WORKFLOW. Extend these rather than building another AI platform.
