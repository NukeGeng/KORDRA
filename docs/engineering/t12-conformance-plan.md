---
id: ENG-T12-CONFORMANCE-PLAN
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-12
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# T12.2–T12.4 — acceptance và bằng chứng bắt buộc

Companion của [thiết kế](t12-remaining-design.vi.md), [WFL contract](../contracts/durable-workflow-v1.md), [worker contract](../contracts/worker-authority-v1.md) và ADR-0021/0022/0023. Tất cả case dưới đây là planned, chưa chạy. Unit mocks không chứng minh database atomicity, distributed ordering hoặc host độc lập.

## Fixture và oracle

Mỗi fixture có caseId/installation/tenant IDs tổng hợp, actor/action/scope, exact SHA/toolchain/lock graph, source/SQL/ACL hashes, resource allowlist, lifetime/budgets, operation approval và cleanup receipt. Dùng fresh UUID schemas trong kordra_test sau approval, fresh fixture service logins/certs nếu cần và hai OS processes thật. Không tái sử dụng account/trust/role/HBA approval T12.1 cho fixture mới. Không lấy target công ty làm shortcut. Những case không cần IO chạy trước, case IO chỉ sau SQL/operation review.

Oracle connection độc lập đọc owner-safe views/fixture tables để đếm state/history/AUD/MSG/publication/logical receipts, version/epoch/hash và absence of unauthorized output. Không purge để làm test pass; compare before/after, verify exact cleanup resource IDs và hash HBA/trust nếu có approved temporary change. Service identity SQL tests phải dùng actual least-privilege login, không owner/superuser giả runtime.

| Case | Thao tác/negative case | Oracle và exit condition |
|---|---|---|
| WF01 | Start same request/hash; changed hash; wrong installation/tenant/env; unknown template/version; Production | 1 immutable instance/receipt; conflict không state change; wrong scope deny và no leaked result |
| WF02 | Hai real transactions cùng expectedVersion; reordered step; wrong target/release/template hash | Một CAS/transition; loser inspect current receipt, không effect/result thứ hai |
| WF03 | WFL+actual AUD+floor+MSG commit/rollback, failure từng staging boundary | All-or-nothing qua reader độc lập; no early flush/Handled; shared server-root ten-envelope/160KiB limit |
| WF04 | Kill producer trước commit, sau commit trước flush; kill consumer trước effect, sau commit trước ACK | Pending transport recovered; mỗi logical transition/history/result/AUD chỉ1; không mất committed intent |
| WF05 | Wire proxy drop actual PostgreSQL COMMIT reply; stop/restart successor trước old backend terminal | Matching/Conflict/StillUnknown đúng; không callback/reclaim khi chưa terminal; original failure giữ nguyên |
| WF06 | Timer dài hơn120s, deadline24h, due/restart, duplicate timer generations, overdue message | Không chạy sớm; old original deadline không refresh; successor schedule có custody/receipt proof, counter≤10 |
| WF07 | Cancel trước start, lúc timer fires/CAS, khi Running/Unknown, sau terminal | Intent không thành undo; Unknown không chuyển Failed/Cancelled khi thiếu evidence |
| WF08 | Five attempts1/5/15/60s, oversize/depth/invalid wire, DLQ, clock rollback/floor mismatch, retention | Bounded attempts; safe diagnostics; active/Unknown/90day receipt không mất; expire không renew quyền |
| AU01 | Missing/wrong mTLS CA/SAN/EKU/pin, expired/revoked cert, old service generation, spoof body/GUC/tenant | Internal RPC/SQL deny trước owner transition; no cookie forwarding hoặc grant by role label |
| AU02 | Permit cùng ID nhưng khác service/instance/hash/version/tenant; expired/consumed permit; same permit new DB root | Chỉ exact service/root/operation được stage một transition; SQL direct bypass bị deny |
| AU03 | Every invalidating writer before permit, giữa issue/consume, trước/đang/after worker fence commit | Epoch ordering đúng; revoke committed trước thì reject; worker trước thì writer chờ bounded; no stale permit after change-back |
| AU04 | Writer matrix gồm logout/security boot, roles/policy, memberships/grants, owner binding/lifecycle, REL/APR/SEC/service revoke | Mỗi writer có real entry-point race test + source path; registry/static grep không đủ coverage |
| AU05 | API/DB outage; RPC response lost/duplicated; transaction budget/expiry; issuer restarts | Fail-closed no cached grace; duplicate same key returns original; unknown root giữ custody, no auto retry business |
| DB01 | Runtime create/alter/drop, foreign schema read/write, AUD update/delete, raw WFL writes, arbitrary function args/search_path | Actual login deny; grant list/function source/hash đúng; SECURITY DEFINER không nhận identifier/tenant authority tùy ý |
| DB02 | Upgrade/downgrade/schema/template/lock mismatch; backup restore old authority floor/epoch | Readiness deny incompatible/stale authority; expand/contract rehearsal không mất history; no startup DDL |
| HO01 | Two workers cùng installation, lock connection loss, suspend old process tới stale heartbeat, restart competitor | Chỉ1active; time expiry khôngtakeover; previous backend terminal + receipt reconciliation trước epoch mới |
| HO02 | SIGTERM10s drain, SIGKILL tại tất cả root boundaries, native transport reassignment | Committed state survives, Unknown preserved; host not ready khi custody chưa resolve |
| HO03 | Full unchanged T01–T11 / MSG regression, DTO fixtures/golden vectors, source dependencies | Không public fake/type activation, no breaking consumer change, no Agent/private-owner references |
| RH01 | API+worker+DB off-Mac, start synthetic admitted delayed workflow, close client then sleep Mac≥120s | Independent host records transitions/timer/AUD during closure, compare service-side times/versions after reconnect |
| RH02 | Repeat RH01 but stop issuer API, then restore it; no hidden Mac process allowed | Worker custody/metadata remains; business transition blocked while issuer absent; reevaluated permit after restore; no promise offline authority |
| RH03 | Restart actual worker independent host; controlled service/DB outage within approved plan | Recovery outcomes and independent reads verified, no data/history loss beyond recorded unknown; no provider calls |

