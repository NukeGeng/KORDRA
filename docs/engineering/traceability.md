---
id: ENG-TRACE
status: proposed
owner: unassigned
last_reviewed: 2026-10-24
---

# Requirements ↔ Module ↔ Feature ↔ Code ↔ Tests

## T08 Keychain design proposal — 2026-10-23

FEAT-SEC-001 / MOD-SEC with IAM/INV/PRJ/AUD consumers links the complete proposed ADR-0015 and Credential boundary v1 decision package. The owner selected macOS Keychain; the proposal specifies offline SEC metadata/history/receipts, exact manifest and child IPC, neutral current dependencies and U/C/D, bounded native child cleanup, compatibility, source adoption slices and SEC08-01–12 acceptance. Neither source nor runtime/provider test evidence exists for T08. Exact human design approval and isolated synthetic Keychain operational authorization remain separate, with no existing item or real network access implied. Historical/T07 evidence and accepted ADRs are preserved. The private design manifest binds exact proposal/context hashes; doc validation is not implementation or native-profile verification.

## T07 bounded protected read — 2026-10-23

FEAT-INV-001 / MOD-INV implements the approved T07 proof using the existing metadata GET. Source: `InfrastructureInventory/Contracts/ITargetMetadataReader.cs`, `CurrentTargetMetadataReader.cs` and mapped Host composition. The immutable server selection carries invocation/tenant/target/exact scope without granting authority. The default Product registration uses the real local registry reader. Wire v1 remains the same seven fields; no dependency, migration or ADR change.

`T07BoundedReadHttpTests.cs` extends the existing actual-owner fixture with 23 cases: delayed golden read and durable U/D digest, project/environment scopes, grant/binding/target/tenant invalidation, audit unavailable/unknown at both boundaries, eight invalid/failing source results, caller cancellation, provider deadline and untrusted requests. All 37 CurrentAuthorityHttpTests cases passed together against synthetic PostgreSQL owner state and actual HTTPS. A separate bootstrap assertion checks the production reader registration. Existing fairness/capacity/partial-write/clock tests remain required in the full gate. The OpenAPI source hash map includes the new port/reader/composition with no wire change.

Final gate evidence is retained privately in `.amr-local/t07/`, bound to its final source digest. Historical blocked/native checkpoints remain unchanged and are not replayed or relabeled completed. Native model/effort remains unverified. No hosted CI/PR review, live runtime provisioning, credential/provider access, full Foundation or OP-P0 readiness is claimed by this local slice.

## T06 scoped runtime implementation — 2026-10-23

FEAT-IAM-001 / MOD-IAM, FEAT-PRJ-001 / MOD-PRJ and INV/TEN/AUD consumers implement accepted ADR-0014/resource-policy-v1 in the existing owner paths. Concrete source/test mappings are in all six owning specs. The local slice supplies current exact owner facts and grant/policy evaluation, immutable policy/head plus unique binding/history, persisted UTC floor, shared bounded U/D and actual HTTPS handoff, typed offline policy impact/apply/recovery, one metadata GET and environment-create v2. Canonical installation custody also excludes simultaneous runtime/offline/migration processes. No new dependency or runtime architecture is selected.

Tests exercise real PostgreSQL IAM/TEN/PRJ/INV/AUD with real cookie/session/HTTPS, reject invalid current owner state, compare full policy/binding identities/revisions/hash between U/D, hold D through bounded write/flush, abort actual partial output without retry, retain writer ordering and capacity, reject clock rollback across restart, and distinguish setup rollback from lost commit ACK. Shared policy changes update only explicitly listed bindings; exact receipt replay does not republish/regrant. Runtime grants and PRJ scope/history FK/nullable uniqueness are checked in isolated `kordra_test`. The [HTTP fixture](../contracts/fixtures/resource-policy-v1.http.json) and [OpenAPI snapshot](../contracts/openapi/resource-policy-v1.openapi.json) are checked against actual response fields and source hashes.

The final deterministic receipt/source digest is stored under `.amr-local/t06-preparation/` separately from historical checkpoints and approval receipts. Native profile remains unverified for this continuation; no native/local AI review, PR trigger, hosted CI claim, real `kordra` migration/provisioning or company/provider operation is made. The broader whole-workspace resource/grant provisioning composition and unproved list/history/other catalog routes remain pending; this bounded slice does not mark the entire Foundation milestone or native GOAL completed. See the [operator runbook](../operations/resource-policy-v1.md).

## Coverage rules

### T06 resource-policy bounded design accepted — 2026-10-22

