---
id: FEAT-WFL-001
status: proposed
owner: unassigned
last_reviewed: 2026-11-01
module_id: MOD-WFL
feature_id: FEAT-WFL-001
phase: MVP
source_paths: ["src/Modules/WorkflowOrchestration/Domain/", "src/Modules/WorkflowOrchestration/Contracts/", "src/Modules/WorkflowOrchestration/Infrastructure/", "src/Host/Kordra.Api/Composition/Workflows/", "src/Host/Kordra.Api/Kordra.Api.csproj", "src/Host/Kordra.Api/packages.lock.json", "Kordra.slnx"]
test_paths: ["tests/WorkflowOrchestration.Tests/", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowCommitClassificationTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowTransitionCodecTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowTransactionSchemaTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowRootDatabaseTests.cs", "tests/Messaging.Tests/CommitReplyFault.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowFixtureIncomingHandler.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowFixtureWire.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowFixtureProgram.cs", "tests/InfrastructureInventory.Bootstrap.Tests/Internal/Generated/", "tests/InfrastructureInventory.Bootstrap.Tests/InfrastructureInventory.Bootstrap.Tests.csproj", "tests/InfrastructureInventory.Bootstrap.Tests/Fixtures/workflow-transition-requested-v1.json", "tests/InfrastructureInventory.Bootstrap.Tests/Fixtures/workflow-transition-requested-v1.sha256", "tests/InfrastructureInventory.Bootstrap.Tests/packages.lock.json"]
planned_source_paths: ["src/Modules/WorkflowOrchestration/", "src/Host/Kordra.ControlWorker/"]
planned_test_paths: ["tests/WorkflowOrchestration.Tests/", "tests/ControlWorker.Tests/"]
---

# FEAT-WFL-001 — Durable deployment workflow through failures

## Versioned durable workflow slice — proposed 2026-10-20

Luna T12 WFL task follows durable transport, frozen approval and the cross-process worker authority decision. WFL owns instance/step/timer/target lease and recovery, DEP owns deployment outcome. First workflow is an immutable reviewed template version with typed allowed steps, not arbitrary shell or a visual editor. Bind each instance to exact template/spec/operation/tenant/target. Independent ControlWorker hosting must keep execution progressing when the Mac client/API is closed where the approved topology permits; a worker on the same sleeping Mac cannot pass this criterion.

Adopt [release-execution v1](../../../contracts/release-execution-v1.md), the concrete persisted transition/lease protocol, worker identity/current authorization and retry/timeout limits before implementation. Persist dispatch before delivery; delayed authorization/approval is revalidated. Expired lease with possibly active provider work becomes Unknown, not permission for a competing worker. Recovery reconciles journal/target evidence and never replays an irreversible step from a UI retry.

Acceptance WFL-D1: restart at every transition/inbox/outbox boundary produces one logical operation and consistent history. WFL-D2: duplicate/stale epoch/result, expired/revoked approval, wrong tenant/target and unsupported workflow version reject. WFL-D3: unknown execution remains Unknown until verified evidence; writer handoff cannot overlap unfenced provider work. WFL-D4: real independent-host restart/Mac-close evidence, finite deadlines, crash-safe timer and cancellation behavior are recorded. No process/container is launched by this proposed task description.


## User story / outcome
**Actor:** System / Operator Recovery. **Goal:** Durable deployment workflow through failures. **Primary owner:** `MOD-WFL`. **Phase:** MVP.

## Pre-conditions
Valid deployment request persisted with immutable correlation IDs.

## Main flow
Persist step → validate approval → acquire lease → write dispatch command to outbox → accept agent result → verify → finalize/release lease.

## Alternatives / exceptional flow
If agent outcome missing mark Unknown; inspect target/journal before redelivery; operator can resolve with evidence.

## Messages and integration contracts
`deployment.requested.v1; deployment.dispatch.v1; deployment.unknown.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Restart between DB write and send, stale lease epoch, duplicate command, expired approval.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Crash at each transition leads to one logical deployment operation and consistent audit.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## Source & test traceability
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.


## T12 bounded design — 2026-10-30

[Thiết kế T12 tiếng Việt](../../../engineering/t12-design.vi.md) và [ADR-0019](../../../decisions/0019-durable-transport-workflow-bootstrap.md) đề xuất compatibility/fault PoC trước runtime adoption. T12.0 chỉ kiểm chứng MSG bằng test-only PostgreSQL fixture; không hoàn tất WFL, distributed authorization hoặc Mac-close. Source/test mappings vẫn chưa implemented. Chưa có package, process, permission hoặc technology adoption nào được cấp quyền bởi ghi chú này.

## T12.2–T12.4 detailed proposal — 2026-10-31

[Full Vietnamese design](../../../engineering/t12-remaining-design.vi.md), [Durable Workflow v1](../../../contracts/durable-workflow-v1.md), [Worker Authority v1](../../../contracts/worker-authority-v1.md) and ADR-0021/0022/0023 define the next scoped adoption, pending exact human acceptance. Custodian proposed NukeGeng. Source/test paths remain unimplemented; planned roots are WorkflowOrchestration Domain/Contracts/Infrastructure, WorkflowOrchestration.Tests and the existing reserved ControlWorker host with ControlWorker.Tests. IAM/AUD existing owner projects provide proposed public transaction/fence/service ports.

Acceptance uses WF01–WF08, AU01–AU05, DB01–DB02, HO01–HO03 and RH01–RH03 in the [conformance plan](../../../engineering/t12-conformance-plan.md). New workflow.start/read/cancel/reconcile permissions are proposals with no implicit grants; pilot is Development/Test checkpoint workflow, not deployment execution. Driver effects remain T13. No accepted architecture, runtime source, existing approvals or operational rights are changed by this document update.

## T12.2–T12.4 scoped design accepted — 2026-10-31

NukeGeng accepted the exact ADR-0021/0022/0023 and companion contract/design/conformance/runbook package after all18 frozen proposal hashes matched. This supersedes the immediately preceding pending-acceptance wording only for that scoped design. No WFL/worker implementation, permission grant, consumer activation, remote qualification or operational authority is claimed. Developer starts12.2a (Domain/Contracts and pure tests) under an allowed native Developer profile; no repeat design approval or Sol review is needed for covered slices. SQL/fixture/host effects retain separate operation approvals; preserve existing checkpoints and user drafts.

## T12.2a first source increment — 2026-10-31

Domain and Contracts projects now implement tenant/installation/instance identity, closed workflow/step states and pure structural transition/version guards. Tests cover terminal resurrection, Unknown versus cancel/expiry, boundary edges, invalid enums, stale/future/overflow versions and dependency isolation. Solution and actual architecture XML graph include only these two BCL libraries and the test project. This is a partial12.2a increment: canonical immutable specification/template/golden vectors and full pure conformance remain pending, before any persistence/worker/HTTP activation. Domain structural approval is never current authority or database CAS evidence.

## T12.2a immutable specification source — 2026-10-31

Added immutable full resource/release/request binding, closed checkpoint template, strict bounded canonical storage reader/writer and SHA256 business hash. Independent fixed byte/hash vector and negative tests cover resource substitution, unsupported fields/versions/environments, duplicate/authority/secret fields, ordered steps/nulls, bad schedules/IDs/hashes, UTC normalization, version exhaustion and terminal/Unknown state guards. Code remains BCL-only with no HTTP/DB/worker activation. Full feature gates/publication are pending; no persistence or independent-host evidence is inferred from pure tests.

## T12.2b persistence increment — 2026-11-01

Infrastructure now contains the EF/Npgsql revision1 owner model for all nine WFL tables, scoped composite keys/FKs, version concurrency tokens, request uniqueness, bounded/hash/state constraints, isolated model-cache keys and offline DDL generation. The owner context rejects history rewrites/deletes, altered canonical initial binding, mutable business specification, skipped versions and autocommit. WorkflowStore stages creation/immutable request receipts, instance CAS/history and initial step custody on a caller-owned root; it never commits or grants authority. Initial custody cannot become takeover merely because time passes. No runtime DI, HTTP routes, worker activation or automatic migration was added.

Targeted tests cover model/DDL, scope associations, fixture isolation, sanitized target rejection, immutable history and fail-closed owner-root guards. The separately approved isolated PostgreSQL fixture has now passed creation/rollback/duplicate/hash-conflict/wrong-tenant/composite-FK/CAS/initial-custody checks and verified schema cleanup. General CI leaves this operation-gated test skipped without its exact fixture flag; its actual execution evidence is separate. SQL artifacts/proposal and coordinator evidence are private under `.amr-local/t12-remaining-design/`. Model constraints and EF mutation guards do not prove restricted worker SQL ACLs (T12.3), multi-owner atomicity (T12.2c), timer/recovery completion (T12.2d) or distributed authority. Offline revision1 SQL and checksum manifest are checked against the current EF model. All eight local canonical gates passed (220 tooling / 1,846 Product tests; two explicitly accounted skips), with source stable. Publication/CI/native PR review are tracked separately; T12.2c–e and all distributed-worker/independent-host gates remain incomplete.

### T12.2b CAS regression and local evidence

First actual fixture failed because EF inserted the unique resulting-version history row before the instance CAS, exposing a uniqueness exception instead of the expected concurrency conflict. The owner store now stages the instance/step CAS first, then history/custody on the same uncommitted caller root. The stale-version assertion was preserved; the exact bounded fixture rerun passed, including rollback/independent reader checks and cleanup. Original failed evidence remains private, followed by a successor repair receipt. No retry, authority, root-commit or cleanup scope was expanded. AUD/MSG integration and lost-COMMIT handling are still T12.2c, not supplied by this store alone.

## PR25-WFL-CUSTODY-001 — cancellation / initial custody serialization — 2026-11-01

Codex round1 on reviewed head `0ec233fa5a9e4c0d4e860dcf12eee93d8b710c1d` reported [the P2 custody race](https://github.com/NukeGeng/KORDRA/pull/25#discussion_r4434995618). A read-only tracked Running instance was not protected by the step CAS. Initial custody now reads current instance state/hash with a parameterized `SELECT ... FOR UPDATE`, using only the closed, quoted owner schema, before any step/custody read or write. This bypasses stale EF identity-map snapshots and holds the instance lock through the caller root; no authority, retry policy or commit boundary changed.

The actual isolated PostgreSQL fixture reproduced acceptance after another root committed Cancelled. The fixed fixture passed both orders: cancellation-first rejects even with a stale Running row tracked; claim-first blocks cancellation with exact PostgreSQL lock-timeout evidence, and independent readers verify Running + one custody versus Cancelled + zero custody. Original assertions remain before additional synthetic instances; final aggregate counts cover the additional cases. Native exception wrappers and fixture-oracle corrections are recorded separately from production changes. All runs retained original failures and verified cleanup of the same approved UUID schema. General CI keeps this operation-gated test skipped without explicit flags; actual execution evidence is private. Publication/current-head CI and follow-up native review remain separate gates. ADR-0021 and accepted contracts are unchanged.

## T12.2c — inactive owner-root composition, qualified local slice — 2026-11-01

The Host composes actual WFL, AUD and MSG on one caller-owned Npgsql root through an isolated-test-only factory. It adds no production route/consumer, authority bypass or worker registration. WFL Domain/Contracts stay BCL-only; only Host receives the approved owner/native dependencies. The PE/XML graph tests enforce these exact Host edges and retain owner/domain isolation. Dependent test lock files reflect the existing pinned graph without resolved-version upgrades.

The root binds one workflow installation/tenant/instance and owner operation. A transition's paired publication also matches its exact step. Old external publication receipts cannot establish a new paired owner mutation. Root operations reject concurrent calls; a server transaction generation has only one coordinator, while a later real root remains eligible despite Npgsql wrapper reuse. Received native contexts accept only their self/default binding or no binding before enlistment; a foreign owner enlistment is rejected. MSG destinations follow the native transport sanitizer. Native metadata is configured before offline SQL export, with queue/scheduled objects required; schema generation does not start hosts or open database connections.

Only a new confirmed root can flush and mark its received envelope Handled in memory. A no-commit-attempt outcome, including AUD Durable replay after rollback, never proves this WFL/MSG root committed. Actual attempted Unknown closes shared admission and prevents flush. Independently observed receipts are scoped facts, not replay/authority/custody permission. The WFL reader compares canonical specification, original request or transition/version, operation and hash. Missing evidence remains StillUnknown; mismatches conflict; unavailable observation cannot advance work.

The separately approved three-schema PostgreSQL fixture passed on current-source serial build: six staging-boundary rollback/cancellation cases (owner, AUD, floor, publication operation, post-publication, pre-commit), a reproduced-and-fixed duplicate coordinator, subsequent real-root eligibility, shared ten-envelope and160KiB limits across two MSG stagers on the same root, producer rollback/confirmed commit, publication hash conflict, older receipt refusal, actual lost COMMIT reply with ended backend and independent WFL/AUD/MSG reconciliation, AUD-only replay without commit, and real native receiver with provisional effects invisible before commit and logical receipt/inbox Handled afterward. Cleanup verified all three namespaces absent. Original failed execution/build evidence and frozen SQL/source-only repair successors remain private; corrected native queue SQL received exact successor approval. No role/HBA/superuser, production database, Keychain or provider operation was added.

The requested-v1 DTO/codec has closed canonical fields and explicit timer null rules, independent golden bytes/hash and negative tests. Production transition-recorded payload/catalog integration is part of the remaining owner/worker path, not supplied by the synthetic transaction fixture. The fixture registry has no business authority and default ingress stays disabled. Native terminal observation here does not prove successor restart/takeover: those WF04/WF05 portions, timer/cancellation/reconciliation/retention and scoped owner commands remain T12.2d/e; distributed authority/worker and independent-host qualification remain T12.3/12.4. This qualified C slice does not complete FEAT-WFL-001 or T12.

## T12.2d initial waiting-timer increment — qualified local owner scope, 2026-11-01

MOD-WFL now has BCL-only timer scheduling input/result facts, pure bounded due-wake/attempt rules, caller-root waiting-timer staging and read-only scoped due discovery. Actor remains the future admitted scheduler/operator, exact installation/tenant/workflow and workflow permission pairs; these data types confer no authority. No owner/worker route or consumer is registered. Source/test ownership stays the existing WFL Domain/Contracts/Infrastructure and WorkflowOrchestration.Tests mapping.

Staging locks and freshly checks the instance, canonical WaitUntil step, successful earlier steps, immutable specification/due time and exact worker/boot/epoch/operation custody. Instance/step/custody CAS, initial timer and append-only transition share the caller root. Same-operation duplicates return the original timer reference/version; changed identity/hash/operation/custody or original workflow version rejects. The port never commits on its own. Timer identity/due time cannot be rewritten, and persisted attempts cannot reset, skip or advance beyond10. The existing nine-table revision1 model/SQL remains unchanged.

A timer may span more than120s within the original24h workflow; a newly admitted due wake is≤120s and expires at least one tick before the unchanged workflow deadline. Early/expired/clock-before-creation windows reject. Due discovery is metadata-only, exact scoped, ordered and bounded1–100; it grants no dispatch or takeover eligibility and does not renew a deadline or mutate a timer.

Validation:26 targeted timing/guard/offline-SQL cases passed; the WFL suite passed99 tests with two explicitly gated DB cases skipped in its ordinary run. After separate exact D operation approval, the actual PostgreSQL timer case passed on its first execution in about4seconds: provisional rollback invisibility, commit/original duplicate, wrong tenant/hash/operation/custody, step ordering, stale CAS and stale tracked Running after cancellation, then bounded due lookup on102 synthetic schedules. Cleanup asserted the approved UUID namespace absent. Only that isolated kordra_test schema/locator was used; no role/HBA/superuser/product database/Keychain/native host/process restart or provider operation occurred. Frozen SQL/source/binary and execution/cleanup evidence stay private.

This is the initial waiting-timer owner increment, not full T12.2d or the entire WF06 matrix. Paired native wake dispatch, successor generations/reconciliation, clock/floor/budget and cancellation/retention/process-restart qualification remain D work; scoped owner commands follow in e. Actual AUD/MSG integration for the new timer port must qualify before activation. Current authority/independent worker and off-Mac qualification remain12.3/12.4. Original design approval remains valid; new operational resources require separate exact approval.
