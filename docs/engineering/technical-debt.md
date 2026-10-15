---
id: ENG-DEBT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Technical debt register, triage & prevention

## Policy
A debt entry is a **tracked obligation**; no TODO-only debt as sole record. Every debt has `DEBT-ID`, impacted feature/module, symptom/evidence, type, severity, risk, owner, proposed fix, verification, due/review checkpoint, status. Backlog severity proposals: `critical/high/medium/low`.

## Register
| Debt ID | Module / feature | Symptom & evidence | Severity | Owner | Mitigation | Exit criterion | Status |
|---|---|---|---|---|---|---|---|
| DEBT-000 | `unassigned` | TEMPLATE ONLY — remove when starting project | low | unassigned | n/a | first real debt registered | example |
| DEBT-AMR-001 | ENG-TOOLING / FEAT-AMR-001 | No Git/provider identity or application build exists; automatic provider review/real PR changeguard/production negative tests cannot execute | medium | engineering-maintainers | Local native review, offline routing/guard contracts and explicit unsupported statuses; review before first PR | Trusted Git checkout, identified provider, automatic review on current SHA, real CI and independent owner approval; checkpoint: first PR | open |
| DEBT-AMR-002 | ENG-TOOLING / FEAT-AMR-001 | Native review/start attempts timed out at 180/300 seconds without terminal review evidence | medium | engineering-maintainers | Separate independent read-only CLI review and regression tests; native review stays not verified | Diagnose runtime/protocol and observe native review exit on exact current revision; checkpoint: first connected PR | open |

## Taxonomy
`architecture-drift`, `documentation-drift`, `test-gap`, `security-risk`, `data-model`, `dependency`, `reliability`, `performance`, `operational-gap`, `model-data-lineage`.

## Review process
- New code debt: file in same PR; explicitly link to feature/ADR/incident.
- Critical security/cross-tenant/deploy risk cannot be accepted as silent debt; requires remediation or risk approval with deadline.
- Weekly sprint/refinement: review high severity/aged debt and owner; after incident: update root cause/exit test.
- Close only with diff/PR/test result; retained record shows why closed.

## Debt budget (proposal)
No fixed percent until team capacity known. Use trend metrics: debt created vs closed, age by severity, orphan owner count, untested critical feature count, docs drift count. Escalate trend, do not game a numeric score.
