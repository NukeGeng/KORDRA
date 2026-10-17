---
id: ENG-DEBT
status: proposed
owner: unassigned
last_reviewed: 2026-10-17
---

# Technical debt register, triage & prevention

## Policy
A debt entry is a **tracked obligation**; no TODO-only debt as sole record. Every debt has `DEBT-ID`, impacted feature/module, symptom/evidence, type, severity, risk, owner, proposed fix, verification, due/review checkpoint, status. Backlog severity proposals: `critical/high/medium/low`.

## Register
| Debt ID | Module / feature | Symptom & evidence | Severity | Owner | Mitigation | Exit criterion | Status |
|---|---|---|---|---|---|---|---|
| DEBT-000 | `unassigned` | TEMPLATE ONLY — remove when starting project | low | unassigned | n/a | first real debt registered | example |
| DEBT-AMR-001 | ENG-TOOLING / FEAT-AMR-001 | GitHub/NukeGeng identity, protected `main`, required checks and first module build/tests are verified; current PR still has no review on its latest published head; provider automatic review and independent owner acceptance remain unverified | medium | engineering-maintainers | Keep local/native review and provider review statuses separate; retain required human merge/production approvals | Provider or native review on exact current PR SHA, relevant CI and independent owner approval; checkpoint: first product PR | open; Git setup, main protection and local product validation completed |
| DEBT-AMR-002 | ENG-TOOLING / FEAT-AMR-001 | Native review/start previously completed only on a stale source snapshot; later native review attempts, including the current Goal E2E, timed out without matching terminal review evidence | medium | engineering-maintainers | Preserve timeout receipts and independent findings; stop same-cause retries until runtime/protocol or provider path changes | Observe verified native/provider review exit on an exact stable current revision; checkpoint: first connected PR | open |
| DEBT-AMR-003 | ENG-TOOLING / FEAT-AMR-001 | Local AMR cannot inspect the active desktop coordinator model, control native GOAL status, or bind direct coordinator edits to a terminal execution receipt; manual selection stays unverified and direct edits cannot complete a checkpoint | medium | engineering-maintainers | Keep runtime claims unverified and use the serial native `goal step` runner for task-bound execution; validate checkpoint source/Git/task identity and use native desktop Goal controls for pause/continue | Session-bound model observation, coordinator execution receipts and native Goal controls are verified end-to-end; otherwise retain the explicit limits | open |

## Taxonomy
`architecture-drift`, `documentation-drift`, `test-gap`, `security-risk`, `data-model`, `dependency`, `reliability`, `performance`, `operational-gap`, `model-data-lineage`.

## Review process
- New code debt: file in same PR; explicitly link to feature/ADR/incident.
- Critical security/cross-tenant/deploy risk cannot be accepted as silent debt; requires remediation or risk approval with deadline.
- Weekly sprint/refinement: review high severity/aged debt and owner; after incident: update root cause/exit test.
- Close only with diff/PR/test result; retained record shows why closed.

## Debt budget (proposal)
No fixed percent until team capacity known. Use trend metrics: debt created vs closed, age by severity, orphan owner count, untested critical feature count, docs drift count. Escalate trend, do not game a numeric score.