## Evidence bundle và gate semantics

Bundle riêng mỗi case: environment identity (public redacted), protected authorization/operation manifest, exact source SHA and hashes, clock/expiry boundaries, invoked commands, process/backend IDs privately, before/after counts, original failures, terminal results, cleanup proof. Logs only fixed codes/IDs/hash, no credential/payload/connection string. Test report phải list skipped/pending với lý do; không suy rằng native test passed khi chỉ chạy guard.

T12.2 exit WF01–WF08 + applicable HO03, thực tế AUD/MSG khi transition integration có; distributed positive HTTP/worker activation chờ T12.3. T12.3 exit AU01–AU05/DB01–DB02/HO01–HO03 với exact writer matrix và owner functions; mỗi slice commit theo feature/owner, required local gates + exact-head hosted CI + Codex BIR. Không thêm AI reviewer tại validate/accept. T12.4 local exit toàn matrix trừ RH; remote qualification cần RH01–RH03 và riêng operation approval. Bất kỳ unresolved blocking finding hoặc pending mandatory remote case giữ broader milestone chưa hoàn tất.

Mỗi failed execution phải ghi cause, code/test fix và targeted rerun trước full gate; giữ failed checkpoint/receipt. Reuse chỉ qua deterministic one-hour fingerprints/fixture epoch. Không nâng timeout hoặc bỏ assertion để pass mà không đối chiếu requirement/operation limit.

Reauthorization cases AU05/WF01 bổ sung: same original actor/current grants can replace an expired delegation only in BlockedAuthority; original spec/deadline/retry counters unchanged, old delegation rejected after epoch change. Wrong actor, new tenant/release/template, Unknown/terminal state or stale If-Match deny without state change. Current release owner must affirm any new approval applies to the exact original release revision/hash.

Capacity gate CAP01 (T12.3/T12.4): exactly100 active instances,1,000 unsettled publications/32MiB boundary and competing API/worker writers under the fence. Rejected admission creates no provisional orphan; transaction rollback frees only uncommitted reservation, Unknown preserves committed reservation; duplicate does not allocate again. Terminal reconciliation settles once. Expired/missing storage health denies new work. Independent physical storage/WAL sizing is an operation qualification input, not inferred from a logical counter.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.

## WF02 initial custody race evidence — 2026-11-01

The separately approved T12.2b fixture now covers cancellation-first with an old tracked Running row and claim-first with exact lock-timeout SQLSTATE. Independent readers verify that no cancelled workflow receives new custody. Original failing evidence and successful cleanup remain retained. This proves the initial-claim serialization subset only, not distributed authority, successor takeover, multi-owner atomicity or the entire WF02 matrix.