FEAT-IAM-001 / MOD-IAM and FEAT-PRJ-001 / MOD-PRJ link the maintainer-accepted [ADR-0014](../decisions/0014-resource-policy-v1.md) and [Resource Policy v1](../contracts/resource-policy-v1.md). They specify exact policy/current-head and binding identities, fixed evaluation/applicability, same-transaction IAM/PRJ/AUD publication, compatibility and planned acceptance. INV remains the target fact owner and AUD keeps existing append contracts. The maintainer explicitly approved the exact design on 2026-10-22; the ADR records pre-acceptance hashes and the private receipt preserves the decision. This is accepted design evidence, not new mapped source, executed tests or T06 completion. Existing implementation/checkpoint evidence is preserved. Implementation paths/consumer list and ordered owner slices are in contract sections 8–10.

### General coverage rules
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

## FEAT-TEN-001 / IAM-001 PR14 earliest provenance deadline — 2026-10-23

Finding 4355763873 maps to TenantMembershipService's shared provenance/effective assignment resolution and TenantMembershipTests. Stored-chain tests cover all-branch minimum (nested/shared ancestor, finite or unbounded leaf), equality expiry, invalid chains and valid unbounded authority. The real TEN reader is wired into ProtectedReadAdmission with instrumented other ports to reproduce expiry inside U/D audit; this is protocol/handoff proof with synthetic data, not a new real-provider or HTTPS claim. Before repair four assertions failed, including D incorrectly returning Completed; after repair no protected bytes are handed off after that upstream deadline. Existing grant creation ceilings and accepted in-flight transport remain tested. Provider follow-up and human merge are separate from local correction evidence.

## PR14 P2 current policy readiness — 2026-10-23

Finding 4355763870 maps to OfflineResourcePolicyCoordinator and ResourcePolicyIntegrationTests under IAM/PRJ with AUD/INV consumer acknowledgments. Ten pre-fix cases reproduced invalid fresh-bind success, including expiry after actual audit staging. All 19 policy integration cases pass after repair. Tests assert no binding/history/receipt/audit side effects on denial, zero commit attempts on post-stage expiry, retained gate availability, legitimate active bind/inactive publication and exact receipt replay after expiry/current-head change. This is local contributor evidence; provider follow-up and human merge remain separate.

## PR14 artifact-domain timestamp fixture — 2026-10-23

Run 39915294133 failed two TEN provenance assertions on head 4df02bd because expected pre-save .NET timestamps retained fractional microsecond ticks discarded by PostgreSQL. TenantMembershipTests now forces that representation on every OS and derives expected timestamps from persisted rows. Before repair all three variants reproduced the CI mismatch locally; post-repair verification preserves strict deadline equality and boundary denial without altering production code. Hosted follow-up must establish Linux success on the new head.


## T08 exact design acceptance — 2026-10-23

FEAT-SEC-001 records the maintainer's exact ADR-0015/contract approval and separate isolated synthetic-Keychain fixture permission. The unchanged pre-acceptance proposal/context hashes and source-bound successful design gate receipt are retained under .amr-local/t08/design/; original snapshots and human decision are under .amr-local/approvals/adr-0015-t08/. Design validation passed 217 tooling and the existing 473 Product/T07 regressions before status annotation; no T08 runtime or native-Keychain test is claimed. Strict doc validation checks the acceptance annotations separately. Developer implementation is now eligible within T08.1–T08.5; all existing checkpoints and T07 evidence remain preserved.

## T08 local credential implementation — 2026-10-24

FEAT-SEC-001 / MOD-SEC maps actual ConnectionManagement Contracts/runtime, additive migrations, offline manifest/coordinator/DbMigrator and new SEC tests. IAM owns neutral current dependency evidence and U/C/D; Host composes SEC and authority ports without cross-owner runtime dependencies. The installation maintainer applies metadata only while API is stopped. Runtime target.read requires current trusted tenant/resource/policy/grant/binding and a registered consumer; Product registers no credential network consumer or online binding route.

Local proof covers exact manifest/history/replay/expiry/SQL privileges, receipt/AUD reconciliation, raw-byte framed IPC, bounded startup/child cleanup, synthetic current-owner HTTPS cancellation/revoke/rotation/audit/capacity interleavings and actual restored/PE project boundaries. Mac conformance uses only a newly identified UUID fixture, designated apphost ACL and synthetic items, then verifies deletion; existing secrets and clusters remain excluded. Exact final commands/counts/source digest are recorded under .amr-local/t08/implementation/; local gates do not claim hosted CI/review or native GOAL dispatch. Non-Mac CI cannot establish native Keychain execution.

The previously verified API folder organization is integrated by moving current files into Hosting/Composition/Endpoints/Authorization/Audit/OfflineAdministration and refreshing mapped source identities; public schemas are unchanged. The deterministic Product-suite timeout is bounded at 300 seconds to accommodate the larger serial suite, with a regression; other checks retain 90 seconds and missing/failed tests still fail. No AMR model/review policy changes. Historical checkpoints and completed T07 evidence are preserved without replay. Stable installed executable/hash, private locator mapping/ACL and T09 identity/TLS/RBAC/provider design remain future activation requirements. See [credential runbook](../operations/credential-boundary-v1.md).

