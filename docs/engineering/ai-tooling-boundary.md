---
id: ENG-AI-BOUNDARY
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# AI tooling is optional development infrastructure

Kordra's `docs/tooling/amr.py` and GOAL tooling support the developer workflow; they are not Control Plane modules or required runtime services. Treat AMR v3.1 single-agent plus optional manual model-switch recommendations as the current project policy.

## Rules
- Product build/test/docs/security gates work from terminal/CI without proprietary model credentials or live Codex sessions.
- Native model switching and provider Code Review are optional integrations. Their inability to run must be recorded, not falsely marked successful.
- Optional provider integration does not waive applicable gates: CRITICAL work still requires independent review under `AGENTS.md`; an authorized independent human review can satisfy that channel when native review is unavailable. Missing required review blocks completion.
- Do not spawn collaboration sub-agents for any Kordra task. Keep the active coordinator workflow single-agent; separate advisory Code Review is a review channel. AI assistants cannot grant deployment or production authorization.
- Keep checkout-scoped checkpoints private (`.amr-local/`), and verify Git/source before resume.
- Review `AGENTS.md`/`WORKFLOW.md` before any AI change; don't create a second orchestration runtime or move AMR without approved migration.

## Quality priority
Correctness, architecture/security invariants, tests and repeatability before token optimization. AI review is advisory; maintain human decisions for architecture acceptance and sensitive operations.