## T12.2c isolated root evidence — 2026-11-01

The three separately approved local UUID-schema cases now exercise actual WFL/AUD/floor/MSG atomicity rather than mocks. WF03 includes six staging-boundary rollback/cancellation branches, independent zero-effect observations, no early flush, actual logical/inbox Handled pairing, shared ten-envelope/160KiB root budgets, exact operation publication and single-coordinator generation guards. A missing single-coordinator rejection was reproduced before the guard fix; a later actual root on the same connection remains eligible. Producer commit/rollback/hash-conflict/old-receipt and AUD replay-without-new-commit cases remain preserved.

WF05 has actual backend COMMIT reply loss plus independent matching/conflict/wrong-tenant receipt inspection after the recorded backend ended. This is its transaction-observation subset; it does not satisfy the planned restart/successor-before-terminal/custody-reclaim matrix. That lifecycle work follows C under T12.2d and later worker activation. The synthetic receiver and offline metadata tests do not prove distributed authority, production readiness or an independent host. Frozen proposal, exact successor queue-SQL approval, failed/build predecessors, current-source three-case success and namespace cleanup are private coordinator evidence; no new operation approval is inferred for D.

## Initial waiting-timer owner qualification — 2026-11-01

WF06 now has pure120s/24h/original-deadline/counter boundary tests and a separately approved actual owner-store fixture for waiting-timer rollback/commit/immutable duplicate and scoped ordered100-row discovery across102 fixture schedules. Negative scope/hash/custody/operation/order and stale-version/cancellation checks use actual PostgreSQL and independent readers. Exact new namespace cleanup passed. Synthetic prior checkpoint facts are setup data, not a shipped executor; the capacity/authority/production workload limits are not qualified by this fixture. Native dispatch, timer generations, issuer/clock/floor rules, cancellation/recovery/retention and WF04/WF05 process restart remain subsequent D/worker proofs. The first fixture's approval is not inherited by new native-host or restart operations.

## Timer scalar precision regression — PR27-WFL-TIMESTAMP-001

The actual owner-timer fixture now includes canonical fractional creation/deadline/due timestamps before and after2000, fresh commit and original duplicate, and original byte/hash preservation despite scalar column precision loss. Deliberate adjacent-microsecond changes to the new fixture's scalar creation/deadline/due values must still deny; no production data is modified. The original failing case and repaired cleanup proof are retained under the same approved scope/SQL/budgets. These checks do not grant clock grace, extend a deadline or qualify D2 worker/recovery behavior.

## D2 pure binding coverage — not full WF06

The descriptor golden/hash and negative matrix cover immutable scope/spec/time, versions/epochs/counters and closed JSON. Host tests cover distinct scheduler/wake IDs, original delivery/correlation/causation, workflow versus step versions, exact TimerDue identity, future/expired rejection and registry type/version/scope rejection without executing a consumer. Offline model-difference tests require revision2 to add only wake_operations/indexes while preserving revision1 and scoped restrict FKs. These checks do not qualify native timer publication, caller-root atomicity, restart or successor reconciliation; actual expanded fixture rights remain separate.

## D2 initial root fixture preparation

The proposed D2 root fixture covers revision1→additive-revision2 compatibility, no independent visibility before commit, missing AUD/floor rollback, explicit rollback, stale instance/timer/step bindings, wrong tenant, cancellation/unsettled custody, preserved native outgoing bytes and no republish on original duplicate/changed delivery. It requires a separately approved new schema and producer-only native host. No successor process, unknown-COMMIT retry or WF04–WF08 completion is inferred.

## D2 initial root actual result and recovery continuation

The first exact D2 root fixture has actual passing evidence and cleanup for the listed atomicity/duplicate/cancellation rejection subset. It does not qualify successor lifecycle or full WF04–WF08. The subsequent read-only wake inspector reports only independently observed owner facts; missing row/backend evidence cannot become permission for replay. Further current-source multi-owner and native lifecycle qualification remains required.

## TimerDue receive path — implementation in progress

TimerDue preparation adds transport-valid but mismatched logical IDs/resource rejection, bounded closed delivery guards and owner-root requirement. Actual native receive/inbox/owner/AUD/outgoing commit, duplicate ACK-loss and restart/reconciliation proof still require the consolidated later lifecycle fixture, not a replay of the first producer-only operation approval.

## D2 paired-result/native receiver preparation