### Local T08 validation result — 2026-10-24

Canonical exact-base AMR gates passed on the stable implementation tree: 218 tooling tests and 524 Product tests, zero failures/skips; locked restore, Release build, docs/scope/submodule pin and source ownership checks passed. The Product count includes 32 SEC/IPC/process/native cases and 18 T08 HTTPS cases covering both project and environment scope. Strict doccheck checked 232 Markdown files, 214 IDs and 21 modules with zero errors/warnings. The Mac native fixture completed exact raw-byte/service/ACL/missing/locked checks and verified deletion. Counts establish local validation only; no hosted CI/PR review, existing-secret access, real installation migration or cluster result is claimed. Final receipts remain private under .amr-local/t08/implementation/. Accepted design, checkpoint history and T07 evidence are preserved; no debt or additional architecture approval was introduced.

## PR15 known launch failure correction — 2026-10-24

PR15-SEC-LAUNCH-001 / FEAT-SEC-001 maps NativeSecretLookup, ChildLifecycleTests and T08CredentialHttpTests. Original code reproduces the launch classification error and closed writer gate; the repair separates completed failure from unresolved ownership. Tests cover throwing/null startup, retained hung/unreaped handling, three failed starts across two slots, no consumer/C and successful unrelated metadata/writer admission. Exact red/green/final-gate receipts remain private under .amr-local/t08/pr15-launch-failure/. Accepted ADR-0015 and existing schemas/permissions are unchanged. Local repair evidence does not assert provider follow-up or merge approval.

## T09.1 design preparation — preparation before acceptance, 2026-10-24

FEAT-INV-001 with IAM/SEC/AUD consumers now has proposed ADR-0016/Kubernetes workloads v1, based on merged PR15 main 40acd58. The owner chose workloads-first; actual native metadata verifies the bounded Architect profile gpt-6.1-sol/high. The proposal records precise namespace identity/TLS/RBAC, transport/string residuals, resource/byte/deadline limits, typed DTO and freshness-at-D compatibility with exact planned files. It does not map new runtime/tests or claim conformance/live reads. Source/test checkpoints and accepted T08 design remain unchanged pending exact T09.1 approval. Author checks are documentation/scope checks, not routine Product tests, a second AI review or PR action.


Accepted scope update — 2026-10-24: NukeGeng approved exact ADR-0016 and Kubernetes workloads v1 for T09.1a–d, including the post-C managed-header exception, offline private-PKI residual, exact Namespace identity GET and additive config/scope/freshness caps. This supersedes pending-design approval wording in this section only. Runtime implementation and K09-01–12 verification remain pending. T09.1e, existing secrets, real clusters, Helm and OBS runtime remain excluded; historical checkpoints are preserved without replay.

## T09.2 lab preparation and accepted provider design — 2026-10-24

FEAT-INV-001/MOD-INV owns the accepted Nomad provider design, with existing IAM/SEC/AUD consumers. [ADR-0017](../decisions/0017-nomad-workload-observation.md) and [contract](../contracts/nomad-workloads-v1.md) were exactly accepted by NukeGeng on 2026-10-24 and declare planned paths/N09-01–10 before implementation. [Local lab](../operations/local-nomad-lab.md) records separately authorized direct API/TLS/ACL and synthetic allocation evidence in private T09-NOMAD-LOCAL-001 receipts. No Product source/test mapping, native GOAL completion or checkpoint recovery is claimed; the historical goal-status owner-path failure was observed and preserved.

## T09.2a source adoption — 2026-10-24

FEAT-INV-001 maps NomadTargetTrust/NomadTrustConfiguration and NomadTrustTests in the existing INV runtime/test projects. FEAT-IAM-001 retains ownership of AuthorityAdmission, ProtectedReadAdmission and their regression suite; existing SEC/Host CurrentCredentialDependencyReader rejects unregistered config-qualified plans. Accepted ADR-0017 is unchanged. Targeted validation passed 40 trust and 58 admission cases; full gate outcome is recorded separately and must not be inferred from these counts. No provider call, live credential use, company operation or AI review occurs in T09.2a.

## T09.2b–e source and conformance adoption — 2026-10-24

FEAT-INV-001 maps the existing INV Contracts/runtime Nomad DTO, transport/projection/reader and Host consumer/registry/endpoint; IAM/SEC own existing admission, binding and native lookup ports. Shared test TLS/native declarations stay test-only in existing projects. N09 cases map to NomadTrustTests, NomadTransportTests, NomadProjectionTests and T09NomadWorkloadHttpTests; optional T09NomadLabTests is the separately recorded own-lab operation, not a fake fixture substituted for live evidence. Schema/source pins are in [OpenAPI](../contracts/openapi/nomad-workloads-v1.openapi.json). Exact final gates/source identities and private native-cleanup receipt remain under .amr-local/t09/. PR/CI/merge is separate; historical checkpoint evidence is preserved without replay.
