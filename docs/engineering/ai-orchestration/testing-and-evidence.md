---
id: ENG-AMR-EVIDENCE
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
---

# Runtime capabilities and validation evidence

Observed 2026-10-15 on macOS, Codex desktop with bundled `codex-cli 0.162.0-alpha.17.2`; Python 3.14 locally, CI pinned to 3.11. This is local working-tree evidence, not a commit or released product verification. No Git metadata, branch, remote, PR/MR, backend source, build manifest, existing AMR implementation or AI config was present initially. Existing doccheck/changeguard/scopecheck and the AI review protocol were extended. All architecture ADRs remain proposed.

| Capability | Support / evidence | Limit |
|---|---|---|
| Native configuration/schema | Supported: installed help, app-server generated JSON Schema and strict-config live startup | Schema is version specific; incompatible runtimes fail explicitly |
| Model discovery | Supported: live native model/list advertises Luna and Sol with required efforts | Discovery alone is not invocation |
| ROUTINE | Verified invocation: gpt-6-luna / high, completed, `KORDRA_AMR_OK`, no reroute | Native start response observes selection; no server-internal attestation |
| ENGINEERING | Verified invocation: gpt-6.1-sol / medium, completed, same expected output, no reroute | Same limit |
| CRITICAL | Verified invocation: gpt-6.1-sol / high, completed, same expected output, no reroute | Same limit |
| Project defaults | Configuration written; effective config/read still returned user Sol High with null approval/sandbox values; native layer reports project config disabled because folder is not trusted | Requires trusted project/runtime configuration; active desktop chat overrides remain |
| Native Auto-review | Verified in isolated read-only probe: `item/autoApprovalReview/started` and `/completed`, native decision approved, risk low; disposable marker created | Only a local marker test; this chat remains full-access/never, which cannot exercise sandbox approval |
| Provider automatic Code Review | Git setup follow-up identified GitHub repository NukeGeng/KORDRA; no PR review execution verified | Requires Codex repository connection, automatic review settings, exact head revision evidence |
| GitLab automatic MR review | Not tested; provider and integration eligibility unknown | Do not assume GitHub parity |
| Native local Code Review | Implemented native review/start adapter; actual attempts at 180 and 300 seconds both timed out without a terminal turn/review-exit event. Not verified; failures retained privately | Stop repeated same-cause attempts; inspect runtime/protocol before further retry. Provider automatic review remains separately unconfigured |
| Independent local correctness review | Separate read-only Codex CLI invocation completed, found five concrete defects with in-memory reproductions; all five repaired with regression tests. Follow-up verdict and findings ledger are private current-content artifacts | This is independent local review, not native/provider Code Review verification |
| Deterministic checks / routing / handoff / triage | 43 offline tests passed, strict docs: 144 files / zero errors or warnings, LAB registry: 39 cases / zero errors. Source changes during gates invalidate results | Product integration, tenant isolation and production approvals cannot be validated against absent product code; rerun gates after any later fix |
| Usage | Native tokenUsage events present for successful probes | No cost/savings comparison claimed; other stages remain unknown unless events exist |

## Commands and receipts
`codex --version`, native `--help`, installed `app-server generate-json-schema`, then actual `model/list`, `config/read`, `thread/start`, `turn/start`. `python3 docs/tooling/amr.py probe-models` completed all three profiles with exit 0. `python3 docs/tooling/amr.py probe-auto-review` completed with exit 0. No simulated review decision was used. The client rejects any human approval request; Auto-review's decision came from the native runtime.

Both probes were rerun successfully. Native start responses confirmed `readOnly`, `networkAccess=false`, on-request policy and the expected reviewer. The Auto-review rerun explicitly loaded this project's `auto_review.extra_policy` as a strict native config override, so project trust could not silently skip it. CLI dry-run resolved the mapped AMR task to Sol High, generated real model/effort arguments and preserved human review; this dry-run is not counted as a live model invocation. The saved handoff passed source/Git validation.

`python3 -m unittest discover -s docs/tooling/tests -v`, `python3 docs/tooling/doccheck.py --strict-sources` and `python3 docs/tooling/scopecheck.py` executed locally. A disposable real Git range test verified changeguard rejects a stale feature spec and accepts the updated spec. No Git history was initialized in this supplied folder. A failed timeout fixture after diagnostic changes was corrected by initializing its event list; the same timeout assertion then passed. No test was removed or weakened.

The native local review exhausted a bounded 300-second retry after the initial timeout; results and source identity are recorded in `.amr-local/native-review.json`, with the initial failure retained in `.amr-local/native-review-attempt-1.json`. No terminal native event was observed, so neither attempt counts as a completed review. The separate independent CLI review completed and reproduced: unstable gate receipt acceptance, source symlink into excluded storage, previous-turn completion reuse, unrelated review-exit acceptance and string `false` accepted as a boolean. Regression cases cover each fix; `.amr-local/review-triage.json` records final source identity, rerun checks and follow-up references. Native model/approval probes are rerun after the exact-turn matching fix. No false positive was dismissed blindly.

A native exit proves execution only: triage must still reproduce/fix or dismiss findings with evidence and rerun checks and follow-up review. Do not read the existence of a receipt as a passing verdict. Current completion status is determined from matching receipts, not this historical narrative.

Private receipts: `.amr-local/model-probes.json`, `.amr-local/auto-review.json`, `.amr-local/gates.json`, `.amr-local/native-review.json`. These may contain runtime-derived information; they are ignored and private, not public evidence files. The committed evidence here is sanitized. Content hashes bind local review/gate receipts to source; subsequent documentation/source edits require rerunning gates and review. No commit SHA is invented.

## Acceptance coverage
Offline tests map AMR-01–09, 11, 13–18 to routing, ownership, unsupported model/effort, escalation limits, content staleness, path escape, approval separation and finding/gate validation. Native protocol contracts test timeout/crash, selection mismatch, reroute and required review-exit evidence. AMR-10 is exercised by the actual local native Auto-review probe. AMR-12 provider review is unavailable; local review is separate evidence. Product wrong-tenant, expired approval, duplicate dispatch, worker crash and unknown agent outcome remain required product tests; no application exists here to run them. No LAB resources were accessed.

## Remaining setup and decision owners
Git setup follow-up: the user supplied [NukeGeng/KORDRA](https://github.com/NukeGeng/KORDRA). GitHub reported an empty repository and authenticated administrative access; the existing local directory was initialized on `main` with that repository as `origin`, preserving all local files and excluding private receipts. Initial commit/push identity must be verified through Git rather than inferred from this note. Earlier statements about absent Git describe the original AMR validation environment. Git initialization invalidates earlier Git-bound handoff snapshots; regenerate before reuse.

Engineering maintainers must still confirm Codex project trust, enable automatic review if supported, require CI/human approval through branch protection, and obtain independent owner acceptance. DEBT-AMR-001 and DRIFT-AMR-001 retain the remaining verification obligations. Human approval for sensitive infrastructure remains mandatory and was not exercised against any real target.
