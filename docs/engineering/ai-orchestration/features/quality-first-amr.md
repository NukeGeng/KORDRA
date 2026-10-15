---
id: FEAT-AMR-001
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
module_id: ENG-TOOLING
feature_id: FEAT-AMR-001
phase: developer-tooling
source_paths: ["docs/tooling/amr.py", "docs/tooling/amr_native.py", "docs/tooling/changeguard.py", "docs/tooling/doccheck.py", ".codex/config.toml", ".github/workflows/docs-ci.yml"]
test_paths: ["docs/tooling/tests/test_amr.py"]
---

# FEAT-AMR-001 — Quality-first engineering and native review

## User story / outcome
Repository contributors resolve task risk to a supported model and effort, preserve verified context, run real checks and triage independent native review without confusing sandbox approval with production approval. Implements the [v2.1 request](../../../../KORDRA_AMR_v2.1_Quality_First_and_Native_Review.md) through existing engineering tooling.

## Pre-conditions
Python 3.11+; Codex CLI for live operations. No new dependencies. Git and a connected provider are required for PR review, not assumed from the presence of `.github/`. Inspect native schema, effective config and model catalog before invocation. Missing/unsupported capability is explicit and fail-closed.

## Main flow
1. Discover runtime and source; mechanical lookup may stay deterministic.
2. Classify from structured evidence and affected paths: ROUTINE only for bounded, verified tasks; STANDARD defaults to ENGINEERING; security, state integrity and production risks require CRITICAL.
3. Resolve Luna High / Sol Medium / Sol High against native model discovery. No silent model or effort fallback.
4. Preserve source hashes, Git identity when available, decisions, consulted paths, boundaries, tests and findings in a structured handoff. Revalidate before reuse.
5. Invoke native Codex with actual model/effort parameters; retain a session when practical. Reclassify on new evidence.
6. Run deterministic gates; review, triage and rerun reproductions for fixes. Two failed repairs of one root cause trigger escalation; bounded attempts stop.
7. Associate native review with content identity and completion evidence. Human merge/production approval stays external and mandatory.

## Authorization / tenant-scope review
| Actor | Permission | Scope |
|---|---|---|
| Contributor | Existing repository/runtime access | Local development files |
| Native reviewer | Advisory review only | Exact content/revision reviewed |
| Authorized human operator | Existing explicit approval, target lease, immutable spec | Sensitive production operation |

No new product permission, role checks or tenant input. Tooling never dispatches to a cluster, queue or device. The native harness denies client approval requests; it never manufactures a human approval. Auto-review is probed only in a disposable local directory and must emit a native terminal decision to count as verified. Root instructions remain mandatory even in Full Access.

## Negative & recovery scenarios
Missing capability/catalog model, unsupported effort, silent model reroute, stale handoff, file escape/symlink, duplicate/stale review, missing terminal review, failed/timed-out check, unknown outcome and missing Git/provider all prevent a false passing claim. Expired/missing production approval and wrong tenant remain product test obligations; no backend exists here to execute them. Native probe crashes/timeouts are failures; never retry an unknown side effect automatically.

## Acceptance criteria
- Product bootstrap follow-up: when Kordra.slnx exists, the existing gates also run locked restore, build and product tests; failed restore/build blocks dependent stages to prevent stale-binary passes. Build outputs are excluded from source snapshots and cannot be mapped as source. Product test/bootstrap file changes require owner feature updates through changeguard.
- AMR-01–09: deterministic lookup, risk routing, explicit unavailable result, bounded escalation and source-validated handoff covered offline; enabled profiles receive real native invocation tests.
- AMR-10–13: distinguish native sandbox decision, local native review and provider automatic review; absent provider/approval support reported honestly.
- AMR-14–15: fixes require reproduction rerun and a current review; dismissals require evidence; unresolved blockers prevent completion.
- AMR-16–18: source/doc mapping checked, LAB observe-only preserved, unavailable usage stays unknown.
- Run strict docs, scope registry, unit/contract tests and native integration checks. Do not infer runtime support from a configuration file.

## Source & test traceability
Sources/tests above are planned before implementation. [Contract](../contract.md), [operating guide](../README.md), [runtime evidence](../testing-and-evidence.md). Working-tree evidence is permitted with hashes because this supplied directory has no Git metadata; no commit is fabricated. Status remains proposed until a commit and owner review exist.

## Open decisions, linked ADR and technical debt
No product architecture, technology, state machine or permission model changes. Engineering glue uses existing Python stdlib and native Codex. `[NEEDS-DECISION]`: repository provider, organization-approved Auto-review mode, branch protection and human independent signoff. See DEBT-AMR-001 and DRIFT-AMR-001 in the existing registers.
