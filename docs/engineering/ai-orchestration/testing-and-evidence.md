---
id: ENG-AMR-EVIDENCE
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-17
---

# Runtime capabilities and validation evidence

Evidence observed on 2026-10-16 in the Kordra checkout on macOS with Codex desktop/CLI and Python 3.14. The checked-out branch is `feat/artifact-identity-foundation` at `2d72e7cd7c3c7340d749b2def8f03d0e07b1618c`; local changes are not yet part of that commit. No production or LAB-01 resource was accessed. Native selections below come from the app-server `thread/start` response and matching terminal turn, not policy text or agent self-report.

## Runtime capability results

| Capability | Observed result | Limit / follow-up |
|---|---|---|
| Native schema and configuration | `app-server` strict startup, `config/read`, generated schema and `model/list` work. The catalog advertises the exact required Luna and GPT-6.1 Sol efforts. | Protocol is version-specific; unsupported fields/models fail closed. |
| ROUTINE smoke (`AMR-LIVE-A`) | Requested and observed `gpt-6-luna/high`; native turn completed without reroute. It checked AMR document links and Sol naming. | Source identity changed during the task (`source_stable=false`), so this is runtime-selection evidence, not a stable review of the current patch. |
| ENGINEERING smoke (`AMR-LIVE-B`) | Requested and observed `gpt-6.1-sol/medium`; native turn completed without reroute and source identity stayed stable. It analyzed changeguard ownership and exercised its negative mapping case. | Read-only task; no implementation was delegated to this thread. |
| CRITICAL smoke (`AMR-LIVE-C`) | Requested and observed `gpt-6.1-sol/high`; native turn completed without reroute. It found missing route evidence, incomplete working-tree mapping, staged-index freshness and historical gate-receipt defects; regression tests now cover the findings. | Source identity changed during this task (`source_stable=false`); findings informed fixes but this was not the final code review. |
| Collaboration sub-agents (historical v2.1 exercise) | Three collaboration agents were created for the earlier Routine, Engineering and Critical smoke tasks. | This is historical evidence only. AMR v3 disables project sub-agent creation and repository policy now requires a single coordinator session. The collaboration tool did not return authoritative model/effort metadata. |
| Native Auto-review | Verified in a disposable directory: native `item/autoApprovalReview/started` and `/completed`, decision `approved`, risk `low`, exact marker created; `gpt-6.1-sol/medium`, no reroute, network disabled. The project review policy was explicitly loaded for the probe. | This validates a local eligible sandbox decision only. It does not exercise this full-access chat or authorize production work. |
| Native source-architecture assessment | Read-only review completed on the integrated source/docs snapshot; app-server route and terminal turn both observed `gpt-6.1-sol/high`, terminal status `completed`, `source_stable=true`. It identified the existing Domain assembly boundary test, root-solution migration dependencies, AMR path ownership, module assembly granularity and messaging ownership issues; the documentation now records these findings. | This is an architecture impact assessment, not a native Code Review approval of the AMR patch and not human acceptance of ADR-0009. |
| Native local Code Review | A prior goal E2E produced a terminal `review/start` result on `gpt-6.1-sol/high`. Its `source_stable` flag was false because repository edits overlapped, so it cannot satisfy review for the current patch. The newer E2E's native review timed out without a matching terminal event. | Adapter is implemented; a verified review of the final stable patch is still missing. Same-cause retries are paused pending a runtime/protocol or provider-path change. |
| Provider PR Code Review | GitHub returned no review submissions on PR #1 through head `adaaa0399de4f797288e0f9f8c70d9984fafd6cd`. | Automatic provider review remains unverified; no review result has been observed. |
| Project configuration (historical v2.1 observation) | The earlier config enabled sub-agent defaults and that observation reported the project layer disabled. | The AMR v3 implementation below supersedes these defaults; its post-change `config/read` result is recorded separately. |
| Native desktop GOAL | The Codex Goal runtime reports `paused`. | This session has no supported resume operation. Repository checkpoint state does not resume the native Goal, and no resume is claimed. |

