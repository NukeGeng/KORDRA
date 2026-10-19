---
id: ENG-AI-BOUNDARY
status: proposed
owner: unassigned
last_reviewed: 2026-10-19
---

# AI tooling is optional development infrastructure

Kordra's `docs/tooling/amr.py` and GOAL tooling support the developer workflow; they are not Control Plane modules or required runtime services. The current project policy is [AMR v4 — Architect / Developer / Reviewer](ai-orchestration/README.md): Luna Max owns approved implementation and GOAL coordination, Sol High provides bounded design when needed, and Codex Auto PR Review is the default independent PR reviewer.

## Rules
- Product build/test/docs/security gates work from terminal/CI without proprietary model credentials or live Codex sessions.
- Native model switching and provider Code Review are optional integrations. Their inability to run must be recorded, not falsely marked successful.
- Optional provider integration does not waive applicable gates: critical changes retain independent PR review and blocking-finding gates under `AGENTS.md`. GOAL validation, acceptance and milestone transitions run deterministic checks and contributor triage without invoking an AI reviewer. Missing required PR review blocks merge; sensitive production approval remains explicitly human.
- Do not spawn collaboration sub-agents for any Kordra task. Keep the active coordinator workflow single-agent; separate advisory Code Review is a review channel. AI assistants cannot grant deployment or production authorization.
- Keep checkout-scoped checkpoints private (`.amr-local/`), and verify Git/source before resume.
- Review `AGENTS.md`/`WORKFLOW.md` before any AI change; don't create a second orchestration runtime or move AMR without approved migration.

## Quality priority
Correctness, architecture/security invariants, tests and repeatability before token optimization. AI review is advisory; maintain human decisions for architecture acceptance and sensitive operations.
