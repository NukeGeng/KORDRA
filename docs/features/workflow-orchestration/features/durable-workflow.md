---
id: FEAT-WFL-001
status: proposed
owner: unassigned
last_reviewed: 2026-11-12
module_id: MOD-WFL
feature_id: FEAT-WFL-001
phase: MVP
source_paths: ["src/BuildingBlocks/Security/FairAsyncReaderWriterAdmissionGate.cs", "src/Modules/WorkflowOrchestration/Domain/", "src/Modules/WorkflowOrchestration/Contracts/", "src/Modules/WorkflowOrchestration/Infrastructure/", "src/Host/Kordra.Api/Composition/Workflows/", "src/Host/Kordra.Api/Kordra.Api.csproj", "src/Host/Kordra.Api/packages.lock.json", "Kordra.slnx"]
test_paths: ["tests/WorkflowOrchestration.Tests/WorkflowModelFixtureSelectionTests.cs", "tests/WorkflowOrchestration.Tests/WorkflowModelFixture.cs", "tests/InfrastructureInventory.Bootstrap.Tests/IsolatedWorkflowFixtureTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/IsolatedWorkflowFixture.cs", "tests/InfrastructureInventory.Bootstrap.Tests/ProductPostgreSqlFixtureSelectionTests.cs", "tests/Shared/ProductPostgreSqlFixtureSelection.cs", "tests/Shared/RegressionPostgreSqlSelection.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposableArtifactOwnerTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposableWorkflowMetadataTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposableRecoveryReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposableWorkflowMessagingTargetTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposableAuditAdapterTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposableDatabaseFactoryTests.cs", "tests/Shared/DisposablePostgreSqlProfile.cs", "tests/InfrastructureInventory.Bootstrap.Tests/DisposablePostgreSqlProfileTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowMetadataCommandFaultDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowAdmittedStartFaultDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowAdmittedStartDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowStartOwnerFixture.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowStartAdmissionReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowReconciliationRollbackDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowReconciliationOriginDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowOperatorAuthorityFixture.cs", "tests/IdentityAccess.Tests/WorkflowPersistedAuthorityDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowOperatorRequestParserTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowReconciliationTransactionTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowReconciliationDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowActionPolicyReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowStartRootDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowStartRequestParserTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowStartEligibilityReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowCallerBoundResponseCommitterTests.cs", "tests/WorkflowOrchestration.Tests/", "tests/InfrastructureInventory.Bootstrap.Tests/ArchitectureTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowCommitClassificationTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowTransitionCodecTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakePublicationTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowTransitionRecordedTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeFixture.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeLifecycleDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeRecoveryReaderTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowTransactionProbeTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowCustodyProcessTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeSuccessorDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowProcessCrashTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeFaultDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowWakeReceiveFaultDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowCancellationDatabaseTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/Fixtures/workflow-transition-recorded-v1.json", "tests/InfrastructureInventory.Bootstrap.Tests/Fixtures/workflow-transition-recorded-v1.sha256", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowTransactionSchemaTests.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowRootDatabaseTests.cs", "tests/Messaging.Tests/CommitReplyFault.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowFixtureIncomingHandler.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowFixtureWire.cs", "tests/InfrastructureInventory.Bootstrap.Tests/WorkflowFixtureProgram.cs", "tests/InfrastructureInventory.Bootstrap.Tests/Internal/Generated/", "tests/InfrastructureInventory.Bootstrap.Tests/InfrastructureInventory.Bootstrap.Tests.csproj", "tests/InfrastructureInventory.Bootstrap.Tests/Fixtures/workflow-transition-requested-v1.json", "tests/InfrastructureInventory.Bootstrap.Tests/Fixtures/workflow-transition-requested-v1.sha256", "tests/InfrastructureInventory.Bootstrap.Tests/packages.lock.json"]
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

## PR27-WFL-TIMESTAMP-001 — provider timestamp precision

The original timer port compared reloaded PostgreSQL microsecond timestamps directly with seven-fractional-digit canonical values, rejecting valid sub-microsecond creation/deadline and duplicate due timestamps. The real DB regression reproduced that rejection before the fix. Parent date equality and duplicate due equality now execute as parameterized PostgreSQL expressions, using the same provider precision as the original write. Canonical specification bytes/hash are never truncated or rewritten, and no tolerance is introduced.

The same approved fixture SQL/resources now cover fractional creation/deadline/due values, before/after the PostgreSQL epoch, original timer duplicate identity and byte/hash preservation. Adjacent persisted microseconds for each scalar binding still reject, including values less than a microsecond away from the canonical value. The repaired actual fixture passed and verified namespace cleanup; the initial failure is retained privately. Workflow instances stay within the original128 cap (110 synthetic instances,106 timers). Existing whole-second/order/scope/custody/CAS/bounded-discovery assertions remain. No schema, permission, native host or accepted ADR changed. Independent current-head Codex follow-up is still a separate gate; D2 work is retained unfinished outside this fix.

## Native wake binding work in progress

The next D increment first implements a BCL-only immutable scheduler/wake descriptor, owned by MOD-WFL under ADR-0021. It binds installation/tenant/instance, exact step/timer/generation/attempt and custody epoch, original request correlation, distinct producer-scheduler and delivered business-operation IDs, delivery ID, independent workflow/step versions, immutable specification/template hashes and original occurrence/deadline. It is stored/validated data, never authority or permission to increment a generation/replay. Both logical operations must remain distinct because WFL transition history uses a unique operation key. Historical descriptor reading must preserve original bytes/hash/deadline, with no clock refresh. Required pure proof: independent canonical bytes/hash, scope/spec substitution rejection, duplicate/unknown/authority fields and malformed/bounded data rejection,120s/original-workflow-deadline boundaries and no mutable data exposure. Existing Domain/tests ownership paths cover this source. Native publication, additive persistence/recovery and any new operational fixture remain pending; no production activation or new DB rights are claimed.

Host mapping of this descriptor to the existing closed `workflow.transition-requested.v1` registration must preserve wake operation/delivery, resource/scope, step/workflow versions, TimerDue generation, epoch, occurrence/deadline, correlation and causation. Effective-now is validation only: it must never refresh those frozen fields. The explicit compiled request registry is a factory, not production DI/consumer activation; caller-supplied consumers still require the later authority gates. Wrong installation/tenant/type/version/resource/binding or expired wire must reject under MSG v1; neither descriptor nor registry creation performs IO.

The explicit expanded offline/fixture model adds append-only `wake_operations` under revision2, with a scoped timer-generation FK and unique scheduler/wake/message identities. The original revision1 factory, nine-table SQL/manifest and previous fixture SQL remain unchanged. The separate additive artifact is generated by EF model differ and verified to contain only the new table/indexes; it is not applied automatically. This model and the wire factory do not yet implement dispatch CAS, paired commit/recovery or an activated worker. Pure validation so far:30 descriptor tests,24 Host wake/codec tests and existing persistence-model coverage passed. Expanded actual database/transaction qualification remains pending and requires its own operation approval.

## Initial native timer dispatch — implementation pending actual fixture

The inactive expanded fixture-only root now stages the first scheduler operation with instance/timer CAS, immutable wake bytes, exact settled predecessor history/custody and a paired MSG publication. Workflow remains WaitingTimer while its version advances; the delivered wake carries that resulting version and the unchanged independent step version. Scheduler and delivered operation IDs remain distinct through the typed binding; generic publication checks are unchanged. Missing AUD/floor, stage failure or duplicate owner result cannot flush a new wake. The timer floor must use the exact admitted scheduler instant. Initial generation/attempt/epoch only; this port rejects successors and does not infer recovery or authority from metadata.

The new compiled database fixture proposes one fresh UUID schema with revision1 seed followed by additive revision2, real WFL/AUD/MSG commit/rollback and native outgoing-envelope inspection. It includes wrong tenant/version, cancellation, unresolved custody, changed duplicate delivery and pre-commit rollback checks. It is not executed without exact operation approval; it does not test a successor worker or Mac-close survival. No existing approved fixture is replayed or silently expanded. Current targeted compilation/pure tests and SQL export are separate from actual database qualification and final canonical gates.

## D2 initial root actual result and recovery continuation

The exact approved D2 initial-root fixture passed on its first execution, including revision1 seed/additive upgrade, independent pre-commit invisibility, missing AUD/floor and explicit rollback, wrong tenant/versions/cancellation/unsettled custody, native outgoing envelope identity/hash, original duplicate/no republish, and verified namespace cleanup. This qualifies the frozen source/binary in the operation receipt only; no successor/restart or full D claim. Subsequent work adds a read-only exact wake receipt inspector; its owner observation must still be combined with independent AUD/MSG and terminal-backend evidence before any lifecycle decision. Absence remains StillUnknown, never rollback/replay permission.

## TimerDue receive path — implementation in progress

The next inactive owner/Host path completes the pure WaitUntil step only from a freshly claimed native logical input. Closed delivery facts must match the retained descriptor, original message/correlation/causation/times, independent versions and exact timer generation. Under the instance lock it requires current WaitingTimer state, settled scheduler history/custody and no unresolved predecessor; CAS advances workflow/timer and transitions WaitUntil through Running to Succeeded within the same uncommitted root. AUD/floor/paired publication/Handled and one confirmed commit remain mandatory. Cancellation/stale versions/expired original wake reject rather than refresh. This source is compiled/pure-tested preparation, not actual receiver or lifecycle qualification; production consumers remain disabled.

## TimerDue paired-result and native fixture preparation

The first compiled transition-recorded v1 tuple is result Completed / workflow state Running / reason TimerDueElapsed, with fixed field order instanceId, stepId, operationId, resultingVersion, specHash, result, state, reason. It reports only pure WaitUntil completion; other tuples are not accepted or activated. The typed factory preserves the original wake window and operation, uses the completed step version and request/input-message correlation, and the root verifies its exact payload/envelope before paired publication. Independent golden/hash and negative tests cover malformed bindings, uncompiled outcomes and transport-valid result substitution. The test-only native receiver/profile and both static handlers have metadata code generation; this does not start a host/listener or qualify actual receive/restart. Production registration remains disabled; the next consolidated lifecycle fixture retains its own operation gate.

## Native receiver lifecycle qualification prepared

The next operation-gated fixture compiles a fresh UUID schema, synthetic waiting timer and real native queue/result flow. It proposes sequential producer and receiver hosts (never two active), independent pre-commit visibility checks, exact result/owner/MSG observations and identical-envelope redelivery with a one-handler/one-transition/AUD/result oracle. Metadata-only SQL export is verified without connections/listeners. Actual execution is pending a new exact operation approval; the prior producer-only case is not expanded or replayed. This case proves neither separate OS process recovery nor successor/unknown-commit/off-Mac behavior.

## D2 native receive/redelivery actual result

The newly approved native receive/redelivery fixture passed on its first actual execution in about6seconds and verified schema cleanup. Independent pre-commit reads saw no receiver WFL transition, logical receipt, AUD/result or Handled disposition. After commit, the result envelope, owner scheduler observation and MSG receipts matched; re-delivering the original physical envelope left HandlerCalls=1 and exactly one transition/AUD/result. Producer and receiver host objects ran sequentially in one test process; no independent OS-process crash, successor generation or Mac-close proof is claimed. The private exact proposal/approval/184-input manifest and execution receipt retain this boundary.

## Combined recovery observations and clock clarification

The new inactive recovery reader composes exact WFL scheduler/completion observations, MSG publication/logical outcome and canonical AUD evidence without owner writes. It validates original delivery/hash/resource/timer/versions, distinct scheduler/completion audit intents and original result message ID; conflicting evidence is Conflict, absent evidence stays StillUnknown, inaccessible evidence stays Unavailable. MatchingObserved is not a new commit receipt, predecessor-terminal proof, permission, successor/replay decision or production activation. Seven no-IO binding/closed-reader tests pass; actual combined-reader qualification remains in the next process-fault/reconciliation batch, not the earlier frozen native binary.

Clock clarification resolved by NukeGeng on2026-11-02: “Chốt theo mẫu thời gian hiện thời; floor không lùi”. The one-second comparator uses contemporaneous DB/UTC/monotonic samples; persisted floor is a nondecreasing lower bound, not an idle-age limit. The exact owner reply is retained privately; no design reset or infrastructure permission is inferred.

## Clock/budget source and observation correction

WorkflowTrustedClock implements the clarified owner baseline: nondecreasing effective UTC, latched clock fault, monotonic budgets, DB rollback/current-sample disagreement and startup floor guards. StageCheckedTimerClockFloorAsync binds the budget to the exact staged descriptor/admitted instant, samples clock_timestamp() on the same root and checks the budget again before attempting commit. A pre-commit budget timeout rolls back as NoCommitAttempt without poisoning admission; a clock trust fault closes admission. The primitive fixture floor port remains for historical compatibility and does not by itself qualify the new checked clock path. Current pure clock tests pass17; actual SQL/fault/process qualification of this newer source remains pending.

D2-WFL-OBS-001: an older parent version with a missing expected operation receipt now remains StillUnknown. If the receipt becomes visible between READ COMMITTED statements, the parent version is refreshed before classifying inconsistency. The lifecycle fixture adds a pre-commit assertion for this case; it is compiled for the next approved fault batch, not claimed from the earlier frozen native pass.

## Exact caller-root provenance/probe — source preparation

The inactive root can capture an opaque timer-operation origin from its actual staged Npgsql connection/transaction: full64bit transaction ID, backend PID/start, server start and exact owner operation/descriptor binding. Public callers cannot construct the origin from a reported PID/XID. The independent probe uses PostgreSQL transaction status plus prepared-transaction and server-generation checks; missing/aged-out status, changed server, closed/same-backend reader or unavailable data cannot become known rollback. Unknown backend-presence data stays null. This is one-root observation only: even Aborted/Committed does not prove all native attempts quiesced, complete owner/AUD/MSG reconciliation, worker lock loss or permission for a successor.

This first origin is in-process and has no caller-controlled import/rehydration; cross-process provenance/qualification remains pending. Classification and public-surface tests compile without DB access. Actual capture/probe, lost COMMIT reply, checked-clock and restart cases belong to the next exact fault operation manifest; earlier native fixtures are not replayed or claimed to cover this newer source.

### D2 consolidated transaction/clock fault fixture prepared — 2026-11-02

`WorkflowWakeFaultDatabaseTests` prepares a new UUID-only fixture for an actual staged root: in-progress/confirmed rollback observations, checked DB clock plus synthetic monotonic budget exhaustion before COMMIT, and loss of the actual COMMIT reply with independent WFL/MSG/AUD observation. It asserts precommit timeout preserves admission, unknown COMMIT closes it, and observation never reopens admission or authorizes retry. One producer-only native host, one synthetic workflow; no listener, OS clock mutation or worker-process restart. SQL metadata export does not open a connection or start a host. Actual DB execution requires its own exact immutable operation approval under ADR-0021; earlier root/lifecycle approvals do not cover this new UUID or changed binaries.

### D2 fault attempt01 reconciliation and bounded repair — 2026-11-03

The approved frozen fault attempt01 failed before COMMIT: its test supplied a second UTC sample for the floor instead of the exact admitted scheduler instant. The fixture now retains/reuses that instant; the runtime exact-instant guard is preserved. Its finally cleanup completed; an independent read-only catalog check confirmed the UUID schema absent. No automatic replay occurred.

`D2-WFL-OBS-002`: scheduler receipt inspection also compared an older parent version before checking for the expected wake descriptor. It now keeps a missing descriptor as StillUnknown and refreshes the parent version only when the descriptor is observed across READ COMMITTED statements, matching the general receipt-reader rule. The prepared fault test asserts this before commit/after rollback and matching owner/MSG/AUD observations after commit. Those assertions still require new frozen binary qualification; attempt01 did not reach them. Original approval/proposal/failure/cleanup receipts remain unchanged.

### D2 actual root/clock/commit-response qualification — 2026-11-03

Owner explicitly approved delta v2. Frozen attempt02 passed all assertions against one synthetic workflow/schema and one producer-only native host: actual root provenance is Unresolved while active, confirmed rollback is Aborted, and the independent scheduler/WFL–MSG–AUD read remains StillUnknown before commit/after rollback. A synthetic monotonic budget expires before COMMIT with no owner/audit/publication effect and admission stays open; actual DB-time checked floor uses the exact admitted timestamp. Dropping the real COMMIT response returns Unknown, closes admission and forbids flush. An independent new connection sees the exact original transaction Committed and matching WFL/MSG/AUD observations; no replay or admission reopen occurs.

Host stop, fixture cleanup and independent read-only namespace absence passed. Receipt/log/cleanup observation are privately retained as `t12-2d-wake-fault-attempt-02*`, bound to v2 proposal SHA `786951493c7f9b0ec12fec46cc18e07db5a621366d5adab131384c24d77d4e59`. Failed attempt01 remains unchanged. This qualifies the current in-process producer fault path and D2-WFL-OBS-002 only: no receiver commit-response loss, OS-process crash/restart, successor generation, current worker authority or full D2 completion is claimed.

### D2 receiver COMMIT-response fault prepared — 2026-11-03

`WorkflowWakeReceiveFaultDatabaseTests` prepares the next uncovered native consumer case, before recovery/cancellation can rely on receiver outcomes. One new UUID schema/workflow; producer host stops before receiver host/listener starts. The fixture-only precommit hook obtains opaque provenance from the actual fully staged receiver root, independently checks precommit invisibility, and drops that connection's actual COMMIT reply. A separate root-outcome callback observes the real disposition; neither hook is wire-controlled or registered in Product. Existing successful handler behavior stays unchanged when hooks are absent.

The new case requires Unknown/closed admission, independent original transaction Committed and backend absence after host stop, exact WFL/logical/AUD/result receipts, retained Handled inbox and the original native result ID/hash (allowing an identical outbox/queue copy during send/ACK overlap), with one handler/effect and no retry/reopen. It does not qualify consumer failure-policy adoption, OS-process restart/quiescence or successor authority; the test-only discard policy is not a production Unknown policy. New SQL export/build is no-IO; separate exact operation approval is required before native execution. Earlier producer/lifecycle receipts retain their original artifact boundary.

### D2 actual receiver COMMIT-response loss passed — 2026-11-03

Explicit owner approval authorized the exact receiver manifest (SHA `31574998cebee150b0128a9896e1ee14dc12c16113dd9371d01403d181a08803`). Actual attempt01 passed 1/1, no skips: native receiver precommit completion stays StillUnknown/invisible; real COMMIT-response loss yields Unknown and closed admission, while independent original transaction/owner/logical/AUD/result/Handled-inbox observations match after the owned receiver stops. One handler/transition/effect and exact original native result ID/hash were asserted; no replay or admission reopen. Producer and receiver hosts ran sequentially, never simultaneously.

Cleanup assertions and independent read-only namespace absence passed; private attempt01 receipt/log/cleanup observation retain the exact artifact binding. This supersedes only the preceding pending-execution label for this receiver case. It does not adopt the fixture discard policy for production, prove OS-process restart/quiescence, or finish D2 successor/cancellation/recovery-horizon/retention. The earlier full-gate receipt remains historical for its original source; current complete gates are required at D2/PR completion, not inferred from this single case.

### D2 durable cancellation intent and owner guards — implementation pending DB qualification

`WorkflowCancellationIntent` binds installation/tenant/instance/request/actor, original specification hash and expected version into a closed cancellation command hash (`OperatorRequested` v1 reason); the server receipt time is deliberately outside the idempotent command content. This object confers no permission. `WorkflowCancellationStore` requires the opt-in revision3 clean caller root, locks/reloads the current instance, checks canonical scope/spec/time and exact request binding, then stages one sticky append-only intent and parent CAS. A matching duplicate returns the original result without another mutation; changed actor/content/version or replacement intent conflicts.

No state-graph edge is added: Running/WaitingTimer/BlockedAuthority use the existing CancelRequested edge; Pending/Reconciling retain their accepted state plus the durable intent (and preserve Unknown steps). Intent is not terminal cancellation or undo. Fresh transition/custody/waiting/wake owner paths check the intent while holding the instance lock; Cancelled cannot be staged while owner custody/Unknown step/published timer remains unresolved. Historical duplicates remain original observations, not new publications.

Revision3 adds only cancellation_intents, with scoped FK, unique request/instance binding, immutable command/result-version checks and append-only EF guards. Offline SQL/manifest keeps revisions1/2 unchanged; no automatic migration or production registration. Legacy model factories are historical fixture profiles, not cancellation-capable writers. Before positive cancellation activation, all relevant composition must use revision3 with current authority/AUD/floor and later SQL ACL/writer coverage. The new guarded cancellation fixture prepares additive-on-populated-schema, WFL/AUD/floor rollback/commit, duplicate/rebinding/terminal denial, stale tracked-state race and waiting/reconciling cases; no DB operation has yet been run for it. Recovery/successor/retention and complete D2/E remain pending.

On 2026-11-05 the revision 3 cancellation owner fixture passed its PostgreSQL qualification. The staged intent and AUD/floor commit together, and a competing stale start is denied by the persisted intent. Independent cleanup verification found zero matching namespaces. Recovery, successor and retention remain open in D2.

The isolated recovery reader now correlates exact scheduler or receiver origin with WFL, MSG and AUD receipts after the original backend exits. It keeps incomplete evidence unresolved and rejects a matching receipt attributed to an aborted root. The updated PostgreSQL assertions are pending qualification.

The owner creation path now rejects historical requests with missing receipts after their original deadline. Matching receipts preserve the original instance and version. Transition retries inspect history after acquiring the instance lock, and stale version conflicts retain the concurrency error expected by callers.

D2 adds a dedicated session custody implementation for the isolated worker profile. The process fixture tests contention while its owned predecessor is alive, acquisition after that child exits, and release of the successor key. The owner-approved process fixture passed on local PostgreSQL with one test and zero skips. It verified contention, owned child exit, successor admission and release through a fresh session.

The isolated revision 3 reader can observe an uncompleted wake after the exact receiver transaction aborts and its backend exits. It checks the retained waiting version, timer, scheduler history and custody, and rejects cancellation or any receiver history. The composition also checks the original scheduler receipts and absence of exact receiver MSG and AUD records. It returns an observation only. The root now captures the staged audit identity/hash and the exact result and consumed delivery identities/hashes. Reconciliation checks those facts before reading receipts. Old delivery settlement and current custody/owner CAS revalidation remain required before successor staging.

Expired-delivery metadata observation stays in MOD-MSG. Host composition continues to require independently qualified custody, old transaction and owner evidence before successor work. Architecture tests pass after keeping the PostgreSQL transport type out of the Host API.

The related MOD-MSG expired-delivery metadata reader has passed 21 PostgreSQL cases under exact operation approval. WFL still requires native delivery settlement, correlation to the original receiver transaction and owner-lock revalidation before successor generation.

Successor binding checks now require expiry of the predecessor window, the same original specification/step/timer, unchanged step version, exactly one increment in generation/attempt/custody epoch/workflow version, and fresh distinct logical and delivery IDs. Counter exhaustion and overflow reject. These structural checks do not supply reconciliation evidence or custody.

The inactive revision 3 successor store now stages the original generation retirement, new timer/wake binding, monotonic scheduler custody/history and immutable recovery reference on the caller root. Host composition obtains opaque evidence from exact terminal predecessor and native quarantine observations, rechecks those observations while the instance is locked, and pairs the new publication with owner/AUD/floor. Recorded duplicates use exact retained evidence and do not republish. A committed scheduler with expired uncompleted delivery and an aborted receiver are distinct supported predecessor observations. Native settlement and all in-flight owner effects must be quiesced; no authority is inferred from the evidence reference.

The first approved successor integration attempt timed out waiting for a native terminal delivery. Independent cleanup verified namespace removal. Its guarded fixture now uses the sanitized native destination and explicit unknown-message DLQ behavior. A shorter delivery-stage budget reports only pending row counts. The original failed attempt is retained; revised live qualification is pending.

The owner-approved successor v2 fixture passed on PostgreSQL and native transport: the original wake expired into DLQ, terminal scheduler provenance and owner/MSG observations reconciled under dedicated custody, stale timer and provisional cancellation denied, successor rollback left no history/publication, confirmed successor commit paired WFL/AUD/floor/MSG, and duplicate returned its original result without flush. Independent cleanup verified namespace and custody key absence. This qualifies the isolated committed-scheduler/no-completion path. Receiver-abort successor, OS-process crash boundaries and complete D2/E remain open.

The OS-process crash qualification fixture is prepared for four fixed isolated installations: producer before commit, producer after commit before flush, consumer after provisional logical claim before owner effect, and consumer after commit before flush/ACK. Each owns one child process, observes its exit and absence of its exact PostgreSQL application sessions, then checks independent durable state. Recovery uses the pinned native reassignment path and the original envelope. Synthetic handler hooks remain in the test assembly. Actual process execution requires its separate operation manifest.

The first approved WF04 batch reached producer-after-commit recovery and timed out. It retained the original failure; all four namespaces, exact advisory keys and child application sessions were independently absent after teardown. The fixture recovery profile had kept a one-minute first scheduled-recovery delay despite a sixty-second case budget. Its revised test-only profile starts bounded polling after one second. Actual rerun qualification remains pending.

The exact owner-approved WF04 v2 batch passed all four actual-process cases. The test killed only its owned children at producer-before-commit, producer-after-commit-before-flush, consumer-before-effect and consumer-after-commit-before-ACK. PostgreSQL sessions from each old child ended before recovery. Uncommitted intent rolled back; committed intent and pending input recovered once through native transport, with one logical completion/audit/result. Recovery after receiver commit made zero handler calls. Independent cleanup verified all four namespaces, advisory keys and child application sessions absent.

Terminal owner staging now preserves unresolved published timers, unsettled custody and Unknown steps. The receiver-abort successor branch supports exact Handled transport evidence with a matching expired journal and independently observed aborted receiver root. The integrated regression fixture builds and awaits live execution.

Both PostgreSQL successor scenarios passed, including actual precommit receiver rollback followed by Handled transport settlement. Independent cleanup verified exact namespace and custody key absence. The T12.2e metadata reader binds installation, tenant, instance and the exact owner project/environment and exposes bounded step/timer/history data without specification bytes, internal receipt identities or transport fields. IAM supplies separate current eligibility facts. Host command policy, U/D handoff and live projection qualification remain pending.

### T12.2e inactive metadata read composition

The local authenticated actor requires an exact environment `workflow.read` grant and current workflow policy allow evidence. The Host holds the shared reader gate, commits U before reading WFL, checks the bounded page again before committing D, and binds disclosure to the exact response bytes, session, scope and cursor. Missing owner or policy authority denies without disclosing metadata. The composition and HTTP handoff have no runtime registration. A concrete workflow policy owner binding, database-backed U/D qualification and the other owner commands remain open.

### T12.2e cancellation owner root

The exact environment `workflow.cancel` actor may record a sticky intent after current admission. The inactive transaction path verifies project/environment against the locked canonical workflow, binds mutation audit to the actor and command hash, and stages the audit and clock floor with the owner CAS. Existing handler roots still require their paired publication. The local guards run without a database. The updated scoped DB and successor rollback scenarios remain subject to their exact fixture approvals, and current policy and response disclosure still gate activation.

### T12.2e cancellation transport isolation

The cancellation-only PostgreSQL root omits native message enlistment. Fixture tests target the actual Host path for rollback, commit and duplicate handling. Local preflight tests exercise audit substitution before connection use. Runtime policy and U/D response admission remain open.

### T12.2e start eligibility

The inactive start eligibility reader composes the current local actor's exact environment workflow.start and project release.read grants. It matches the owner-selected approval to the exact frozen release ID/revision/hash and owner environment, requires current Granted eligibility, and accepts only Development/Test and the compiled checkpoint template. No mutation or execution permission is issued. A start command still requires workflow policy, durable creation/delegation and audited disclosure.

### T12.2e scoped timer reconciliation evidence

The inactive timer receiver reader accepts a closed decision selection and separately resolved typed owner evidence. It checks tenant, project, environment, instance, operation, expected version and evidence digest before entering fixture custody. Matching commit requires terminal origin plus matching WFL/MSG/AUD receipts. Known rollback requires a disconnected aborted origin and exact absence of completion effects. The version check binds the selected historical effect. A future decision writer must independently compare the current owner version under its root. Current workflow.reconcile policy, durable decision append and audited disclosure still gate activation.

### T12.2e cancellation transaction fixture

The updated Host cancellation transaction is compiled into a dedicated fixture with a fresh namespace and no native host. The review bundle contains exact schema creation, expansion and cleanup SQL. Local preflight checks use no database. The fixture requires separate approval tied to the new executable and SQL before it can qualify commit, rollback and duplicate behavior.

### T12.2e policy binding decision pending

[NEEDS-DECISION] [ADR-0024](../../../decisions/0024-workflow-action-policy.md) proposes the concrete IAM/PRJ policy binding for the four accepted environment actions. The existing target policy remains action-specific. The workflow command remains inactive while the binding semantics await approval.

### T12.2e start retry fingerprint

The start command identity separates caller scope and key from immutable business content under the already approved start idempotency contract. Pure tests cover stable retry, changed business content and tenant/actor separation. The persisted start receipt and authorization remain pending.

### T12.2e durable start receipt staging

WorkflowStartStore implements key lookup before instance allocation and stages the canonical workflow and start receipt on the same owner transaction. Cross-binding conflicts fail closed. Revision 4 is an additive opt-in model with unchanged legacy models. Local tests verify the SQL artifact and root guards. Live transaction qualification and Host policy, delegation and audited response remain open.

### T12.2e start receipt fixture

An isolated PostgreSQL case exercises start receipt rollback, exact duplicate and changed content. A concurrent request uses the same scoped key but a different request ID and must lose at the receipt key. The test confirms no partial workflow survives. SQL export runs offline. Live execution remains gated by an exact operation manifest.

### T12.2e start request parsing and persistence evidence

The inactive Host parser bounds and decodes the start body and exact single idempotency header, and rejects extra authority fields. The approved PostgreSQL start receipt fixture passed rollback, same-key concurrent uniqueness, and retry against the original receipt. Independent cleanup verified namespace absence. This qualifies owner persistence, with Host admission/delegation and disclosure still open.

### T12.2e isolated start Host root

The fixture-only Host root composes the revision 4 owner store with the existing WFL/AUD/MSG transaction. Its callback must revalidate the admitted scope, policy and authority. The root checks exact audit and initial checkpoint bindings and rolls back duplicate roots without republication. Actual paired transaction qualification and IAM delegation remain open.

### T12.2e paired start root fixture

The new operation-gated fixture compiles actual WFL/AUD/MSG start, audit substitution, publication mismatch, authority loss before commit and original duplicate. One producer host has no listener. Reviewed SQL export runs without connections. Actual execution requires its own immutable operation approval.

### T12.2e paired start root qualification — 2026-11-07

The exact owner-approved isolated PostgreSQL/native producer fixture passed the new start root. Substituted mutation actor, wrong checkpoint specification hash and revoked precommit admission were rejected. Commit and duplicate cases retained one workflow, request receipt, start receipt, audit intent and publication receipt. The producer was stopped and the fixture namespace removed; a separate read-only connection confirmed absence. This evidence qualifies the isolated root only. Runtime policy, IAM delegation and protected response qualification remain open.

### ADR-0024 acceptance — 2026-11-07

The owner approved the exact workflow action policy proposal. This supersedes the pending-decision status above for ADR-0024. IAM Contracts implements the v2 payload and action-qualified evaluation. Persistence, offline binding and Host adoption still require validation.

### ADR-0024 concrete Host policy reader

The inactive Host policy reader resolves current PRJ environment/classification and exact-action binding, then checks IAM v2 policy head/hash and rules. Evidence binds scope, action, owner revisions, classification, policy content hash and binding revision. Observation time is outside the stable digest. The read coordinator rechecks the digest and retains the original expiry cap, so a later read cannot extend an in-flight invocation. No DI registration or route activation is included before U/D qualification.

### T12.2e isolated read/AUD fixture

Prepared an operation-gated read/AUD fixture with real owner projection and audit transactions, synthetic current authority, independent pre-handoff audit observation and a policy-change denial. SQL export is offline; actual execution is pending approval.

The exact approved read/AUD fixture passed the coordinator and projection on PostgreSQL. It verified durable U/D before handoff and denied disclosure after a synthetic policy revision change following U. Cleanup was independently confirmed. Real persisted authority-chain invalidation and runtime registration remain pending.

The read/AUD fixture has a new exact namespace for persisted IAM/PRJ action policy observation. It reuses the actual coordinator, current policy reader, owner stores and independent U/D handoff oracle. Prior synthetic-policy execution evidence remains historical. The new case requires operation approval before database access.

### Persisted policy read/AUD qualification

The exact approved PostgreSQL read fixture passed actual PRJ owner/classification/binding and IAM current policy reads with durable U/D. A new policy head published at the test-owned boundary after U invalidated the old binding and prevented handoff. The separate observer verified both audit intents before the successful payload handoff. Session/grant facts remained synthetic and writer-gate races were not exercised. Namespace cleanup was independently verified.

### Combined local command admission and cancellation preparation

WorkflowCommandAdmissionReader composes exact environment grant/session facts with current action policy and returns bounded immutable evidence. Revalidation compares actor/session, grant and owner revisions, exact action/scope and policy digest against the original cap. The isolated admitted cancellation entry acquires admission, binds actor to session, creates the mutation audit from current evidence and supplies mandatory rechecks to its existing owner transaction. No runtime registration is included. Live end-to-end cancellation qualification with persisted authority remains open.

### Reconciliation command identity

The WFL command now pins authenticated actor and the complete operator selection into an immutable command hash, separate from the owner evidence hash. Tests cover retries, actor/version/kind/evidence changes and invalid outcome selections. The existing recovery-decision rows are scheduler history and do not yet retain the full operator command receipt. Durable operator storage remains open.

## T12.2e operator receipt preparation — 2026-11-07

Implemented an opt-in revision 5 owner receipt store for the closed reconciliation command. The use case is recording an authenticated operator's evidence-bound decision for one tenant/project/environment/instance, under `workflow.reconcile`; the store itself grants no permission. Source remains in WFL Infrastructure persistence and the existing Domain command. Revision 5 additive SQL/hash manifest and `WorkflowOperatorDecisionStoreTests` cover unchanged prior schema, exact scope/operation uniqueness, caller-root failure before I/O and cancellation before work. Together with command/start/cancellation regressions, 33 targeted tests passed without skips. Database CAS/rollback/concurrent duplicate behavior and paired Host authorization/custody/AUD require further qualification; this slice is not activated and T12.2e remains incomplete. Accepted ADR-0021/0024 boundaries are unchanged.

### T12.2e operator receipt fixture qualification — 2026-11-07

The maintainer-approved frozen fixture for revision 5 passed on local `kordra_test`: owner CAS and receipt roll back together; commit persists version +1 and one receipt; retry returns the original; changed actor/hash, wrong tenant/project, denied evidence, stale expected version and repeated operation under another request fail without extra durable mutation. One live case passed without skip; its new UUID namespace was independently verified absent after cleanup. No runtime host or provider was started. This is owner-store qualification, not paired Host AUD/custody or concurrent-operator proof. The stable full gate run passed docs/scope/tooling/clients/restore/build/mapping, but product-test failed on 553 missing isolated-fixture locator/manifest configurations. Full completion remains blocked; targeted and live evidence do not override that gate.

## T12.2e paired reconciliation coordinator preparation — 2026-11-07

The inactive Host composition now binds the operator selection to a server-resolved canonical WFL specification and current session actor. `WorkflowReconciliationTransaction` holds admission, checks exact workflow.reconcile authority and action policy, retains the fixture custody lease through the caller root, and stages revision 5 owner receipt/CAS with AUD and clock floor on the same connection/transaction. Current authority and independently resolved original evidence are rechecked before commit; admission/custody are checked after those awaited callbacks, including duplicate receipt handoff. Duplicate receipt retrieval rolls the new root back without another AUD mutation; revoked authority or changed evidence rolls everything back. Unknown actual commit closes admission, while pre-commit failures do not. It never publishes, replays or activates a workflow, and introduces no public endpoint.

Tests: 40 targeted Host/recovery/classification/architecture cases passed; an additional offline SQL-export case passed. The prepared paired PostgreSQL test uses real WFL/AUD and a dedicated fixture advisory lease but synthetic authority/evidence callbacks; it cannot establish current real-session/grant validation, actual timer-origin evidence integration, distributed fencing or T12.3. Live execution needs its own frozen operation approval.

The paired fixture v2 also includes admission closing inside the final authority callback, which must roll back rather than commit. The first frozen proposal was not executed. Current-source targeted Host/export/recovery/classification/architecture checks passed 41 cases with zero skips; actual atomicity and late-close behavior await the separately approved fixture execution.

## T12.2e paired fixture qualification and operator DTOs — 2026-11-08

The exact approved paired reconciliation fixture v2 passed one live case with no skips. Real WFL receipt/CAS, AUD and floor committed together under the retained dedicated fixture lease. Late authority denial, changed synthetic evidence, initial evidence rejection and admission closing inside the final callback all prevented durable mutation. Duplicate returned its original receipt without another AUD. A separate read-only PostgreSQL connection confirmed both namespace absence and advisory-key release. This qualification is bound to its frozen executable; it does not claim actual session/grant or timer-origin integration, concurrent operator races, distributed fencing, or current-source live requalification after later builds.

`WorkflowOperatorRequestParser` now decodes inactive closed selections: cancellation body schemaVersion/requestId/reasonCode (only OperatorRequested), with a separately decoded positive nonoverflowing If-Match version; reconciliation body schemaVersion/requestId/operationId/expectedVersion/kind/evidenceHash, with only the two approved decision kinds. Route instance must match the server-resolved canonical specification; actor and tenant are never taken from payloads. Unknown/duplicate/missing fields, extra authority/replay/path fields, invalid kinds/hashes/versions, invalid route IDs and oversized bodies fail. No HTTP ETag syntax, endpoint registration, permission grant, business state override or replay is introduced. 56 targeted parser/admission/audit-guard/architecture tests passed, zero skips.

## T12.2e persisted session/grant qualification preparation — 2026-11-08

`WorkflowPersistedAuthorityDatabaseTests` prepares a separate isolated PostgreSQL case using actual `LocalAuthenticationService` session-reference/current-principal reads, `RoleDefinitionStore`, `TenantMembershipService`, PRJ owner/scope/binding readers and IAM resource policy reader. Synthetic principals, sessions, definitions and founding environment assignments are seeded only in a fresh UUID namespace; no real account/grant, login issuance, host, startup, Keychain or provider operation is implied. Synthetic invalidation updates exercise current reads, not production writer entry points or cross-process fencing.

The prepared matrix starts with four explicitly granted/bound workflow actions, then checks revoked/expired session, disabled principal, revoked/expired assignment, newer retired definition and wrong tenant. A valid session without membership/assignment cannot acquire workflow.cancel. Positive cancellation uses the actual admitted coordinator and same-root AUD/floor; read U/D uses persisted session/grant/policy facts and denies a new policy head published after U. Reconciliation timer-origin and full start/REL/APR integration remain separate. The test exports exact IAM/TEN/PRJ/WFL/AUD SQL without connecting; live execution awaits its own frozen manifest approval.

### Persisted-authority fixture seed correction — 2026-11-08

The first approved fixture attempt failed on the IAM required password_hash column while seeding the first synthetic principal, before current-authority assertions. Cleanup completed and an independent read-only connection verified namespace absence; failed evidence is preserved. The fixture now supplies an opaque synthetic password hash and explicit security/concurrency stamps required by the unchanged IAM model. An offline model-metadata regression checks every required principal field without database access. Fifty offline/export/authority/policy cases pass, zero skips. SQL/UUID/target/resource limits and scenarios remain unchanged; the changed executable awaits exact delta operation approval. This is a fixture correction, not a Product authentication change or evidence that the live authority test passed.

### Persisted-authority fixture v2 qualified — 2026-11-08

The exact owner-approved v2 executable passed one live test (5 seconds, no skips). It used real IAM current-session/principal and immutable-role readers, TEN current membership/grant resolution, PRJ owner/scope/binding and IAM policy readers. Each candidate initially admitted all four explicitly provisioned actions; current revoked/expired session, disabled principal, revoked/expired assignment and retired definition denied, and wrong tenant denied. An active session without membership/assignment could not acquire workflow.cancel. Actual admitted cancellation committed WFL intent with AUD/floor; actual persisted-authority read completed U/D, while a newer policy head published after U prevented disclosure. Final AUD counts matched the expected committed mutation and reads. Independent read-only cleanup confirmed the namespace absent. The failed v1 execution and its cleanup evidence are retained.

This verifies seeded synthetic persisted sessions and grants, not login issuance, enrollment, boot startup or real-account permission changes. Fixture invalidation updates are not production writer/fence evidence. The run did not execute a native host, provider or original timer reconciliation; REL/APR start and original-evidence reconcile integration plus T12.3/T12.4 remain open. The latest stable full gates passed docs/scope/tooling/clients/restore/build/mapping but product-test still failed with 553 missing isolated-fixture configuration errors. This targeted qualification does not override that failure or mark T12 complete.

## T12.2e original timer evidence + admitted reconciliation preparation — 2026-11-08

The new isolated `WorkflowReconciliationOriginDatabaseTests` and test-only `WorkflowOperatorAuthorityFixture` prepare actual current-session/principal, role, TEN grant and PRJ/IAM policy admission together with the original native timer transaction evidence. In one fresh schema the producer and receiver run sequentially; the owned receiver connection drops the actual COMMIT response. The original worker admission stays closed. Only after both hosts stop and the original backend is terminal does the operator acquire dedicated installation custody. The actual admitted coordinator resolves the original typed origin, exact WFL/MSG/AUD receipts, request/result identity and evidence hash before recording metadata. Wrong evidence, substituted result and a rollback claim against a committed origin must reject; the valid decision records once, duplicate adds no AUD/publication, and a revoked persisted session cannot retrieve the receipt. No worker restart, replay, provider effect or runtime activation is authorized by this preparation.

Offline export verifies all required IAM principal fields and exports exact IAM/TEN/PRJ/WFL revision5/AUD/MSG-owned/native SQL without starting a host or opening a connection. Forty-one targeted export/audit-guard/recovery/classification/architecture cases passed with zero skips. Actual host/COMMIT-loss/DB/custody qualification remains pending a separately frozen fixture operation approval. Seeded authority is synthetic persisted test data; login issuance/startup and T12.3 global fencing remain out of this fixture scope.

### Admitted original committed-timer reconciliation qualified — 2026-11-08

The frozen owner-approved origin fixture passed one live test in 3 seconds with zero skips. Native producer and receiver ran sequentially; the actual receiver COMMIT response was lost, the original root was independently observed committed with its backend disconnected, and both hosts stopped before administrative custody. Actual persisted-session/role/TEN-grant/PRJ-policy admission and the typed original WFL/MSG/AUD evidence authorized one metadata decision (owner version 6 to 7). Substituted evidence/result and a rollback claim against that committed origin rejected without mutation. Duplicate returned its original receipt without another audit or publication; revoking the persisted session then denied receipt retrieval. Original native handler count remained one, native publication receipts did not grow, and the original Unknown worker gate stayed closed. A separate read-only connection verified namespace absence and advisory-key release after cleanup.

This qualifies the BindMatchingCommittedReceipt path with original evidence and current seeded persisted authority. A positive ConfirmKnownRollback decision, full current REL/APR start/root proof, remaining command recovery/concurrency acceptance and T12.3/T12.4 are not established by this case. Login issuance/startup, production invalidating writers/global fencing and provider operations remain outside its scope. Forty-one offline/export/recovery/guard/classification/architecture cases passed. Stable full gates still fail product-test on 553 missing fixture configurations; targeted qualification does not override that gate or complete T12.

## T12.2e known-rollback original-evidence preparation — 2026-11-08

`WorkflowReconciliationRollbackDatabaseTests` prepares a new namespace and actual native receiver failure after staging but before COMMIT. It checks that unresolved/live original backend plus absent effects cannot prove rollback, then stops both fixture hosts, independently confirms Aborted plus backend absence and exact missing WFL/MSG/AUD completion effects. Actual persisted current-session/role/TEN-grant/PRJ-policy admission and retained installation custody must authorize only ConfirmKnownRollback. A false commit claim, substituted result/evidence, duplicate and post-revocation receipt access are covered. The administrative metadata CAS advances 5 to 6 without inventing a completion transition or publication, replaying the native operation or resuming a host. This is not proof of native settlement/successor permission or an OS process crash.

The test-only authority helper now takes a closed case identity so both cases reuse actual owner readers with distinct EF schema models. The committed and rollback fixture classes use the existing nonparallel Product test collection. Forty-two offline/export/recovery/audit-guard/classification/architecture tests pass without skips, including both schema exports and required-principal-field validation. Historical committed execution evidence is preserved; it has not been replayed or claimed as current binary qualification after this test-only refactor. New rollback host/DB/custody execution awaits its own frozen operation approval. No Product runtime dependency, API/consumer activation or ADR change follows.

### Actual admitted known-rollback original evidence qualified — 2026-11-08

The exact owner-approved rollback fixture passed one live test in 3 seconds with no skips. A synthetic receiver exception occurred after staging but before actual COMMIT; no receiver commit outcome was emitted. While the original backend was unresolved, the reader rejected an aborted-outcome claim. After both fixture hosts stopped, an independent reader confirmed Aborted/disconnected origin and exact absence of WFL/MSG/AUD completion effects. Actual persisted-session/role/TEN-grant/PRJ-policy admission plus dedicated custody then recorded ConfirmKnownRollback metadata once (parent version 5 to 6), without adding a completion transition, result publication or handler execution. A false committed claim and substituted evidence/result rejected; duplicate added no AUD/publication and persisted-session revocation denied receipt access. Independent read-only cleanup confirmed exact namespace absence and advisory-key release.

The 42 current offline/export/recovery/guard/classification/architecture cases also pass. This is metadata reconciliation of a known rolled-back owner root; it is not proof of native settlement, successor execution permission, OS-process crash handling or Product runtime activation. Full current REL/APR start, remaining command recovery/concurrency evidence, T12.3/T12.4 and applicable global gates remain required. No company/provider/Clients/Website operations, new dependency or ADR changes were introduced.

## T12.2e current start-admission composition — 2026-11-08

Implemented inactive Host `WorkflowStartAdmissionReader` and opaque `WorkflowStartAdmission`. It joins the existing exact REL/APR eligibility reader with environment-scoped workflow.start policy through the existing evaluator, using a start scope with requestId instead of inventing an instanceId before scoped-key lookup. Eligibility explicitly binds installation and exact release/template selection; the composed snapshot checks matching session/boot/principal revision, workflow.start environment grant, release.read project grant, current project/environment revisions, Development/Test classification, Granted-approval identity/version and release owner/app/target facts. Missing/unbound/inconsistent facts deny. Command binding preserves stable business/key hashes without allocating a specification; schedule and delegation are not chosen here.

Revalidation compares the original selection/invocation/request, actor/grants, approval/release/app/target/owner revisions and exact policy/binding digest, with the original minimum expiry cap retained. New observations cannot extend that cap. Existing read/cancel/reconcile policy consumers use the same shared evaluator and retain their instance-bound scope. The producer root now checks admission both before and after asynchronous start revalidation; a gate closing while that callback runs cannot authorize staging/COMMIT. This guard's actual database/COMMIT qualification remains part of the upcoming full-start fixture.

Seventy-seven targeted join/negative/cap/parser/eligibility/existing-policy/classification/architecture and offline root-export cases passed without skips. The new join tests intentionally use synthetic REL/APR eligibility and no database; they do not qualify real REL/APR persistence or a complete admitted start transaction. No API route, consumer, runtime registration, dependency, permission model, accepted ADR or DB operation changed.

### Retained admitted-start root binding — 2026-11-08

FEAT-WFL-001 / MOD-WFL implements inactive `WorkflowRootTransaction.StageAdmittedStartAsync`. One root operation guard spans current snapshot revalidation, lazy allocation and retaining the typed admission. It checks root installation/current session, derives the stable command, and binds a fresh spec to exact selected project/environment/release revision/hash plus admitted application/target/classification and creation window. The owner store also validates the canonical original against current owner binding on key retry without allocating a new spec or applying the fresh-creation clock window to historical data. It does not append a new mutation audit on duplicate.

`WorkflowStartAdmission.CreateMutationAudit` produces exact actor/resource/request/invocation/policy/assignment fields; the root rejects substituted invocation or policy/assignment revisions in addition to its existing command/spec audit checks. The existing generic isolated start seam remains for prior fixtures, with shared core logic and the same serial/root/Unknown semantics. No default WaitUntil schedule, delegation, API, consumer or new wire/persistence schema is introduced.

Current synthetic join/owner-binding/creation-window/audit-factory/parser/eligibility/policy/architecture/classification/offline-export tests pass 87 cases, no skips; owner-store/command regressions are run separately. These do not prove the new typed root against actual REL/APR/WFL/AUD/MSG on PostgreSQL. That integrated fixture, audit-substitution/late gate-close/duplicate concurrency and Unknown recovery qualification remain required. Existing frozen live receipts are historical and have not been replayed after source changes. ADR-0021/0024 remain unchanged.

### Integrated start qualification fixture prepared — 2026-11-08

FEAT-WFL-001 / MOD-WFL adds `WorkflowStartOwnerFixture` and `WorkflowAdmittedStartDatabaseTests` for a separately approved disposable `kordra_test` schema. The fixture seeds synthetic IAM session/principal, TEN membership and two scoped permission definitions, PRJ application/environment/target association, INV target, ART stored projection, canonical frozen REL and APR Granted request with real captured requester/approver evidence. Current eligibility is computed by the concrete owner readers, `ApprovalSubjectReader`, `ApprovalEligibilityReader` and `WorkflowStartAdmissionReader`; no fake REL/APR eligibility is supplied. A Test-only self-allowed approval policy is explicit fixture data, not a Product default or registry verification claim. No login issuance or real grant is performed.

The live test is prepared to exercise typed start with shared WFL/AUD/MSG producer root, substituted application/target/classification and invocation/policy/authority audit revisions, then a duplicate key whose specification factory must not execute. The native host has no listener. SQL export and policy parsing run offline without opening connections or reading credentials. Live execution remains unverified until the exact frozen manifest is authorized and executed with independent cleanup observation. This fixture is not production migration/ACL/startup qualification; late gate closure, start concurrency/Unknown recovery and remaining T12.2–T12.4 exits remain open. No accepted contract, wire schema, dependency or runtime registration changes.

### Integrated start fixture attempt 01 — 2026-11-08

The approved first batch failed during synthetic policy setup because the new helper had not enlisted the owner transaction required by ResourcePolicyStore. No admitted start/root or native host was reached. The fixture cleaned its newly created schema and an independent read-only connection confirmed absence. The correction enlists all initial IAM/TEN/PRJ owner seed contexts in one transaction, commits before current-authority readers use independent connections, and clears transaction bindings. SQL, target, case UUID, data scope and host/connection/time bounds stay unchanged. The revised binary requires a frozen delta operation approval before live execution; the failed receipt is retained. This does not qualify the start path or alter Product transaction requirements.

### Actual persisted-owner start qualified — 2026-11-08

The separately approved v2 integrated start batch passed one live test with no skips in six seconds. Concrete IAM/TEN/PRJ/INV/ART/REL/APR readers and real captured requester/approver evidence admitted the exact frozen release. Typed WFL/AUD/MSG start rejected application/target/classification and audit invocation/policy/authority substitutions, committed exactly one workflow/start receipt/AUD/publication, then returned the original on duplicate without calling allocation. Independent read-only cleanup verified namespace absence. The initial missing-seed-transaction failure and its cleanup remain preserved. Synthetic ART projection is fixture data, not registry qualification.

The next inactive test-only fault batch (`WorkflowAdmittedStartFaultDatabaseTests`) prepares real-reader instrumentation at the three start revalidation boundaries, same-key producer contention with explicit retry after rollback, and actual COMMIT-reply loss. Instrumentation observes concrete eligibility rather than supplying fake owner facts. Unknown observation must leave the original gate closed and never authorize replay/reopen. All five isolated cycles require one explicit frozen batch operation approval covering every lifecycle before credential/schema/native-host effects. This is not Product activation or completion of T12.2–T12.4.

### Five-cycle actual start fault qualification — 2026-11-08

The exact separately approved start-fault batch passed one live test covering all five sequential schema lifecycles in seven seconds without skips. Closing the common gate after real eligibility at each of the three start revalidation boundaries prevented commit and left WFL/request/start/AUD/publication/floor/native outbox counts zero; the first boundary did not allocate. Two owned roots produced an observed lock wait, one winner, one unique-conflict rollback, and explicit current retry returning the original without allocation. Dropping the actual COMMIT response returned Unknown and closed the original gate; independent original transaction/backend and canonical WFL/start/AUD/publication/floor/outbox observations matched committed state. No flush/replay/reopen or operator recovery action followed. Each lifecycle cleaned; the runner independently verified final namespace absence. Historical failures and success receipts remain retained.

The next test-only `WorkflowMetadataCommandFaultDatabaseTests` prepares actual cancel/reconcile metadata-root COMMIT-loss qualification with real persisted current authority. Reconciliation's synthetic evidence callback isolates its metadata commit path; the earlier actual original-evidence cases remain the separate evidence oracle. This does not replace them or establish a new reconciliation capability. SQL/connection/custody effects require a separate exact frozen batch approval; Product APIs/consumers, T12.3 and T12.4 remain unactivated.

### Actual metadata command Unknown qualification — 2026-11-08

The separately approved two-schema metadata batch passed one live test in two seconds without skips. Both cancel/reconcile used actual persisted current authority and lost their actual COMMIT response; both returned Unknown/null and retained closed gates. Independent terminal-backend/XID and exact owner command receipt, AUD hash, floor and CAS2 observations matched committed state. Both schemas and the dedicated advisory key were independently absent after cleanup. Reconcile's evidence callback is explicitly synthetic for metadata commit isolation; previously qualified actual original-timer evidence remains separate. No retry/reopen or new recovery authority followed.

Read-only regression preflight found an existing app schema in kordra_test with 58 relations and two known pending IAM/PRJ migrations. The owner said its data must be kept or ownership is uncertain, and chose preparation of a separate disposable test database. No app migration, row read/write or reset was performed. Preparation adds a test-only manifest-bound target profile parser and negative tests before any owner guard integration. It is selection metadata, not operation permission. Existing Product runtime/default targets remain unchanged; creating/migrating/dropping the new database needs a separate exact operation manifest approval. Full regression and T12.2 exit remain open.


### ADR-0025 explicit disposable test selection — 2026-11-08

Exact scoped ADR-0025 is owner-accepted. The owning factory adds separate disposable-test options/enlisted-connection entry points receiving explicit run/source/manifest/expiry projection from the validated test profile. Fixed loopback/test-role/name validation and connection/session safeguards apply. Selection is not operation authority; caller loader/runner verifies expected hashes and separate approval. Existing runtime/offline/default methods are preserved. Cross-owner negative/default/no-connection tests are added; actual adapter/root/test-host threading and disposable DB operation qualification remain pending. No database creation/migration/reset or existing app data operations are authorized by this code.


### Explicit disposable AUD/test bridges — 2026-11-08

ADR-0025 preparation adds explicit AUD shared/isolated/offline test enlistment and opt-in IAM/TEN/offline-policy adapters, plus a profile-bound catalog-floor writer. The same AuditTransactionContext staging/commit implementation, one-second budget and custody semantics are retained; default constructors and runtime paths still select their original validation. No ambient/global profile is registered. Offline/no-connection tests cover case-model isolation, expired binding, closed caller roots and missing custody. Owner transaction/test-host/transport integration and actual DB qualification remain pending. No DB/role/HBA operations are granted or executed.


### Explicit disposable WFL/MSG test options — 2026-11-08

Accepted ADR-0025 adds separate manifest-profile projection entry points for WFL revision1–5 isolated contexts and MSG receipt/journal/capacity/inspection construction. Exact disposable database, fixed test identity, case namespace and binding validity are checked without ambient/global target overrides. Existing default constructors/target guards remain unchanged. Offline tests inspect real model revisions/case separation and reject old/runtime/wrong targets and expired/unbound selections without opening connections. Host root/provenance/custody and actual fixture runner threading/qualification remain pending; these options do not establish durable writes, authority, operation permission or a complete regression run.


### Disposable root/provenance selection preparation — 2026-11-08

Accepted ADR-0025 adds explicit WFL caller-root creation, origin capture/independent probe, dedicated fixture custody acquisition and composed WFL/MSG/AUD recovery readers for the selected disposable database. The profile is retained as private selection metadata; legacy/default methods keep their original target choices. Opaque origin and custody selections must match the reader/root profile, preventing a same-case lease from another database/manifest being used as coordination. No origin is accepted from a request body; observation does not grant replay/reopen. Closed-reader and invalid-custody negative tests perform no DB operations. Actual cross-database custody, native transaction/provenance and full test-host qualification remain pending separate operation approval after integration.

ADR-0025 integration: explicit disposable cancel/reconcile entry points use the same authority, audit, custody and commit logic. Offline negative tests verify target/expiry rejection before authority, audit substitution denial and required custody; actual disposable metadata commits remain pending separate operation approval.

ADR-0025 offline continuation: explicit disposable composition reaches IAM resource-policy publication, APR policy publication, SEC target/registry metadata and ART stopped recovery. Canonical test manifests preserve the actual selected DB/role; default parsing/canonical vectors remain separate. AUD offline receipt reads require custody. The shared test-runner descriptor loader is prepared and tested for all-or-none configuration/no fallback, but full-suite fixture wiring remains incomplete; it is not Product startup or operation approval.

ADR-0025 fixture wiring continuation: shared test-only composition captures the explicit profile and supplies actual owner factories/adapters. Initializer, HTTP and injected-fault paths are being retargeted without changing business assertions or default/runtime target rules. No database operations were performed; full native/isolated fixture wiring and selected-target integration evidence remain pending.

ADR-0025 native/isolated fixture wiring (working tree): explicit runner selection now reaches actual WFL models/revisions, AUD enlistment, MSG receipts/journals/capacity/inspectors, native handlers, transaction probes, custody and recovery readers. SQL-only exports retain explicit offline metadata paths; live fixture construction validates the selected target before opening connections. Existing crash/redelivery/COMMIT-loss and authorization assertions remain in place. This is source plus offline verification, not current-binary live recovery evidence or database-operation approval.

### Disposable PostgreSQL wake qualification — 2026-11-10

The approved v4 remaining-case batch passed producer and receiver COMMIT-response loss and the successor guards, including both native DLQ and receiver-rollback scenarios. Independent checks verified the three case namespaces and the disposable database were absent after cleanup. The preceding v3 receipt covers wake publication and native redelivery on its recorded binary. The two receipts do not establish a single-revision full regression or close WF01–WF08/HO03.

The disposable T12.2 opt-in run exposed fixture selection and clock-floor schema mismatches. The corrected cancellation, expiry, lock contention and two authority/read cases passed on their respective recorded binaries. The two read cases use the caller-owned isolated IAM floor transaction. Disposable database cleanup was independently verified after each batch. A combined current-source gate remains required.

## PR29 round-1 review fixes — 2026-11-12

Codex round 1 on reviewed head `fd56a6edc7300f00ee0f5a80ae0290339c00fd26` reported three P2 findings. Two touch this feature; the third is owned by FEAT-IAM-001/FEAT-PRJ-001.

**PR29-WFL-TIMESTAMP-002 — metadata and successor precision.** The PR27 rule (canonical seven-digit JSON/hash, scalar comparison at provider precision) had not reached two new owner ports. `WorkflowMetadataReader` compared the reloaded microsecond deadline with the canonical value in CLR, so a valid `.xxxxxx7` deadline made `WorkflowReadCoordinator` return Unavailable. `WorkflowWakeSuccessorStore.StageAsync` did the same for creation/deadline and for the generation-1 timer due time, rejecting a valid successor. All three comparisons now run as typed PostgreSQL parameters on the exact scoped row. Canonical bytes/hash are unchanged; no tolerance, truncation or deadline extension is introduced, and an adjacent persisted microsecond still conflicts.

**PR29-IAM-FLOOR-001 — legacy isolated read floor.** Without a disposable runner profile, the read coordinator used the runtime IAM floor writer, which targets `app.iam_authority_clock_floors`, while the legacy fixture's IAM model lives in the case schema. `WorkflowReadCoordinator.CreateIsolatedTestCoordinator` now selects an explicit isolated path: target validation is exact loopback `kordra_test` without session overrides, and the floor is written to the case-owned schema in the same IAM/AUD root. Admission, revalidation, audit staging and gate-close behavior are unchanged; the public constructor keeps the runtime writer.

Regressions: `WorkflowWakeSuccessorDatabaseTests` now seeds sub-microsecond creation/deadline/due values and adds adjacent-microsecond rejection for each scalar; `WorkflowReadAuditDatabaseTests` uses a fractional deadline, requires the case-schema floor row and an unchanged runtime-shaped `app` floor, and rejects a 1µs persisted deadline change with Use but no Disclosure. `WorkflowPersistedAuthorityDatabaseTests` uses the same isolated coordinator.

Status: Implemented in source. Release build and the offline WFL/IAM/Bootstrap subsets pass (WFL 232 passed, 4 gated skips). The operation-gated PostgreSQL cases have **not** been executed for this revision; their fail-before/pass-after evidence and current-head CI remain pending explicit operation and push approval. No schema, wire contract, permission or accepted ADR changed.
