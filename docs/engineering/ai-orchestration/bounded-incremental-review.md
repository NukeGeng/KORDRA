---
id: ENG-AMR-BIR
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-19
---

# Bounded Incremental Review — AMR v4

The product owner approved this PR review policy. Owner: [FEAT-AMR-001](features/quality-first-amr.md), ENG-TOOLING. Codex Auto PR Review is the default independent PR reviewer. This protocol limits duplicate review cost while retaining deterministic CI, blocking findings and human merge authority. It changes no product architecture or ADR status.

## Default and round budget

Run one Codex Auto PR Review after the feature PR is ready and applicable CI checks complete. This is the default and normally the only request. A PR may use at most **three automated review requests total** if new commits or unresolved blocking findings justify follow-up. Each request consumes a round before dispatch, including failed, timed-out and unknown requests. Reopening a PR, changing sessions, losing a local ledger, renaming a branch or switching models does not reset the budget.

| Round | Required scope |
|---|---|
| 1 | Full PR diff: pinned PR base → reviewed head SHA |
| 2 | Last verified reviewed SHA → new head SHA, with surrounding context |
| 3 | Last verified reviewed SHA → new head SHA, with surrounding context |

Do not request a review again for an already reviewed SHA or a range whose source tree is unchanged. Subsequent requests require a new head or unresolved findings that need current-head follow-up; never spend a round merely because the provider produced an empty result. If the last run has not completed, there is no trustworthy incremental baseline. A base change, force push or lost ancestry requires maintainer reconciliation; do not silently reset the ledger or request a full fourth review.

Context includes PR intent, owner feature, accepted contracts/ADRs, affected callers/tests, prior findings and risk boundaries. Incremental scope focuses the reviewer; it does not excuse a regression outside changed lines. Group findings by category and root cause. Keep one canonical ID and stable bug key per bug, link duplicate provider comments to that ID, preserve unresolved findings and never repost a previously reported bug. Fixes require a rerun reproduction and current-head follow-up evidence. Dismissals require current-head counterevidence and rationale. Semantic deduplication remains maintainer judgment.

After round 3, **stop automated requests/triggers**. Escalate remaining blockers or uncertain/stale outcomes to the human maintainer with target, reason and evidence. Sol is not triggered automatically by a PR finding or exhausted budget. If the maintainer explicitly assigns a bounded defect after two evidenced failed Luna repairs, AMR routes it to the appropriate Sol debugger; that debugging task is not a PR review. Blocking findings must be resolved before merge. Budget exhaustion, a clean ledger structure or absence of new comments never implies approval.

## Manual enforcement and capability limits