Result conformance adds independent bytes/hash, closed Completed/Running/TimerDueElapsed tuple and rejection of validly encoded but wrong operation/resource/version/hash bindings. Metadata-generated receiver preserves the compiled-handler requirement; receive/inbox/owner/AUD/result and ACK-loss/restart proof remains in the planned consolidated native lifecycle operation batch.

## D2 native receive/redelivery batch preparation

The next native batch covers receiver root/inbox atomicity, original window/result binding, exact independent WFL/MSG observations, host-object replacement and same-envelope redelivery with HandlerCalls=1 plus one owner/AUD/result. It uses one new schema and at most one active host, without OS kill, clock mutation or VM/provider work. Unknown-COMMIT/process restart/successor cases remain mandatory and unqualified by this batch.

## D2 native receive/redelivery actual result

The isolated native receive/redelivery subset now has a real passing result and cleanup: producer/receiver host objects were sequential, receiver root effects were independently invisible until commit, exact result/WFL/MSG observations matched, and identical-envelope redelivery caused no second handler/effect/AUD/result. This simulates post-commit transport redelivery; actual OS crash/ACK-loss injection, unknown-COMMIT successor and worker/off-Mac matrices remain incomplete.

## D2 combined recovery reader preparation

Recovery observation guards reject wrong tenant/resource/scope/correlation/stage and reuse of one AUD intent for two operations before IO. Closed readers remain Unavailable without opening a connection or refreshing deadlines. Actual composite observation under unknown COMMIT/absence/terminal races still belongs to the next fault batch; the passing initial native test does not qualify this newer source.

## D2 clarified clock and recovery observation progress

Pure clock tests now cover long idle, nondecreasing UTC, one-second current-sample limits, DB rollback/pace, fault latching, exact original deadline and monotonic budget exhaustion without host poisoning for timeout alone. No privileged clock mutation is used. Actual checked-root SQL/precommit/fault/restart matrix remains planned. The next native pre-commit oracle also requires StillUnknown for an uncommitted completion whose parent version is still older.

## D2 exact transaction provenance preparation

Predecessor-proof preparation now binds the exact full transaction ID and backend/server generation from the staged caller root. Prepared/in-progress status remains unresolved; missing/aged-out status or a changed server is unavailable, never safe rollback. The next fault fixture must capture a real root before commit, observe in-progress, verify confirmed rollback/actual lost-COMMIT outcomes independently and combine owner/AUD/MSG evidence. Opaque in-process provenance is not a cross-process restart journal.

### D2 consolidated root fault batch — prepared, not executed

`WorkflowWakeFaultDatabaseTests` exports exact isolated SQL without IO and prepares three sequential attempts against one synthetic waiting workflow: confirmed rollback with backend provenance, checked-clock budget expiry before COMMIT, and actual dropped COMMIT reply. Verify independent unknown/matching WFL–MSG–AUD evidence, original backend observation, no flush on nonconfirmed root and admission remaining closed after observation. One producer host; no consumer/process restart or OS clock mutation. Each attempt is fixture-only; this is not permission for a product successor generation. New schema/SQL/source/binary manifest and explicit operation approval are required before DB execution.

### Fault attempt01 retained; repaired fixture awaiting qualification

The actual approved attempt01 stopped at the exact admitted-instant floor guard before COMMIT. Fixture cleanup and independent read-only namespace absence are recorded privately. Repair reuses one admitted timestamp and corrects scheduler-reader older-parent handling (`D2-WFL-OBS-002`); retain precommit/rollback StillUnknown and postcommit matching assertions. No operation replay under the old frozen source/binary hashes. SQL/resources/target/budgets remain unchanged; revised immutable manifest must identify the source/binary delta before another DB attempt.

### D2 root fault attempt02 — actual pass with cleanup

Frozen v2 proposal SHA `786951493c7f9b0ec12fec46cc18e07db5a621366d5adab131384c24d77d4e59` received explicit owner approval. The actual one-workflow/one-producer case passed (no skips): active root Unresolved, confirmed rollback Aborted, precommit and rollback receipt StillUnknown, checked-clock budget expiry before commit without admission poison, actual dropped COMMIT reply → Unknown/closed/no flush, independent original Committed plus matching WFL/MSG/AUD observations. Owned host stopped and schema absence was asserted and independently observed. Original failed attempt01 retained. Receiver fault/process recovery/successor generation and full D/E qualification are still pending.

### D2 native receiver response-loss case — compiled/prepared, not executed

