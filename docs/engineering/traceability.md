---
id: ENG-TRACE
status: proposed
owner: unassigned
last_reviewed: 2026-10-19
---

# Requirements ↔ Module ↔ Feature ↔ Code ↔ Tests

## Coverage rules
`UseCase → feature_id → module_id → contract_ids → source_paths → test_paths → evidence/PR`.
- `source_paths: []` = not mapped yet, **not implemented**.
- Mỗi implemented/verified feature phải có ≥1 source, ≥1 test và evidence; manual exceptions require owner approval.
- Incident/regression maps back to feature. Architectural change maps to ADR and affected features.

## Initial cross-reference
| Use case | Feature | Module | Contract / evidence | Implementation status |
|---|---|---|---|---|
| UC-001 | FEAT-TEN-001 | MOD-TEN | permissions.md | proposed/unmapped |
| UC-002 / GOAL outcome 2 / F0-ART-I1 | FEAT-ART-001/I1 | MOD-ART | [digest contract](../contracts/artifact-identity.md), [feature evidence](../features/artifact-management/features/immutable-artifact.md), [merged PR #1](https://github.com/NukeGeng/KORDRA/pull/1) | Standalone digest Domain validation increment merged with docs/product CI passed on `2aa156f4`; ADR-0008 accepted narrowly; registry-backed artifact registration not implemented |
| UC-003 | FEAT-REL-001 | MOD-REL | message-envelope.md | proposed/unmapped |
| UC-004 | FEAT-APR-001 | MOD-APR | permissions.md | proposed/unmapped |
| UC-005/006 | FEAT-DEP-001 | MOD-DEP | driver-contract.md | proposed/unmapped |
| UC-007 | FEAT-AGT-001 | MOD-AGT | agent-protocol.md | proposed/unmapped |
| UC-008 | FEAT-MSG-001 | MOD-MSG | message-catalog.md | proposed/unmapped |
| UC-009 | FEAT-REC-001 | MOD-REC | messaging.md | proposed/unmapped |
| UC-010 | FEAT-PAD-001 | MOD-PAD | permissions.md | proposed/unmapped |
| Developer quality-first workflow | [FEAT-AMR-001](ai-orchestration/features/quality-first-amr.md) | ENG-TOOLING (engineering only) | [local contract](ai-orchestration/contract.md), [evidence](ai-orchestration/testing-and-evidence.md), [PR #1](https://github.com/NukeGeng/KORDRA/pull/1) | Tooling merged; direct coordinator/native desktop model verification and independent/provider automatic review have explicit limitations |
| Source and solution architecture baseline | [ADR-0009](../decisions/0009-source-architecture.md) | Architecture governance (proposal) | [six source specs](../architecture/README.md), [integration audit](source-architecture-adoption.md) | Proposed documentation only; no source migration or broad graph/host checks are accepted. One narrow Domain assembly dependency test already runs in product CI. |
| OP-01–OP-10 / OP-P0–OP-P4 | Candidate existing features; [FEAT-INV-001/I0](../features/infrastructure-inventory/features/register-target.md), [FEAT-PRJ-001/I0](../features/project-environment/features/environment-catalog.md) and [FEAT-SEC-001/I0](../features/connection-secrets/features/secret-binding.md) now have proposed read-only seams | Existing module owners; ConnectionProfile stays MOD-SEC-owned; backup ownership unresolved | [Operator requirements](../product/operator-workstation.md), [pilot sequencing](../product/operator-pilot-plan.md), [dated task evidence](../product/roadmap.md) | All three documentation tasks passed native execution/gates/review/triage, including High credential work after explicit resume. Kubernetes/Helm read-only is the owner-selected design target. Product source/tests remain unmapped. Local disposable host/reference feasibility passed with 14 HTTP assertions; native High re-review/triage recorded separately from the failed sandbox attempt. ADR-0010 is explicitly accepted for the scoped local bootstrap only, implementation pending; no pilot/runtime completion or company authorization. |
| Product technology reference A–F | No implementation feature assigned by catalog inclusion | Product planning; not new modules | [43-group catalog](../product/technology-landscape.md), [Beta gates](../product/oss-beta-acceptance.md) | Nonbinding Build/Integrate/Observe/Defer reference; no dependency or ADR acceptance. |

## File mapping after source exists
Goal-driven execution maps the existing FEAT-AMR-001 source/test registry to [goal workflow](ai-orchestration/goal-execution.md). Runtime smoke, E2E execution and review evidence are distinguished from policy/configuration in [runtime evidence](ai-orchestration/testing-and-evidence.md). Product milestone continuation remains in [roadmap](../product/roadmap.md).

The v1.2 source architecture files are proposals, not code mappings. Existing project paths remain owned by the current feature specs until an explicit decision and incremental migration update those mappings.

Set feature frontmatter `source_paths` and `test_paths` to actual repository-relative paths verified through `git ls-files`, then add `evidence` link in spec. The checker can validate existence in strict mode; review still validates semantic traceability.

## Current scoped bootstrap and folder evidence — 2026-10-18

The OP-P0 row above describes pre-bootstrap specification work. FEAT-INV-001/I1 now maps the actual four new projects and shared root solution/CI (shared paths remain co-owned by FEAT-ART-001). ADR-0010 was explicitly accepted by the maintainer. Current local gates: 114 tooling tests, 48 Artifact + 19 bootstrap tests, locked restore/build, docs/scope/mapping all pass. Stable independent native Sol 6.1 High review and contributor triage completed `KORDRA-OP-P0-BOOTSTRAP-CONT-001`; the old failed attempt remains failed. See [I1 evidence](../features/infrastructure-inventory/features/register-target.md#coordinator-final-acceptance--2026-10-18).

The [folder framework](repository-folder-framework.md) tracks 45 reserved locations with proposed README files, without new project/source mappings or ADR-0009 acceptance. Owner-selected Kordra-managed local accounts feed proposed FEAT-IAM-001 design; its source/test mappings remain empty, ADR-0003 and persistence/credential choices remain open. No company access, positive inventory read, full pilot completion or new GitHub CI evidence is claimed.

FEAT-IAM-001/I0 proposal readiness is now completed as a documentation task: native Sol 6.1 High execution/review, full existing gates and contributor triage passed. [IAM owner spec](../features/identity-access/features/authorize-resource.md) retains empty source/test mappings and lists planned tests separately. No IAM runtime or authentication implementation is verified; contract/PoC/storage and explicit ADR-0003 review decisions remain prerequisites.

## AMR v3.1 policy increment — 2026-10-18

FEAT-AMR-001 / ENG-TOOLING maps the existing `docs/tooling/amr.py`, `amr_goal.py`, `amr_native.py`, `.codex/config.toml` and existing mapped tests to Luna Max routing, critical/milestone/design review and backward-compatible policy-versioned execution receipts. AGENTS/WORKFLOW, owning feature, guide, local contract, [BIR protocol](ai-orchestration/bounded-incremental-review.md), PR template and change history describe the same owner-approved policy. Contributor/repository-local scope introduces no tenant permission or product runtime contract. Deterministic validation and exact native model/review observations are centralized in [runtime evidence](ai-orchestration/testing-and-evidence.md); provider execution/native hard-cap and desktop-control limitations remain explicit debt rather than passing claims.

## Synthetic IAM proof and AMR runner recovery — 2026-10-18

FEAT-IAM-001 / MOD-IAM maps `tests/InfrastructureInventory.Bootstrap.Tests/IdentityAccess/AuthorizationPolicyProofTests.cs` through the existing owner spec. The source map remains empty: evaluator/facts are private test-only candidates in the existing project. [I1 evidence](../features/identity-access/features/authorize-resource.md#coordinator-acceptance--2026-10-18): 66 policy cases, 133 product/141 tooling tests, formatter, native High design/execution/post-review and accepted triage. Earlier I0 empty test mappings are dated history; this does not implement product authorization. Permission keys/wire contracts are unchanged; ADR-0003 is a reviewed proposal awaiting human acceptance.

FEAT-AMR-001 / ENG-TOOLING retains existing adapter/dispatcher/test ownership for the bounded-runner developer context and validated pre-dispatch facts. Runtime source/task/lock/review checks are preserved. Its native task and independent review passed full gates; the later successful IAM source task exercised the corrected context live. Shared contract/workflow/evidence docs are reconciled here after task acceptance. Old no-op/failed checkpoints are not promoted; diagnostic debt DEBT-AMR-004 is explicit.

## ADR-0003 narrow human acceptance — 2026-10-18

FEAT-IAM-001 / MOD-IAM: [ADR-0003](../decisions/0003-tenant-authorization.md) accepted by the maintainer for the conceptual permission/trusted-scope baseline only. Existing synthetic proof and its 66 passing cases are unchanged; source mapping remains empty. The decision index, permissions evidence, module/feature, roadmap and change history record the scope and historical status transition. Runtime contracts, owners, authentication/storage and inventory ingress remain pending; no public schema or consumer migration changes.

## AMR v4 policy migration — 2026-10-19

FEAT-AMR-001 / ENG-TOOLING owns this migration through the existing `docs/tooling/amr.py`, `amr_goal.py`, `amr_native.py`, `.codex/config.toml` and mapped AMR regression suites. The current guide, goal workflow, local contract, BIR protocol, AGENTS.md and WORKFLOW.md describe the same Architect / Developer / Reviewer roles. `goal validate`, `goal accept` and milestone transitions perform deterministic validation and contributor triage without invoking an AI reviewer. [Runtime evidence](ai-orchestration/testing-and-evidence.md#amr-v4-policy-migration--2026-10-19) distinguishes catalog/config support from execution, provider review and hard-cap capabilities. Existing completed v3/v3.1 receipts remain historical; unfinished-task migration is limited to GOAL review obligations. No product runtime, accepted ADR, permission, provider setting, PR, merge or production state changed.

The [2026-10-19 PR #4 trigger incident](ai-orchestration/bounded-incremental-review.md#recorded-role-boundary-incident--2026-10-19) exposed a missing role boundary: Sol High initiated a manual Codex review trigger. The PR #5 follow-up reserves manual PR-review actions for Luna or the human maintainer and adds AMR v4 BIR schemaVersion 2 validation for declared trigger, allowed initiator and evidence; Luna requests require session evidence. Codex Auto PR Review finding `PR5-BIR-INITIATOR-001` was reproduced and fixed with negative regressions. The fields remain unauthenticated assertions and do not block direct provider actions. No trigger controller or provider setting was added. PR #5 passed exact-head CI and Codex follow-up, then was maintainer-merged as `6ed810ebff94a74bb03c7af4e23731a3cbd48cdb`.

## FEAT-IAM-001 / I2.7 cross-owner authority proposal — 2026-10-19

The proposal-only reconciliation draft maps tenant/membership facts to MOD-TEN, project/environment associations to MOD-PRJ, target ownership metadata to MOD-INV, durable append outcomes to MOD-AUD, and policy evaluation to MOD-IAM. The canonical responsibilities remain in those existing feature specs and [FEAT-IAM-001/I2.7](../features/identity-access/features/authorize-resource.md#i27--owner-reconciliation-and-decision-register-proposed). This increment is incomplete pending named human custodians and maintainer review; authority freshness, grant resolution, consistency and audit semantics remain `NEEDS-DECISION`. All source/test mappings remain unchanged, inventory ingress remains denied, and no runtime or ADR adoption is claimed.

## FEAT-INV-001 Linux fixture recovery — 2026-10-19

The [Inventory architecture test](../features/infrastructure-inventory/features/register-target.md#linux-fixture-first-run-recovery--2026-10-19) now provisions an existing isolated HOME/CLI-home and a private Linux `XDG_RUNTIME_DIR` for the SDK child process. This repairs NuGet migration-mutex startup while retaining the compiled forbidden-assembly-edge checks. The corrected code passed on macOS and in a sanitized Linux SDK 10.0.400 checkout (locked restore, zero-warning Release build, Artifact 48/48 and Inventory Bootstrap 85/85). Hosted docs and product CI both passed on implementation SHA `38da931edae3c36182c46712502b242598f9e0da` ([docs](https://github.com/NukeGeng/KORDRA/actions/runs/39188847531), [product](https://github.com/NukeGeng/KORDRA/actions/runs/39188847534)). The broader registration feature and real provider reads remain incomplete. No product contract, permission, ADR or runtime behavior changed.

## PR #4 review repairs — 2026-10-19

FEAT-AMR-001 / ENG-TOOLING owns the existing dispatcher/native adapter and mapped goal/native tests. [Provider review 5566543009](https://github.com/NukeGeng/KORDRA/pull/4#pullrequestreview-5566543009) completed automatically on `705c6227ddebd4293ae437a0bdefc5d907ae4ec7`, triggered by PR opening. Its four canonical findings cover legacy design-receipt acceptance, task outcome evidence, stale active-policy entry points and unsafe local PostgreSQL teardown wording. The first two use the [existing task contract](ai-orchestration/contract.md#task-outcome-evidence-and-receipt-compatibility); README, AI tooling boundary and review protocol now identify v4. The [local PostgreSQL runbook](../operations/local-postgresql-setup.md) names only the three dedicated roles for optional cleanup and preserves both pre-existing superusers. No database operation is performed.

The repair uses the serial Luna Developer route, not a new Architect or independent local review. Current-head provider follow-up, CI and human merge remain separate; neither this mapping nor the historical completed review approves a later SHA. Original native receipts/checkpoints remain retained; incomplete records without the new task outcome need explicit finalization rather than retrofitted success.


## FEAT-IAM-001 document recovery — 2026-10-20

DOC-IAM-RECOVERY-001 reconstructed the full pre-approval owner document, including I2.7a–I2.7g. Its first post-recovery file included annotations and had SHA-256 `91c63cc1452d40f5462f77c8e002c44f3c9fa2f97440b6ee1ef1f544d7bec9f0`, so it did not match the accepted input. DOC-IAM-RECOVERY-002 then restored the preserved exact snapshot at SHA-256 `88c2a3a0297ee94da793a440029e11435390f74af9c511f387232f85b6e4faaf`, equal to the FEAT-IAM-001 hash in the ADR-0012 acceptance receipt; I2.7a–I2.7g and their 19 acceptance rows remained intact. The acceptance receipt's embedded `91c...` recovery note is retained as a historical first-reconstruction observation. A subsequent [T01–T07 crosswalk](../features/identity-access/features/authorize-resource.md#t01t07-implementation-baseline--accepted-2026-10-20) reconciles that accepted feature input with the later exact ADR-0012, local-authentication-v1 and authority-admission-v1 approval. The feature's current hash therefore includes a post-acceptance documentation annotation; the original receipt/checkpoints remain unchanged. The earlier base-only recovery omitted part of the body and was insufficient.

## FEAT-IAM-001 / T01–T07 contract reconciliation — 2026-10-20

The current [feature crosswalk](../features/identity-access/features/authorize-resource.md#t01t07-implementation-baseline--accepted-2026-10-20) makes the accepted implementation boundary explicit: IAM consumes current session and policy facts; TEN owns membership/assignment; PRJ/INV provide authoritative resource facts; AUD owns durable append. T06 uses ADR-0012's single-process fair writer-preferring gate and the accepted authority/admission U/D sequence, audit outcomes, bounds and transport residual. T03 owns local authentication. T06 remains dependent on T02/T04/T05, and positive inventory ingress stays denied until the real revoke/fairness/expiry/audit/HTTP handoff proof passes. Historical I2.7 open-decision text is retained but cannot override the later accepted T01–T07 contracts. This is documentation reconciliation only; no new runtime or ADR decision was made.

The immutable approval receipt keeps pre-acceptance input hashes. At this reconciliation, the feature input had matched its accepted hash `88c2a3a0297ee94da793a440029e11435390f74af9c511f387232f85b6e4faaf`; its current hash is `37cecb30122b785d693f27674d28a157100b799b0404efcf8dbe95bd4b56ec77` after the crosswalk annotation. Current ADR-0012, local-authentication-v1 and authority-admission-v1 hashes are respectively `8d333c3e28440431b4a8a93cad5a45bf3b8a843b2a2b53bab6eb7303c329b178`, `b31936226468223be08cc13a217099da8d8cacac6584659aadfecdc9f659259c` and `b4ec40bc58f26295bcca91ba1c83d817527d31d40bee29f6ba229e0f795ba720`, which differ from the receipt's pre-acceptance hashes. Their original byte snapshots are not present in this worktree, so this records a direct semantic comparison of the T01–T07 provisions in the current accepted-state text, not byte-identity proof. The exact current hashes and this limitation are retained in `.amr-local/recovery/t06-contract-reconciliation.json`; the receipt was not rewritten.

## Comprehensive feature design handoff — 2026-10-20

Maintainer-approved overall direction maps through the [delivery design](../architecture/control-plane-delivery-design.md) and [Luna handoff T00–T14](luna-implementation-handoff.md) to existing Foundation/F0-C/M1-A, OP-P0, M1-B/C, M2-A/B and release gates. This is design evidence only; no feature runtime, source/test mapping or old checkpoint is promoted.

| Use case / milestone | Owner features | Contract / decision | Current readiness |
|---|---|---|---|
| Local enrollment/login/session/recovery | FEAT-IAM-002 / MOD-IAM | [Authentication v1](../contracts/local-authentication-v1.md), ADR-0012 | T01 persistence and T02 audit implemented; T03 login remains pending |
| Current tenant/grants/catalog/target and protected read | IAM-001, TEN-001, PRJ-001, INV-001, AUD-001 | [Authority/admission v1](../contracts/authority-admission-v1.md), ADR-0012 | I2.7 behavior preserved; T04–T07 accepted for implementation, proof pending |
| Profile/credential and real read-only observations | SEC-001, INV-001, OBS-001 | Owner OP-P0 slices, provider/allowlist/identity decisions | T08–T09 remain provider/operational decision-gated |
| Artifact → frozen release → scoped approval | ART-001, REL-001, APR-001 | [Release/execution v1](../contracts/release-execution-v1.md), ADR-0005 adoption | T10–T11 design-gated; existing digest implementation retained |
| Independent durable execution → verify → rollback | MSG-001, WFL-001, DRV-001, DEP-001, OBS-001 | Release/execution v1; ADR-0001/0002/0004/0006 and concrete worker/provider protocol | T12–T13 require adoption and isolated conformance; no Mac-close claim |
| Independent clients integration | Product API owners → separate clients owner | [Client handoff v1](../contracts/client-api-handoff-v1.md) | Contract handoff proposed only; no clients/Website source, test, process or submodule action |

Existing 14 owner feature pages now have bounded implementation slices with actors, scopes, dependency gates and named acceptance cases. At design-package authoring time, IAM-002 had empty source_paths/test_paths; the subsequent T01 implementation evidence below supersedes that status. No real product/account/DB/provider test was executed by authoring the package itself. Architect model provenance and exact design hash manifest are private handoff evidence, not a GOAL execution receipt. No debt added/closed or ADR automatically accepted.


## FEAT-IAM-002 T01 persistence — current implementation evidence, 2026-10-20

T01 follow-up: the same mapped IAM test file now includes an exact persisted-column/ignored-field regression and explicit fail-closed missing-locator errors for xUnit 2.9. The suite contains 17 cases; current execution/gate evidence is separate from historical terminal checkpoints. The owner's requested Sol 6.1 Low continuation is a task-local override, not a routing or architecture change.

The preceding design-package paragraph records the authoring-time state and is superseded for T01 only. `local-account-session.md` maps the implemented IdentityAccess persistence project, additive migration, separate explicit DbMigrator, and isolated test project. Locked restore, Release build (0 warnings/errors), EF pending-model check, all 16 IAM tests, and full local AMR gates passed; the full gate included 159 tooling tests, 48 Artifact tests, 85 Inventory Bootstrap tests, strict docs, scope and source mapping. The migration round trip ran only against isolated `kordra_test`; the `kordra_app` privilege probe used read-only catalog queries. No production/runtime database was migrated or modified. This verifies T01 persistence and its mapped tests only. Login/session endpoints and behavior, audit foundation, owner facts, authorization/admission and protected inventory remain incomplete. The native T01 GOAL checkpoint is failed after a sandbox-limited attempt and has no completion receipt; it was not replayed, and local gate success does not change that checkpoint status.

The T01 status was reconciled on 2026-10-21: the original failed native checkpoint remains unchanged, while the separate maintainer-authorized evidence-recovery successor is completed from current artifact hashes, exact native-context evidence and the already-passing applicable gates. The recovery did not replay T01, alter its original receipt, or start T02.


## FEAT-AUD-001 T02 append/lookup foundation — local implementation evidence, 2026-10-21

The approved append/lookup slice maps to `Kordra.slnx`, `src/Modules/AuditCompliance/`, `src/Host/Kordra.DbMigrator/`, `tests/AuditCompliance.Tests/` and the existing Inventory architecture graph test. The append model canonicalizes allowlisted intent fields and an explicit `Completed`/`Denied`/`Cancelled`/`Unknown` outcome; it excludes sensitive payloads, makes exact same-intent replay idempotent, rejects changed-content reuse, and returns lookup receipts without claiming protected work completed. Owner state and audit stage together on one PostgreSQL transaction. The append deadline starts before context creation and covers pool wait, connection open, transaction, staging and commit: one second for required appends and 100 ms for denial, without a retry queue. The additive migration preserves audit rows and limits `kordra_app` to SELECT/INSERT. No production `kordra` migration, API route, login, owner-data integration or provider operation was performed.

Final deterministic gates passed after the append-budget and commit-failure regressions against base `63a7951f48b4441e880defaf315c3755ff79842b`: docs 227 Markdown / 0 errors and warnings; scope 39/39; AMR tooling 175 tests; locked restore; Release build with 0 warnings/errors; product suites 48 Artifact, 17 IAM, 34 AUD and 85 Inventory Bootstrap tests; source mapping 46 changed source files / 0 problems. The AUD suite includes isolated PostgreSQL migration, role-ACL, table/pool saturation, deferred commit-failure (`Unknown` plus absent exact lookup), and exact lookup after a deliberately discarded successful caller response, using only loopback `kordra_test`. The failed native origin is retained; a separate maintainer-authorized evidence-recovery successor is completed without replay. AMR implementation changes are outside these product commits.

## T02 private checkpoint evidence

All 38 outputs were inspected for the completed evidence-recovery successor; the original seven checkpoint files remain byte-identical. Private receipts are excluded from Git. Hosted CI/review and merge remain pending.

## FEAT-CLIENTS-001 / independent frontend repository — 2026-10-20

ENG-TOOLING owns the approved [ADR-0013](../decisions/0013-clients-git-submodule.md) migration: Product retains `clients/` as one gitlink, with its source/docs/test content independently owned by [KORDRA-Clients](https://github.com/NukeGeng/KORDRA-Clients). The [feature](ai-orchestration/features/clients-repository.md) maps `.gitmodules`, pin validation, guarded Product traversal/context, CI and boundary regressions; shared tooling remains co-owned with FEAT-AMR-001. Clients retains an extracted history and bootstrap `387c56edc75df8332775e267e8cac50462b781dd`, with [passing standalone CI](https://github.com/NukeGeng/KORDRA-Clients/actions/runs/39262407727). All four former reservation files are byte-identical at the extraction commit; bootstrap updates their links for standalone use.

The [operating guide](clients-development.md) defines workspace, pin, contract and service ownership. Source/stack/IAM/Website/production scope is unchanged; frontend runtime is absent. Product GOAL remains paused, completed checkpoints are preserved, and pre-split unfinished snapshots require explicit refresh without replay. Product integration CI and human merge remain separate from the already published Clients bootstrap.

## PR #9 round-1 corrections — 2026-10-21

FEAT-IAM-002 maps PR9-IAM-HISTORY-001 to the additive IAM history-ACL correction and isolated ACL regression. FEAT-AUD-001 maps PR9-AUD-CLEANUP-001 to deadline-linked rollback/detachment and the bounded whole-append wait with delayed-cleanup regressions. Review 5594801684 completed on `1f3e9832153b43a63b50765dc3ca78ef1f80a75f`; source fixes require follow-up review of the updated head. One of three BIR rounds is consumed; no further AI review is launched by these changes. Foundation ADR-0012 and Clients ADR-0013 retain separate decisions after the identifier collision was reconciled. Checkpoints and unfinished AMR work remain separate.

## FEAT-IAM-002 T03 local authentication — implementation evidence, 2026-10-21

T03 maps the accepted local-authentication-v1 behavior to the explicit HTTPS loopback local-pilot profile, IAM-owned enrollment/login/session/recovery service, same-transaction AUD adapter, ephemeral cookie protection, bounded request and login controls, and offline account command. The default API remains its separate health-only profile. No tenant grant, protected inventory endpoint, public account bootstrap or real infrastructure access was introduced. The new [HTTP fixture](../contracts/fixtures/local-authentication-v1.http.json) contains route and field shapes only; credential and token values are omitted. The [error catalog](../contracts/error-codes.md#accepted-local-authentication-v1-http-additions) records the accepted local-authentication-v1 responses.

The local T03 verification passed 34 focused tests, the maintainer-authorized runtime-role read-only probe, and all 224 product tests without skips. The combined worktree gate also passed, but its 205 tooling tests include unrelated AMR changes that remain local; isolated feature-commit validation is reported separately in PR evidence. The two-owner reconciliation is retained privately with original checkpoint hashes. T03 checkpoint completion remains unclaimed because an outside-write protected-content mismatch predates that reconciliation. Hosted CI, provider PR review and human merge are separate gates.

### FEAT-IAM-002 — PR #10 bounded corrections, 2026-10-22

Provider findings 4335586242/6253/6261/6272/6276/6285 map respectively to command replay, unknown-commit fence, admission capacity, activity order, denial budget and TLS constraints. The existing `LocalAuthenticationService`, shared `FairAsyncReaderWriterAdmissionGate` and `LocalPilotHostOptions` implement the corrections under unchanged ADR-0012. Existing IAM test files carry HTTP replay/conflict/restart, real committed mutation ACK loss, queued/future gate closure, older-after-newer PostgreSQL refresh, cancellation-budget and certificate chain/SAN/EKU regressions. Initial focused suite: 72/72, zero skips; subsequent full-gate evidence binds the final local source. Publication/provider thread resolution is pending and does not retroactively complete the failed historical T03 checkpoint.

## FEAT-IAM-001 / I2.7 custodian assignment — 2026-10-20

The maintainer assigned NukeGeng as human custodian for all eight I2.7 decision areas: authentication, tenant membership, grant assignment/resolution, project/environment binding, inventory target identity, durable audit, enforcement/consistency, and security/maintainer review. The assignment is recorded in the [I2.7 decision register](../features/identity-access/features/authorize-resource.md#i27--owner-reconciliation-and-decision-register-proposed). At that assignment checkpoint, exact-content approval was pending; custody alone assigned no formal module ownership or accepted decision. The later approval below supersedes only the behavioral-design review status. Source/test mappings are unchanged and inventory ingress remains denied.

## FEAT-IAM-001 / I2.7 bounded behavioral approval — 2026-10-20

The maintainer explicitly authorized the coordinator to assess and approve the document conditionally if suitable. After correcting audit intent versus actual admission, removing obsolete mandatory independent-native-review wording and fixing a typo, the coordinator approved I2.7a–I2.7g as a bounded behavioral baseline. The [canonical decision record](../features/identity-access/features/authorize-resource.md#i27g--human-decision-and-bounded-handoff) binds reviewed file SHA-256 `a2a2083221751761ba20e9da6fda5e17ed7b8bdfd25474118ae8f5d56d0add78`, exact task/content identities and the conditional user instruction. Existing AMR checkpoint `KORDRA-OP-P0-IAM-I27-DESIGN-001` is completed without replay or another AI-review invocation. TEN/PRJ/INV/AUD handoff notes now reference NukeGeng's existing custody assignment. At that decision point, concrete mechanism/ownership/limit decisions and ADR acceptance remained unchanged; the later accepted ADR-0012/T01–T07 contracts supersede that status only within their recorded scope. The 19 scenario rows are future acceptance requirements, not executed coverage or runtime evidence.

## FEAT-AMR-001 owner-mapped output paths — 2026-10-21

The existing AMR task contract now distinguishes existing input `paths` from exact, owner-predeclared `output_paths`, allowing a validated task to create new source/test/docs without scaffolding. The existing goal validator confines writes to those outputs, rejects traversal/private/cache/symlink targets and still binds observed artifacts to `write_paths`. Regression coverage is in `docs/tooling/tests/test_amr.py` and `test_goal.py`; no product runtime, checkpoint history, model routing or review policy changes. T02 uses the same owner-mapped mechanism; its product implementation and local validation are recorded below. The original native task timed out without an execution receipt and remains failed; deterministic gates do not substitute for native GOAL acceptance.

## FEAT-AMR-001 coordinator efficiency — 2026-10-21

Existing `amr.py`, `amr_goal.py`, `amr_native.py` and their three owner-mapped test suites implement current-session preparation/completion, inspected local Developer continuation, explicit optional native runner and conservative deterministic evidence reuse. Actor is the repository contributor; scope is local engineering files under the owner's optimization request, with no tenant/runtime permission or ADR change. AGENTS/WORKFLOW, the feature, guide, goal workflow and local contract carry the same semantics. Historical recovery/native evidence stays separately validated; original T01/T02 checkpoints are not migrated by installation. Tests cover no dispatch/review, stale scope/model/artifact/gates, recovery exclusions and evidence-cache invalidation. Current verification is recorded in the feature after applicable checks; provider review and human merge remain separate.

## FEAT-AUD-001 T02 checkpoint recovery — 2026-10-21

The maintainer's current request authorizes the bounded checkpoint correction. Existing `goal recover` adopts exact current source/gates and actual historical Luna Max turn-context into `KORDRA-OP-P0-FOUNDATION-T02-RECOVERED-20261021`, with `completion_kind: evidence_recovery` and no new execution attempt. All 38 declared outputs are inspected and hashed in private companion evidence. The original failed checkpoint and all six other pre-existing goal files remain byte-identical. T02's authorized isolated PostgreSQL tests are declared explicitly; this uses schemaVersion-1 recovery rather than asserting repository-files-only continuation. The AUD feature, module and roadmap distinguish the completed successor from the failed native origin. Current applicable gates retain 199 tooling/184 product tests (34 AUD), exact-base mapping and current docs/scope checks; no AI review, new architecture, production migration or T03 work is performed. Feature commits/remote PR verification remain separate.

## FEAT-AMR-001 checkpoint reconciliation repair — 2026-10-22

The maintainer requested repair of the remaining AMR bookkeeping issue. `docs/tooling/amr_goal.py` and `docs/tooling/tests/test_goal.py` own the reconciliation string-input fix, exact private checkpoint snapshots, protected-file manifest/diagnostics and preservation of known findings through recovery. The original failure reproduced before the fix. Historical digests remain compatible, unchanged status reads preserve bytes, and no model, review or product operation is dispatched. T03 PR #10 has six provider findings on `79a6ee72af8f0240c941074e183f0ca1a284309b`; deterministic gate success does not resolve them or authorize task/merge completion. Actual validation and checkpoint reconciliation receipts remain private under `.amr-local`.
### FEAT-IAM-002 — PR10-AUDIT-SETUP-001

The authentication service now keeps setup-only `IIdentityAuditTransactionFactory.EnlistAsync` outside the commit-uncertainty catch. The factory port explicitly documents its no-mutation/no-commit setup boundary. `IdentityAccessAuthenticationServiceTests` injects cancellation during enlistment and a separate setup exception, asserts that no commit protocol was entered, checks the safe HTTP 401 and confirms existing `/me` plus fresh login remain available without restart. Both cases failed before the correction and passed after it; the five actual-commit lost-ACK/exception cases and the best-effort denial-budget case also pass. Accepted runtime architecture and contracts remain intact; full publication gates and provider follow-up remain separate evidence.

## T04 — IAM definitions and TEN ownership — 2026-10-22

Base is the human-merged T03 `main@25bd7e71fbeb5370d19baac1878ab1609c7d72a3`; branch `codex/foundation-t04-definitions-tenancy`. Existing T03 execution/recovery evidence remains historical and unchanged. The maintainer explicitly permitted this Sol Medium session to implement T04 only; this is a scoped exception, not an AMR routing change. No additional AI review or PR review trigger was run.

| Owner child slice | Implemented paths and behavior | Validation / remaining boundary |
|---|---|---|
| FEAT-IAM-001 / MOD-IAM | IAM Contracts, RoleDefinitionStore, role-definition model/additive migration; exact current revision, offline manifest replay/conflict, retirement and allow/deny interpretation | RoleDefinitionTests and DefinitionRuleTests; actual XML/restored/PE architecture checks. Not T06 authorization or a role-edit HTTP route. |
| FEAT-TEN-001 / MOD-TEN | Own Contracts/context/migration; founding membership, explicit exact-scope grants, delegation ceiling/provenance, lifecycle and hashed command receipts; host-only AUD enlistment and guarded offline migrator | TenantMembershipTests uses only isolated kordra_test with synthetic data: cross-tenant FK/owner denial, same-principal isolation, replay, expiry/revocation/ambiguity, rollback, gate cancellation and unknown-commit recovery. No live runtime migration or workspace provisioning. |

Shared consumers FEAT-IAM-002/AUD-001/INV-001/ART-001 acknowledge solution/context/host/lock/test graph changes in their existing feature records. Accepted ADR-0012 and public v1 contract decisions are unchanged. T04 remains local unpublished implementation until validation/publication evidence is recorded; T05 owner scopes and T06 endpoint/U-D wiring are still separate approved tasks, not hidden completed work.

Targeted execution evidence in this working tree: 9 IAM definition/rule cases and 23 TEN cases passed, zero skips. Both owner `dotnet ef migrations has-pending-model-changes --no-build` commands reported no model drift. Strict doccheck and exact-base working-tree source mapping passed. Final required validation is `python3 docs/tooling/amr.py gates --base 25bd7e71fbeb5370d19baac1878ab1609c7d72a3` with private isolated-test/read-only probe locators; its generated `.amr-local/gates.json` records actual per-check results/content identity. Targeted evidence is not a substitute for that full completion gate. No remote CI or PR review result is claimed for unpublished T04.

Release validation after the fixture-scheduling correction passed all 281 Product tests (48 Artifact, 39 AUD, 85 Inventory Bootstrap, 86 IAM, 23 TEN), zero skips, and all 168 tooling tests. Locked restore, Release build (zero warnings/errors), strict docs, scope and 42-source-file mapping also passed. The first rerun correctly rejected its aggregate content identity because a final co-owned documentation acknowledgment was added while it ran; its individual results are historical evidence, not a valid stable completion receipt. A fresh unchanged-tree full gate must establish final acceptance; the private gate receipt and T04 preparation evidence retain the actual outcome rather than overriding the stale run.

## FEAT-TEN-001 PR12 replay correction — 2026-10-23

Finding 4345647477 maps to TenantMembershipService.ExecuteAsync and TenantMembershipTests. The suspend and self-membership-revocation replay tests failed before the correction; three self-invalidating command cases now pass, including self-assignment revocation, changed hash/new key, other/no subject and inactive IAM account. The owner service only reorders exact receipt lookup after trusted active subject selection and before mutable grant resolution. No new authority, schema, migration or ADR is introduced. Final deterministic gates and exact publication identity remain separate from independent provider follow-up and human merge.

## FEAT-AMR-001 PR11 integration and finding fixes — 2026-10-23

Merge reconciliation preserves main Product runtime/tests and both sides of historical AMR/Product evidence. The existing tooling/contract/feature and `test_goal.py` own the deletion-tombstone and Architect output/write restriction fixes for findings 4343932365/4343932372. Both original failures were reproduced before repair; targeted continuation/Architect tests pass. No Product behavior, accepted ADR, historical checkpoint or provider review state is rewritten by this integration.

## T05 — PRJ catalog and INV target association — 2026-10-22

Continue from the locally verified T04 input on codex/foundation-t04-definitions-tenancy (Git base remains human-merged T03 25bd7e71fbeb5370d19baac1878ab1609c7d72a3). Original T04 source/doc snapshots and SHA-256 manifest are preserved privately under .amr-local/t05-preparation/t04-input/ for future feature-scoped commit separation; historical checkpoints and T04 full-gate evidence are unchanged. No commit/push/PR or AI review was initiated. Model/effort metadata for this resumed turn is unverified; no model substitution or extension of the historical T04-only model exception is claimed.

| Feature / module | Files and behavior | Contracts, authorization and validation |
|---|---|---|
| FEAT-PRJ-001 / MOD-PRJ | ProjectEnvironment Contracts/runtime/context/migration/store; project/application/environment lifecycle, monotonic revisions, immutable initial classification/policy and PRJ target association | Accepted ADR-0012; authority/admission v1; project.manage at verified applicable scope; trusted policy association. ProjectCatalogTests and actual graph/PE checks. |
| FEAT-INV-001 / MOD-INV | Existing INV runtime plus target metadata Contracts/context/migration/store; stable target identity, exact owner binding and Active/Disabled/Deleted lifecycle | target.manage at exact project/environment; current resolved PRJ ownership and matching binding revision. TargetAssociationTests exercises rollback, replay, FK/scope/policy denial and both unknown-commit outcomes. |
| Shared Host / owner consumers | CatalogMutationCoordinator, typed offline DbMigrator dispatch/locators, solution/lock graphs | No cross-owner table access; one transaction/gate/AUD commit; current subject/policy revalidated. Existing IAM/AUD/TEN/ART feature records acknowledge shared impacts. |

Targeted evidence: 28 new PostgreSQL cases and four architecture tests passed; PRJ and INV model/migration comparisons found no drift. Final validation command is python3 docs/tooling/amr.py gates --base 25bd7e71fbeb5370d19baac1878ab1609c7d72a3 with private isolated-test/read-only probe locators. Its actual stable-content results are retained in .amr-local/t05-preparation/final-gates-output.txt and completion.json; no remote CI or independent PR review result is asserted for this unpublished content.

Remaining boundary: T06 supplies production trusted session/tenant/current-policy/owner adapters, whole-workspace offline composition, guarded endpoints and U/D proof. HTTP inventory/PRJ/TEN ingress remains closed. No runtime migration, real tenant/account/workspace provisioning, provider or credential access occurred. Broader project policy replacement, provider/deployment/worker and full module readiness remain outside this slice. Accepted ADRs are unchanged; no existing debt is closed or hidden as completed integration.

## FEAT-PRJ-001 PR13 locator regression — 2026-10-23

ProjectEnvironmentMigrationCommand and ProjectCatalogTests own finding 4345619894. The documented PRJ locator now reaches the existing strict target guard; a synthetic forbidden target is rejected before connection. The regression failed before repair; final validation/publication evidence is recorded separately. No actual offline migration, new authority, schema change or automatic AI review is implied.