Native Codex hard-cap and programmatic incremental-scope enforcement are **unverified**. Official [Code Review guidance](https://learn.chatgpt.com/docs/code-review) describes provider integration and review instructions; it does not establish a three-request hard limit. Repository instructions are advisory and cannot disable provider triggers.

The maintainer must reconcile the provider timeline and ledger before any follow-up request, reserve the next round, pin its SHA range and provide context where the integration permits. If automatic every-push review is enabled, prevent extra triggers using supported provider settings or stop further automatic activity and escalate the configuration gap. Do not claim BIR compliance while an uncontrolled trigger can launch round 4. This implementation neither changes remote settings nor posts comments and adds no dispatch controller.

Manual trigger authority is limited to Luna acting as the GOAL coordinator or the human maintainer. Sol Architect and debugger sessions must not post/edit GitHub PR comments, invoke `@codex review` / `@codex security review`, or start/manage BIR rounds. A review trigger is an external PR side effect even though Codex performs the review. Before an authorized manual request, verify applicable CI, current head, remaining budget and incremental base; record the initiating role and any session-bound evidence with the request. If origin is unknown, stop and reconcile before another trigger. These are procedural safeguards: the current validator cannot authenticate a session or prevent direct connector/CLI actions, and provider-trigger settings remain outside AMR.

### Recorded role-boundary incident — 2026-10-19

For PR #4 round 2 on `4821ea32894739667ec0339a76aab45b63b208d7`, the maintainer confirmed that the manual `@codex review` comment was sent from a Sol High session. GitHub attributes the comment to the connected repository account; its Codex summary identifies a manual request but does not expose the originating model/session. The Codex provider review completed on that SHA with no findings. Count the provider request as consumed BIR round 2/3 and retain its review result as provider evidence; separately mark the initiating action as an AMR role-boundary violation. The provider's reviewer identity does not authorize the originating Sol session's PR action.

Cause: AMR defined Sol as not being the PR reviewer but did not explicitly prohibit a Sol session from initiating the external review trigger. The manual GitHub connector path bypasses the GOAL router, and the ledger had no initiating-role record. The coordinator treated “Codex performs review” as sufficient separation and missed that sending the trigger was itself PR work. Correction: the role boundary above now reserves manual PR actions to Luna or the human maintainer. No provider setting or product behavior changed, and no technical trigger controller is claimed.

Keep a private ledger at `.amr-local/pr-review.json` (owner-only, ignored). Track PR identity, reserved/used rounds, provider request/run IDs, reviewed SHAs, canonical finding IDs, root-cause categories and escalation references. Reconcile it against the provider timeline on every handoff; a local file is not a cross-machine source of truth. Missing history blocks further requests until reconstructed; never assume zero. External provider setting changes remain a separate owner action.

## Ledger schemaVersion 1

This PR ledger is separate from local GOAL checkpoints and does not change their schemaVersion 1. New ledgers use `policyVersion: "4"`. The validator also accepts historical v3.1 ledgers under their original Sol follow-up rules. Example of one completed default review:

```json
{
  "schemaVersion": 1,
  "policyVersion": "4",
  "pr": {"repository": "https://github.com/example/repo", "number": 7},
  "base_sha": "1111111111111111111111111111111111111111",
  "head_sha": "2222222222222222222222222222222222222222",
  "rounds": [{
    "number": 1,
    "channel": "codex_auto_code_review",
    "scope": "full",
    "base_sha": "1111111111111111111111111111111111111111",
    "head_sha": "2222222222222222222222222222222222222222",
    "status": "verified",
    "request_evidence": "provider request reference",
    "review_id": "unique provider run/review identity",
    "completion_evidence": "terminal result on this head",
    "context": "PR intent, owner contracts, callers and tests",
    "new_findings": [],
    "tracked_findings": []
  }],
  "findings": []
}
```

Round statuses are `requested`, `verified`, `failed` or `unknown`; all count. Only a completed run with a unique provider `review_id` and terminal evidence may be `verified`. Round numbers are contiguous. Round 1 is full. Follow-ups are incremental from the preceding verified round's head. `tracked_findings` carries all previous canonical IDs for audit; `new_findings` contains only first reports. The canonical findings set must equal the union of first reports.

Each finding contains nonempty `id`, `category`, `root_cause`, `bug_key`, `evidence`, boolean `blocking` and `state=open|fixed|dismissed`. Category/root cause/bug key form a normalized duplicate fingerprint.

- `open`: a blocking finding prevents merge; nonblocking findings remain tracked.
- `fixed`: `reproduction_passed=true` and a current-head `followup_review`. A Codex follow-up references a verified current-head provider `review_id` within the existing budget. A human maintainer follow-up includes the current `head_sha` and evidence. Under v4, Sol debugging output is not a review attestation.
- `dismissed`: include a rationale, current-head `validated_sha` and counterevidence in `evidence`.

After exhaustion, remaining findings require `escalation` with `target=human_maintainer`, nonempty `reason` and `evidence`. A human maintainer may complete an exact-current-head follow-up with `escalation.review` containing `status=verified`, `reviewer=human_maintainer`, current `head_sha`, evidence and either full scope or an incremental base at the last verified provider SHA plus surrounding context. This does not claim Codex reviewed the new head and does not add a provider round. All findings still need their own resolution evidence.

```bash
python3 docs/tooling/amr.py validate-bir \
  --receipt .amr-local/pr-review.json --head <verified-current-PR-head-SHA>
```

The validator checks schemas, round accounting, SHA identities and ancestry, changed source trees, canonical findings and exhaustion escalation. Exit 0 means `ledger_valid`, exit 1 means `blocked`, and exit 2 means malformed input/runtime failure. It returns `merge_approval=false` and `native_hard_cap=unverified`. It does not trigger or stop provider review, authenticate entered references or replace CI, owner merge authority or explicit production approval.