`WorkflowWakeReceiveFaultDatabaseTests` exports a new isolated UUID schema without starting hosts/opening connections. Planned native batch: one synthetic workflow, producer then receiver sequentially (max one active host), loss of the actual receiver COMMIT response only after WFL/AUD/MSG/inbox staging, precommit independent invisibility, original transaction/root-outcome distinction, exact original completion receipts/native result, closed admission and one handler/effect. Stop owned host and verify schema removal. Test90s + cleanup15s; no OS clock/process fault, roles/HBA, provider/VM/Keychain or live deployment. No receiver error-policy/independent-host qualification is inferred. New immutable SQL/source/binary manifest + operation approval precede any IO.

### D2 receiver fault attempt01 — actual pass, cleanup verified

Owner-approved frozen proposal SHA `31574998cebee150b0128a9896e1ee14dc12c16113dd9371d01403d181a08803` passed the native receiver case 1/1 with no skip. Actual staged precommit completion is invisible/StillUnknown; lost actual COMMIT response → Unknown/closed admission; after host stop the independent original Committed transaction and complete inbox/logical/WFL/AUD/result binding match. One handler/effect and original native delivery ID/hash are required; identical outbox/queue copies during send/ACK overlap are permitted. Both owned hosts stopped, cleanup asserted, namespace absence independently observed. Process restart/production error policy/successor and remaining D2 guards stay pending. Final current full gates remain a D2/PR completion requirement.

### D2 cancellation owner fixture — prepared, not executed

`WorkflowCancellationTests` checks independent command hash/binding negatives, additive revision2→3 SQL/model compatibility, append-only/no-delete/current canonical owner checks and no-IO caller-root rejection. `WorkflowCancellationDatabaseTests` exports exact SQL without opening connections and prepares four synthetic workflows in one new UUID schema: populated-schema expansion, WFL/AUD/floor rollback and commit, matching original duplicate including after terminal state, wrong actor/version/tenant/spec conflict, sticky Pending intent, cancellation-versus-stale-tracked-start CAS, WaitingTimer dispatch denial, synthetic unresolved timer marker preventing terminal Cancelled, and Reconciling/Unknown preservation. No native host or MSG business execution; this is owner-intent/CAS qualification, not full cancellation API/permission or transport proof. Case90s + cleanup15s; no role/HBA/superuser/provider/VM/Keychain. Obtain exact schema/SQL/source/binary operation approval before DB execution; keep prior fixtures/failed receipts unchanged.

The approved cancellation fixture attempt on 2026-11-05 failed during race-test teardown when the fixture tried to roll back an already committed transaction. A separate read-only catalog query verified schema removal. The revised test disposes the transaction and verifies the specific cancellation denial. Its 34 targeted offline tests passed. The revised binary still requires PostgreSQL qualification against the same SQL and resource scope.

Cancellation fixture v2 passed on PostgreSQL on 2026-11-05. The test covered additive expansion on a populated schema, rollback and commit across WFL/AUD/floor, original duplicates and binding conflicts, cancellation winning the stale-start race, waiting dispatch denial and unresolved-state preservation. A separate catalog query verified schema removal.

The targeted recovery matrix checks committed, aborted, unresolved and unavailable roots against matching or incomplete receipts. The producer fault fixture now asserts correlation with the committed root and rejects attribution to the earlier aborted root. The receiver fixture checks that a receiver origin cannot establish scheduler provenance. These new database assertions await an approved execution. Earlier receipts remain evidence for their frozen executables.

Offline creation-window tests cover the exact creation and deadline boundaries, equivalent UTC offsets and a historical request. The PostgreSQL fixture adds expired missing-receipt and retained-duplicate assertions. Those assertions still await execution on current artifacts.

The owner-approved custody process case passed on local PostgreSQL with one test and zero skips. It used an isolated fixture installation and its registered advisory key. The test host observes a separate child holding custody, verifies contention, terminates only that child and verifies acquisition after exit. It then closes the successor session and checks that the key is available. The fixture covers session custody with no table changes. Transaction reconciliation and the remaining WF04 crash boundaries have separate evidence requirements.

Offline successor-reconciliation checks cover closed/cancelled readers and distinguish a missing MSG receipt row from an unavailable or conflicting observation. The updated receiver fault fixture also checks that in-progress and committed original roots cannot yield an aborted-completion observation. The receiver fixture also checks that a different audit ID, altered audit content under the same ID and a different result delivery with the same binding hash conflict before reconciliation. The changed database assertions and the positive revision 3 aborted-receiver case require live qualification.