## Persistent goal execution

The executable local coordinator is `python3 docs/tooling/amr.py goal`; it persists a private, atomic task DAG/checkpoint, validates ownership and dependencies, records dispatch before invoking the native model, verifies the actual model/effort/thread/turn, runs gates, and requests a separate native review. It does not control native Goal status, merge a PR or approve production work.

`KORDRA-FOUNDATION-AMR-002` executed its documentation worker on `gpt-6-luna/high`. The worker changed only its declared feature file; the recorded full gates passed for that content snapshot, including 89 AMR tests, locked .NET restore/build, 48 product tests and working-tree ownership checks. The later native Code Review timed out before returning a terminal review identity. The checkpoint was reconciled to `validation_failed`, with no automatic worker replay. Additional `.temp` source files and later documentation edits changed the content identity; that old gate receipt does not certify the final integrated documentation.

The earlier `KORDRA-FOUNDATION` run also remains `validation_failed`: its native review completed on `gpt-6.1-sol/high`, but its source identity was unstable. Neither checkpoint is accepted as a completed E2E task. Private receipts are stored under `.amr-local/` and are excluded from public evidence.

## GitHub identity and branch protection

`ssh -T git@github-nukegeng` authenticated as **NukeGeng**; `origin` uses `git@github-nukegeng:NukeGeng/KORDRA.git`, and `gh auth status` reported the active NukeGeng account using SSH. GitHub's branch-protection API confirmed for `main`: pull requests required, one approval, stale reviews dismissed, last-push approval required, `docs-integrity` and `artifact-domain` required in strict mode, conversation resolution required, admin enforcement enabled, and force-push/deletion disabled. This does not approve or merge PR #1.

On PR #1 head `35f9f0c4b4af89d3da48597868dbff72f17c7560`, GitHub Actions completed both required checks successfully: [Documentation integrity](https://github.com/NukeGeng/KORDRA/actions/runs/38876780973) and [Product validation increment](https://github.com/NukeGeng/KORDRA/actions/runs/38876780926). The follow-up head `42ae8fc2f38b1583935cecfaef1f53ca5d625360` also passed both checks: [Documentation integrity](https://github.com/NukeGeng/KORDRA/actions/runs/38876992261) and [Product validation increment](https://github.com/NukeGeng/KORDRA/actions/runs/38876992393). Head `adaaa0399de4f797288e0f9f8c70d9984fafd6cd` passed [Documentation integrity](https://github.com/NukeGeng/KORDRA/actions/runs/38877165413) and [Product validation increment](https://github.com/NukeGeng/KORDRA/actions/runs/38877165446). These runs validate their respective exact heads; later pushes require fresh checks.

## Final validation after v1.2 integration

On 2026-10-16, the final full gate run used checkout HEAD `42ae8fc2f38b1583935cecfaef1f53ca5d625360` plus the then-current PR-check evidence edits later committed as `adaaa0399de4f797288e0f9f8c70d9984fafd6cd`. The verified base was `d0eff75951d1d587b1d9689c189a8a62bae18d58` (`origin/main` matched the remote branch). The local AMR gate command `python3 docs/tooling/amr.py gates --base d0eff75951d1d587b1d9689c189a8a62bae18d58` completed with `source_stable=true` and `local_status=passed`:

| Check | Result |
|---|---|
| `python3 docs/tooling/doccheck.py` and strict source validation | Passed: 157 Markdown files, 139 tracked IDs, 21 module metadata entries, zero errors/warnings. |
| Scope cases | Passed: 39 cases, 39 unique IDs, zero errors. |
| AMR Python unit suite | Passed: 89 tests. |
| Locked .NET restore | Passed for root `Kordra.slnx`. |
| Release build | Passed with zero warnings and zero errors. |
| Product tests | Passed: 48, zero failed/skipped. |
| Working-tree source ownership | Passed: 17 changed source/config/test paths, zero mapping problems. |
| `git diff --check` | Passed. |

