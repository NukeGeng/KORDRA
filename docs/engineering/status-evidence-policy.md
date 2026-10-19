---
id: ENG-STATUS-EVIDENCE
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# GitHub evidence reconciliation policy

## Why
Repository milestones and debt entries can become stale after PR merge or CI changes. Technical records must distinguish remote facts from plans, not duplicate an automatically changing GitHub status without verification.

## Authoritative sources
- GitHub PR API: merge state, exact head/merge SHA and review submissions.
- GitHub Actions: CI result for **the exact commit**.
- Git/source/tests: implemented code and verified behavior; module status is not inferred from green docs checks.
- Accepted ADR/contract: approved architecture, not `proposed` prose.

## Current verified checkpoint
Rechecked on **2026-10-18**: [PR #1](https://github.com/NukeGeng/KORDRA/pull/1) is merged at `2aa156f4e6e3afe3cc648186ac6a3242781dc71b`, which matches fetched `origin/main`. [Documentation CI](https://github.com/NukeGeng/KORDRA/actions/runs/39000192554) and [product CI](https://github.com/NukeGeng/KORDRA/actions/runs/39000192528) both succeeded on that exact merge SHA. GitHub shows a COMMENTED review on old head `c95b0db25dcbf5ab376037855508864a39b3de85`, not an independent APPROVED review on the merged head. Automatic Codex provider review remains unverified without provider identity and exact-revision terminal evidence. These facts are a dated checkpoint, not a live status feed.

## Solo-owner repository merge policy (verified checkpoint)

The owner explicitly requested control of `main` without requiring a second person's approval. Rechecked branch-protection API: PR review requirement remains configured with **0 required approvals**, last-push approval disabled, `docs-integrity` and `artifact-domain` required in strict mode, conversation resolution and admin enforcement enabled, force-push and deletion disabled.

The active [main-owner-only-updates ruleset](https://github.com/NukeGeng/KORDRA/rules/25289723) targets `refs/heads/main`. Its only bypass actor is user `NukeGeng` (ID `139500775`) in `pull_request` mode; GitHub reports `current_user_can_bypass=pull_requests_only`. It controls who may update `main` through a PR; it does not bypass the separate branch-protection CI requirements. This is repository governance, not the Kordra product authorization model. Owner merge authorization, risk-based independent review and sensitive production approval must not be conflated.

## After every merge
1. Read HEAD/PR/CI rather than relying on a prior agent report.
2. Update only affected roadmap progress, feature status/evidence, technical debt, traceability and change history.
3. Do not close debt solely because PR merged or changed files exist.
4. Preserve old release/decision history and mark checks pending/unverified when evidence is incomplete.
5. Prevent duplicate progress tracking: roadmap = milestone facts; GitHub = live PR details; feature = source and test evidence.

## Decision and context freshness
Capture revision in audit reports. If base commit differs from package baseline, review diffs and re-evaluate impacted evidence before applying any prepared patch.