The prepared expired-delivery SQL fixture covers 21 synthetic metadata states on one new UUID schema, including provisional invisibility, exact terminal match, journal omission/mismatch, replayable/null/archive/ambiguous DLQ, incorrect destination/type, active or unknown inbox state, pending outbox and immediate/scheduled queues including a second declared route. The reader is independently connected. This qualifies SQL observation only after approved execution. Native quarantine production, receiver rollback composition and successor CAS remain additional cases.

The first approved observation fixture failed its first positive assertion because the synthetic seed used an unnormalized destination without the trailing slash produced by Uri.ToString. Offline reproduction confirmed the difference. The test seed now uses the native URI representation and reports the failing case name. Original failure and independent namespace-absence evidence are retained. The separately approved revised executable passed the full 21-case SQL observation fixture. Independent cleanup found zero matching namespaces.

Successor binding regression tests cover counter skipping/reset/overflow, reused identities, changed step/timer/version and early or substituted occurrence. Native expiry tests distinguish valid expiry from malformed, wrong-identity/route and future inputs. A separate native fixture is prepared to verify an actual nonreplayable DLQ diagnostic, committed journal visibility and zero business effects using the existing native ingress profile.

The native expiry fixture passed with one message and one host, zero business effects and independent cleanup. The successor integration fixture is prepared with sequential producer, expiry receiver and successor producer hosts, exact custody, a terminal scheduler origin, revision 3 cancellation-aware observations, stale timer/cancellation denial, root rollback/commit, original duplicate/no republish and original receiver-effect absence. It awaits exact operation approval.

Successor attempt 01 timed out before reconciliation while waiting for DLQ. Local regression tests confirmed the mismatch between sanitized publication destinations and unsanitized ingress/observation routes. The repaired fixture pins the native destination and unknown-message DLQ policy, and bounds its delivery wait to 15 seconds within the existing total budget. Fresh binary authorization and actual integrated rerun are required.

Successor v2 passed its exact live operation after the route repair. Verified native original expiry/DLQ, dedicated custody, original committed scheduler correlation, locked WFL state, stale timer and cancellation denial, atomic successor rollback/commit and duplicate without republish. Exact namespace and advisory lock counts were zero on an independent connection after teardown. Original failed attempt remains retained. Remaining WF04 process boundaries and receiver-abort successor are pending.

WF04 now has a compiled four-case process fixture with exact fresh UUID schemas, deterministic SQL artifacts, one child/native host at a time and independent read assertions. Producer-before-commit requires no durable publication/audit; the other three cases require exactly one eventual owner completion, logical receipt, completion audit and result. Consumer-after-commit additionally requires zero handler calls in the recovery host. Backend disappearance is observed for the exact child application name. All four live cases remain pending operation approval.

