---
id: ENG-DEBT
status: proposed
owner: unassigned
last_reviewed: 2026-10-19
---

# Technical debt register, triage & prevention

## Policy
A debt entry is a **tracked obligation**; no TODO-only debt as sole record. Every debt has `DEBT-ID`, impacted feature/module, symptom/evidence, type, severity, risk, owner, proposed fix, verification, due/review checkpoint, status. Backlog severity proposals: `critical/high/medium/low`.

## Register
| Debt ID | Module / feature | Symptom & evidence | Severity | Owner | Mitigation | Exit criterion | Status |
|---|---|---|---|---|---|---|---|
| DEBT-000 | `unassigned` | TEMPLATE ONLY — remove when starting project | low | unassigned | n/a | first real debt registered | example |
| DEBT-AMR-001 | ENG-TOOLING / FEAT-AMR-001 | PR #1 merged on `2aa156f4`; merge-SHA docs/product CI passed; the only GitHub review is COMMENTED on older head `c95b0db`, not an independent APPROVED or provider automatic review | medium | engineering-maintainers | Owner explicitly chose solo-owner PR merge with zero required approvals; required CI and owner-only update rules remain. Keep local/native advisory review, GitHub review, merge authority and sensitive production approval distinct | Verify provider automatic review on an exact revision or explicitly decide to leave that integration unsupported; checkpoint: next feature PR | open; merge-policy decision recorded, provider integration unverified |
| DEBT-AMR-002 | ENG-TOOLING / FEAT-AMR-001 | Historical Goal E2E reviews were stale/timed out. On 2026-10-18, CLI `0.163.0-alpha.10.2` completed F0-OPP0-INV-SPEC's native Sol High review with exact thread/turn exit, stable content, passing full gates and accepted contributor triage; see [runtime evidence](ai-orchestration/testing-and-evidence.md) | medium | engineering-maintainers | Retain old failure receipts and do not replay/promote those checkpoints. Each future CRITICAL task and milestone still needs current independent review under v3.1; ordinary PR review and provider integration remain separate | Verified native review exit observed on an exact stable task snapshot; not an architecture/production approval | closed for the missing stable native-review evidence; historical checkpoint failures remain historical |
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

## AMR v3.1 follow-up

DEBT-AMR-001 remains open: default provider Code Review execution and native BIR hard-cap/incremental controls are unverified. BIR is manually enforced, with tested local ledger validation; no dispatch controller or provider-setting change is approved. Engineering maintainers own trigger accounting and must escalate blockers after round 3. Exit: verify provider execution on an exact PR SHA and separately verify native enforcement, or obtain explicit approval for a tested controller. DEBT-AMR-003 remains open; project defaults do not switch the active desktop chat.

## DEBT-AMR-004 — Native terminal failure diagnostics

Owner: engineering-maintainers / ENG-TOOLING / FEAT-AMR-001. Severity: medium; status: open. A native ephemeral task returned terminal `failed` with zero source edits, while the adapter summary retained status/model/turn identity but omitted the terminal error object; the destroyed ephemeral server could not subsequently serve thread history. Fail-closed unknown/reconciliation behavior remained intact, so no completion or automatic replay was allowed.

Mitigation for the explicit local continuation: a private observer retained the actual terminal status/error unchanged while invoking the existing dispatcher; it granted no authority, changed no result and performed no retry. The later task completed and passed current gates/review. This is diagnostic evidence, not a second controller. Exit: safely retain bounded terminal error diagnostics in private receipts with exact thread/turn binding, a controlled-failure smoke and regression tests, preserving redaction, source stability, legacy receipts and no-replay rules. Provider and native GOAL-control debts remain open separately.

## Provider review evidence update — 2026-10-19

DEBT-AMR-001's missing automatic-provider-execution evidence is resolved for PR #4 at `705c6227ddebd4293ae437a0bdefc5d907ae4ec7`: the native bot completed [review 5566543009](https://github.com/NukeGeng/KORDRA/pull/4#pullrequestreview-5566543009), triggered by PR opening, and returned four findings. The historical register describes what was known before this observation. The integration gap is no longer described as wholly unconfigured; the local adapter's `provider_review=not_configured` discovery value does not describe the separately configured GitHub connector.

Keep the debt open for native hard-cap/incremental enforcement capability and exact-current-head follow-up. The private PR #4 BIR ledger records round 1 and canonical unresolved findings; fixes require current-head evidence before merge. No fourth trigger, new review controller, automatic Sol review or human merge authority is introduced. DEBT-AMR-003/004 remain unchanged.


## DEBT-AMR-003 partial remediation — 2026-10-21

The owner-requested coordinator efficiency correction removes the default need for another native execution session merely to record local Developer progress. Current-session completion checks referenced real session/turn model context and stores a separate contributor receipt; inspected repository-only failed-task continuation no longer needs repeat design approval. Historical native/recovery checkpoints are unchanged. This resolves the direct-edit completion/bookkeeping gap through an explicitly different evidence kind, not fabricated terminal execution. Native desktop GOAL pause/resume, authenticated active-caller identity and automatic reconnection to ephemeral interrupted runners remain unsupported. DEBT-AMR-003 stays open for those limits; use current-session `step --context` / `finish` by default, and explicit `--native-runner` only when chosen. See the [contract](ai-orchestration/contract.md#current-session-developer-completion).


## Checkpoint reconciliation defect — 2026-10-22

FEAT-AMR-001 reproduced a contradictory `dict`/`str` check that made the documented reconciliation CLI unusable. The repair accepts its evidence reference, retains an exact private snapshot, and gives new preparations digest-bound protected-file manifests. Historical records without per-file baselines remain explicit limitations; their original hashes are not reset. Reconciliation captures known findings so recovery cannot hide unresolved provider defects. DEBT-AMR-003 remains open for native Goal-control/authenticated-caller limitations. This bounded tooling repair does not clear the six product findings on PR #10.


## DEBT-T12-001 — residual default transport fixture objects

FEAT-MSG-001 / NukeGeng / operational-gap / open. See DRIFT-T12-001 in [architecture drift](architecture-drift.md). Early PoC runs accidentally used the candidate's default transport schema outside their UUID schema. New runs bind both schemas explicitly. Preserve existing objects until read-only attribution and owner-authorized cleanup; do not silently delete a shared/default schema or label the PoC fully isolated. Exit: verify case-local transport under restricted runtime, attribute residual objects and record approved exact cleanup or an explicit retained-artifact disposition.


### DRIFT-T12-001 cleanup disposition — 2026-10-30

NukeGeng separately authorized deletion of exactly the two T12 native queue tables and their dependent indexes. Read-only attribution found one technical DeliveryProbe matching the approved fixed case and an empty scheduled queue. With no active fixture children, cleanup acquired table locks in a bounded transaction and dropped the two exact tables without CASCADE. Verification confirms the schema was retained and has zero relations. No HBA change or other object deletion occurred. Private receipt: `residual-default-transport-cleanup-receipt.json`. Residual-object DEBT-T12-001 is closed for cleanup; schema isolation regression and corrected restricted-runtime evidence remain part of final PoC validation, not automatic T12/runtime acceptance.