The gate receipt is private under `.amr-local/`. Product validation was run locally and did not access a cluster, production service or LAB-01 resource. The earlier pre-import receipt is historical and does not certify this integrated snapshot.

## Remaining validation and decisions

- Obtain a native or provider Code Review on one stable current revision. Local review timeouts do not count as success; provider review requires the current PR head and configured repository integration.
- Architecture-owner, security-reviewer and maintainer decisions and the acceptance evidence listed by ADR-0009 remain outstanding; the source architecture proposal is not an accepted baseline.
- Keep architecture tests described by [the v1.2 rules](../architecture-test-rules.md) proposed until ADR-0009 and real project edges are accepted and executable checks are added to CI.
- Product wrong-tenant, expired-approval, duplicate side-effect and worker-recovery tests remain future product obligations; the present checkout has no product hosts or persistence layer to run them.
- Human review for merge and explicit approval for sensitive production operations remain mandatory.

## AMR v3 implementation and runtime support

The v3 implementation is in the current working tree on `feat/artifact-identity-foundation`. Offline validation passed **102 AMR tests**, including Medium→Medium continuation, Medium→High checkpoint/pause, conditional High→Medium recommendation, stale Git/source rejection, interrupted/unknown work, unsupported local native pause, and the unverified-selection boundary. These tests validate local state handling; they do not simulate a real user changing the desktop model.

Post-change `python3 docs/tooling/amr.py discover` on Codex CLI `0.162.0-alpha.17.2` reported the project config layer loaded, effective project defaults `gpt-6.1-sol/medium`, and `agent_defaults.subagents_enabled=false`. The model catalog advertised `gpt-6.1-sol` with both medium and high effort. The AMR app-server adapter also passes an explicit `agents.enabled=false` override. This verifies project configuration and model availability, not the currently selected desktop coordinator model; the local CLI has no session-bound observation for that chat. `goal resume` therefore records manual selection as unverified.

The local CLI records the switch checkpoint but cannot change the desktop Goal status. At inspection time the native Goal was already paused; this implementation did not mutate or resume it. No live model switch or native Goal continuation was executed, so those acceptance points remain unverified and require a user model change plus a subsequent native runtime observation. The existing sandboxed native task runner continues to verify its own child session selection independently.

## PR #1 follow-up: checkpoint integrity and route consistency

On 2026-10-17, follow-up changes on `feat/artifact-identity-foundation` addressed the four P2 review issues against PR head `c95b0db25dcbf5ab376037855508864a39b3de85`:

- Validation pins a digest of all non-writable paths at dispatch and blocks later source drift outside `write_paths`; repairs within declared writable paths remain revalidatable.
- Checkpoint reads validate the immutable task plan without requiring historical input files to remain present. New dispatch still checks live inputs strictly.
- `goal recommend` records the task-bound current selection and `goal step` reuses it only if the catalog still advertises it and it meets the task quality floor. The native execution receipt verifies the actual request and observed selection.
- Direct coordinator edits have no native task receipt and remain ineligible for `goal validate`/`goal accept`. The operating guide now states that the serial `goal step` runner is required for checkpointed completion; this adapter limitation remains tracked in DEBT-AMR-003.

`python3 docs/tooling/amr.py gates --base d0eff75951d1d587b1d9689c189a8a62bae18d58` passed on the follow-up working tree: strict docs (157 Markdown files, zero errors/warnings), scope checks (39 cases), AMR unit tests (110), locked .NET restore, Release build (zero warnings/errors), product tests (48 passed), and source mapping (17 paths, zero problems). These are local results; GitHub checks for the new pushed head must run separately.

Codex CLI confirmed `gpt-6-luna/max` for a full-PR review attempt, but that review exceeded the 300-second process limit during its .NET stage and exited 130 without returning findings. It is not a completed review. The user requested Luna Max for this work; no native session-bound evidence exposes the coordinator model selection, so that selection remains unverified. Provider Code Review and human approval for merge are still separate requirements.