WF04 attempt01 started the producer-before-commit and producer-after-commit cases, then timed out in the recovery wait. The two consumer cases were not reached. A fixture configuration mismatch left scheduled recovery startup at one minute, matching the entire case budget. V2 adjusts only the test recovery cadence and adds bounded count diagnostics; per-case and batch limits are unchanged. Independent cleanup verified all four exact namespaces, custody keys and child database application sessions absent. The pinned package documents recovery timing in its source. [Wolverine 6.59.0 source](https://github.com/JasperFx/wolverine/blob/0313f3f49ed34d203f077f65210efe3adfa36079/src/Wolverine/DurabilitySettings.cs).

WF04 v2 passed the complete four-case real-process batch. The producer-before-commit case retained the original waiting version and no publication/audit. Producer-after-commit and consumer-before-effect recovered one owner completion/logical receipt/audit/result. Consumer-after-commit recovered its result with zero handler calls in the new host. Old child database sessions were absent before recovery. Independent post-batch namespace/key/session counts were zero for every case. The first failed cadence attempt is retained.

The next integrated fixture covers both committed-scheduler expiry and a native receiver that aborts before commit. In the second case it captures receiver provenance, observes native settlement, waits for the original window to expire, and rechecks the absence of business effects before successor CAS. Both cases exercise original creation receipt recovery after the retention horizon, rejection of a missing expired receipt, and denial of Failed/Expired while a published timer remains unresolved. Live qualification is pending.


### T12.2e integrated start fixture preparation — 2026-11-08

T12.2e integrated start: `WorkflowAdmittedStartDatabaseTests.ActualCurrentOwnersBindTypedStartAuditAndOriginalReceipt` is a prepared, unverified live case. Uses actual persisted owner facts/captured requester and approver evidence; checks owner/audit substitution and duplicate allocation. Separate SQL-export/policy tests are offline only. Gate-close, concurrency and Unknown-start recovery remain required; no broader acceptance is inferred.


### T12.2e live start evidence and fault preparation — 2026-11-08

Start v2 passed its approved live case with exact independent cleanup. Prepared fault batch adds gate closure after real eligibility at each of three stage/commit revalidation boundaries, contended producer roots/explicit original-receipt retry, and one dropped actual COMMIT response with independent transaction/receipt/WFL-AUD-MSG/outbox observation. Each scenario has a fresh isolated schema lifecycle; no simultaneous hosts/listeners or gate reopening. This remaining batch requires separate exact-manifest operation approval and must not be reported verified before execution.


### T12.2e start fault evidence and metadata command preparation — 2026-11-08

The separately approved start-fault batch completed all three gate boundaries, actual same-key lock contention with explicit duplicate retry, and actual COMMIT-response loss with independent matching owner/AUD/MSG/floor/native outbox evidence. Original gate remains closed for Unknown; no recovery action is inferred. Each lifecycle cleaned, final independent namespace observation passed. The next two-schema metadata command batch prepares cancel/reconcile Unknown commit classification with actual current persisted authority; its reconcile evidence callback is synthetic and does not replace the earlier actual origin/evidence cases. Two exact schemas and one dedicated advisory key require separate operation approval. Full T12.2 exit and global gate status remain distinct.


### T12.2e metadata evidence and disposable regression preparation — 2026-11-08

The approved metadata command fault case passed both cancel/reconcile branches with actual current authority, actual lost COMMIT replies, Unknown/null responses and retained closed gates. Independent original XID/backend plus receipt/AUD/floor/CAS observations and both namespace/key cleanup passed. Reconcile evidence callback was synthetic for metadata commit classification; actual original timer evidence remains separately recorded. Existing test-database app data must be preserved per owner clarification. ADR-0025 proposes a separate exact disposable test profile; parser/offline SQL checks cannot replace full regression or authorize existing-data migrations.


### ADR-0025 core test-target integration — 2026-11-08

ADR-0025 explicit disposable test-target core is prepared under exact owner approval. Current offline tests validate profile/owner factories, model revision/case separation, no implicit runtime/default selection, closed-root/custody negatives and existing probe/classification/architecture behavior. This does not qualify new database creation, shared-root atomicity, native transport/provenance or cross-database custody races on the new target. Default factory source sections were compared byte-for-byte with the retained pre-integration inventory. Existing app data is preserved; global regression still requires the completed test-composition/manifest and separate DB operation rights.

ADR-0025 continuation: explicit profile now reaches cancel/reconcile and ART/REL/APR/catalog owner transaction entry points and their AUD factories, preserving shared commit/authority behavior. Existing owner/offline manifests and actual full-suite test composition still require profile integration before freezing the disposable DB operation plan. No operational rights are inferred from ADR acceptance.

ADR-0025 native fixture wiring is now present in the working tree: WFL isolated/root/handler/probe/custody/recovery selection, module-only model fixtures, MSG compatibility/native/metadata/receipt paths, exact restricted transport-role selection, and isolated IAM policy/read-audit fixtures. Offline configuration/SQL/default-guard evidence is distinct from native crash/recovery execution. Historical live receipts are retained and are not represented as current-source verification. T12.2 exit/full regression and T12.3/T12.4 remain open; freeze the final DB/role/config/executable operation manifest only after remaining suite-composition review.

## PR29 precision and fixture-floor regressions — PR29-WFL-TIMESTAMP-002 / PR29-IAM-FLOOR-001

The successor fixture seeds canonical creation/deadline/due values with a seventh fractional digit, stages a valid successor, and rejects an adjacent persisted microsecond on each scalar inside a rolled-back root. The read/audit fixture reads a fractional-deadline workflow, rejects a 1µs persisted deadline change with a durable Use and no Disclosure, and requires the IAM floor in the case schema with any pre-existing `app` floor unchanged. These are operation-gated cases: implemented, not yet executed on this revision. They grant no clock grace or deadline extension and do not change WF04–WF08 status.
