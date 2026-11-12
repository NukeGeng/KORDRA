---
id: CONTRACT-DURABLE-WORKFLOW-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-12
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# Durable Workflow v1 — T12.2

Thiết kế trình duyệt, chưa có implementation hoặc quyền vận hành. Owner: FEAT-WFL-001 / MOD-WFL. Đọc cùng [ADR-0021](../decisions/0021-durable-workflow-persistence.md), [worker authority](worker-authority-v1.md) và [thiết kế tổng thể](../engineering/t12-remaining-design.vi.md). Không thay accepted Durable Messaging v1 hay quyền commit của caller.

## Phạm vi và kết quả nghiệp vụ

WFL sở hữu instance, step, timer, transition history, operation custody và recovery decision. MSG sở hữu transport/receipt; IAM sở hữu delegated authority; AUD sở hữu audit; REL/APR giữ release/approval. WFL Completed chỉ nói template đã hoàn tất. DEP Verified và provider mutation thuộc T13.

Template đầu `pilot.checkpoint.v1` là workflow thực: Checkpoint → WaitUntil → Checkpoint → Complete, có state/timer/restart/AUD thật và không effect provider. Đây là template đầu của engine, không phải mock production provider. Template dispatch/deploy/rollback chưa được đăng ký. Step handler là compiled allowlist, không CLR-name từ wire, script, plugin động, expression editor hoặc serialized delegate.

## Identity, binding và trạng thái

Instance schemaVersion=1 gồm installationId, tenantId, projectId, applicationId, environmentId, targetId, releaseId/revision/hash, templateId/version/hash, instanceId, requestId, specificationHash, expectedVersion, delegationId, createdAtUtc, deadlineUtc. Owner xác minh toàn bộ chuỗi resource; tenant từ subject/owner facts đáng tin cậy. V1 chỉ Development/Test. Không chấp nhận system scope để fallback cho tenant workflow.

Backend sinh IDs và canonical spec theo quy tắc closed JSON của MSG: thứ tự field cố định, GUID lowercase-D, UTC bảy chữ số phần giây, số nguyên, không duplicate/unknown fields. Hash bao gồm immutable business/resource binding, ordered step descriptors và original deadline; không gồm delivery/trace IDs, retry counter hoặc authority-history references (delegationId/permitId). Specification tối đa 64 KiB; canonical golden vectors bắt buộc. Thay binding/template tạo instance/request mới, không update spec cũ. T11 approval có executionAuthorized=false, tiếp tục giữ nguyên.

| Instance state | Chuyển tiếp hợp lệ |
|---|---|
| Pending | Running sau current admission + claim; BlockedAuthority, Expired hoặc Cancelled trước khi có possible operation |
| Running | WaitingTimer, BlockedAuthority, Reconciling, Succeeded, Failed, CancelRequested |
| WaitingTimer | Running khi due và admitted; BlockedAuthority, CancelRequested, Failed/Expired khi chưa possible effect |
| BlockedAuthority | Running sau authority mới cho đúng spec; CancelRequested, Expired nếu không có operation chưa rõ |
| CancelRequested | Cancelled chỉ khi tất cả operation đã terminal/no-effect; Reconciling nếu chưa rõ |
| Reconciling | Trạng thái xác nhận bằng exact receipt/evidence; giữ nguyên nếu chưa đủ evidence |
| Succeeded / Failed / Cancelled / Expired | Terminal, không reset hoặc tự resume |

Step states: Pending → Running → WaitingTimer / Succeeded / Failed / Unknown; WaitingTimer → Running; Unknown chỉ được resolve bằng evidence. Cancel là intent trong khi step đang chạy. Không chuyển Unknown sang Failed/Expired chỉ vì timeout. Nếu workflow deadline hết khi còn Unknown, giữ Reconciling với deadlineExceeded=true; hết hạn không chứng minh no-effect. Failed không tự kích hoạt compensation hoặc rollback provider.

## Persistence, CAS và custody

Schema `wfl`, migration revision1, EF/Npgsql theo graph hiện có. Tables: instances (immutable binding + version/state), steps (ordered step/version/outcome), transitions (append-only), timers, operation_custody, recovery_decisions (append-only), request_receipts (immutable), clock_floor và authority_bindings (append-only delegation references, không copy IAM grants). Mọi key/index chứa installation/tenant và exact instance/step; uniqueness request identity + hash, transition operation identity, timerId/generation. DB constraints cấm tenant null, version âm, unknown enum và orphan association. Không sửa history để dọn một test.

Mỗi transition dùng expectedVersion CAS; update một row hoặc conflict. Khoá theo thứ tự authority fence → instance → step → custody → timer → AUD/floor/MSG. Không giữ lock trong network I/O. T12 pilot một active ControlWorker/installation, consumer concurrency1; có thể nhiều process tranh start nhưng chỉ một holder qua session-level PostgreSQL advisory lock gắn dedicated connection, key installation-derived và đăng ký trong manifest. Key collision chỉ được từ chối startup, không được chia sẻ installation. Process mất dedicated connection đóng admission trước thêm công việc. Đây là coordination, không là external fencing.

operation_custody gồm instance/step/operationId/specHash, worker identity + generation, epoch tăng đơn điệu, startedAt, heartbeatAt, phase và lastCommitReceipt. Heartbeat5s, stale15s chỉ tạo suspect. Không takeover chỉ theo thời gian. Sau khi holder mất lock và backend transaction cũ terminal, successor phải reconcile exact durable receipt/version trước claim CAS epoch+1. Chưa xác minh transaction cũ terminal thì giữ Unknown. Một process mới không tự kết luận process cũ hết provider effect.

## Atomic transition và Unknown

Host coordinator mở một connection/root transaction. Trên cùng root: validate current authority/fence → claim logical MSG key → WFL CAS/history/timer → AUD append + WFL clock floor → paired outgoing result/next wake → inbox Handled. MSG adapter stage-only; AUD owner port phải hỗ trợ root và worker profile được chốt ở T12.3. Một commit-capable path; không transaction AUD riêng hoặc hai login giả atomicity. Không effect callback sau khi nhận commit Unknown.

Confirmed commit rồi mới native flush/ACK. Mất flush/ACK: native recovery và immutable receipts, không transition thứ hai. Lost COMMIT reply: dừng consumer/admission của host, giữ custody, reconcile bằng reader độc lập exact installation/tenant/operation/hash. Matching receipt xác nhận kết quả gốc; conflicting hash quarantine. Absence chỉ là StillUnknown cho tới khi xác minh backend cũ terminal và không còn prepared/unresolved transaction. V1 cấm prepared transaction; nếu operator phát hiện thì gate đóng và xử lý offline. Rollback được xác nhận mới chứng minh no-commit. Startup/restart không là bằng chứng kết quả.

## Timer, retry, cancellation và retention

Workflow lifetime tối đa24h, tối đa32 steps, timer due không vượt original workflow deadline. Clock dùng effective UTC không giảm + monotonic budgets; persisted floor từ cùng transaction. DB clock rollback hoặc lệch >1s giữa các mẫu DB/UTC/monotonic hiện thời đóng admission; persisted floor là mốc UTC không được lùi, không phải giới hạn thời gian idle; không sửa OS clock khi thử. Due query tối đa100 timers, index theo status/due/id, poll1s; gửi tối đa10 envelopes/160KiB serialized Data trong mỗi root, chia root trước staging, không dựa vào rollback savepoint để reset budget. Timer `(timerId,generation)` unique; CAS dispatch với state+MSG cùng commit, không xoá timer trước khi publish.

MSG envelope vẫn deadline ≤120s và original retry horizon của accepted v1. Không tạo một message sống24h: timer dài nằm trong WFL. Khi timer thực sự due, một scheduler operation mới có original deadline ≤120s và ≥1 tick trước workflow deadline; cùng timer generation chỉ có một publication. Message hết hạn thì settle/quarantine message cũ; tạo scheduler operation kế nhiệm chỉ sau exact receipt/custody reconciliation chứng minh logical transition chưa xảy ra, với bounded counter10 và audit, không đổi deadline/messageId để replay operation cũ. Hết counter → Failed/SchedulerBudgetExhausted chỉ khi chứng minh không còn pending/unknown operation; nếu chưa rõ thì Reconciling, không success giả.

Năm native handler attempts, delays1/5/15/60s theo MSG; chỉ transient allowlist. CAS conflict đọc lại current state/receipt, không generic business retry. Deny/revoke/unsupported template không retry thành permit. Cancellation API chỉ ghi intent + audit; phải recheck trước transition, không tuyên bố undo confirmed effect. Nếu timer đã fire đồng thời cancel: CAS quyết định thứ tự, outgoing wake kế tiếp thấy cancellation và dừng.

Active/Unknown history không purge. Terminal instance/transition/recovery/request receipt giữ tối thiểu90 ngày và chưa có automatic purge; inbox≥7ngày, producer recovery≤24h theo MSG. Sau retention horizon, historical request không được thành operation mới vì thiếu record; reject RecoveryHorizonExceeded và yêu cầu request mới được approve. Cleanup/migration không DROP immutable history để rollback.

## User API và permission proposal

New exact runtime pairs: workflow.start/read/cancel/reconcile tại environment; cùng complete deny/policy/scope của IAM hiện có, không alias deployment.execute và không implicit platform access. Mỗi permission cần grant riêng, không backfill. Start còn cần release.read và current active T11 release/approval matching; approval quyết định release intent, không phải quyền deploy.

POST `/api/v1/environments/{id}/workflows` nhận schemaVersion1, releaseId, releaseRevision/hash, templateId/version, originalDeadline, requestId; Idempotency-Key bắt buộc. GET `/api/v1/workflows/{id}` đọc scoped metadata; POST `/{id}/cancel` nhận requestId, reasonCode đóng, If-Match. POST `/{id}/reconciliation-decisions` nhận immutable evidence refs/hash + expectedVersion; chỉ stopped/quiesced custody, không arbitrary observed state hoặc replay flag. Phase đầu có thể là offline owner commands với cùng rules; positive HTTP chỉ bật sau U/D disclosure tests.

202 chỉ sau durable creation/intent; duplicate same-key/hash trả original result; changed hash409; stale version412; denied403; unknown/not-owned resource404 theo disclosure policy; capacity503 với safe reason; unknown commit503 + opaque operation reference chỉ cho caller đã được admit. Không trả request body, service identity secrets hay tenant khác. GET không streaming, bounded50 history entries/response≤256KiB; mỗi page reauthorize bằng U/D. Không đưa message receipt thành public unauthenticated status API.

## Acceptance và compatibility

New WFL contract v1 chưa có consumers. Proposed consumers: API owner adapters, ControlWorker compiled handler, future DEP qua Contracts; MSG không reference WFL runtime. Không thay MSG wire v1. Template versions immutable; readiness chỉ nhận exact schema/template/registry manifest. Upgrade drain/quiesce + backup/restore rehearsal, forward-compatible expansion trước writer upgrade; downgrade từ chối nếu incompatible. SQL/ACL artifact generation không cấp quyền migrate thật.

Tests/exit gates được định nghĩa tại [T12 test plan](../engineering/t12-conformance-plan.md). Thiếu current authority hoặc actual multi-owner transaction proof thì chưa activate consumer.

## Recovery decision input is evidence selection, not authority to invent outcome

Allowed decision kinds are BindMatchingCommittedReceipt or ConfirmKnownRollback. Server resolves typed internal receipt/journal references through owner ports, independently checks backend terminal/custody, canonical operation/hash/version and immutable evidence. No caller path/URL/SQL/raw payload or arbitrary “Succeeded” override is accepted. Unknown without sufficient evidence remains Unknown, even for a maintainer. Provider outcome reconciliation needs T13's own evidence contract.

## Closed message registrations proposed

MSG envelope v1 stays unchanged; new registered typeSchemaVersion1 payloads only: `workflow.transition-requested.v1` on `workflow-transitions-v1` carries instanceId, stepId, expectedVersion, custodyEpoch, operationId, specHash, templateHash, transitionKind (Checkpoint/TimerDue/Cancel), optional timerId/generation with explicit null semantics. `workflow.transition-recorded.v1` on `workflow-results-v1` carries exact instance/step/operation, resultingVersion, specHash, closed result/state/reason. All IDs nonempty lowercase-D, hash64 lowercase hex, versions positive int64; closed payload size≤16KiB, wire≤32KiB, JSON depth≤8 and string≤256 UTF-8 bytes. Full64KiB spec stays in WFL, never embedded in transport.

Producer/consumer for transition commands is the admitted WFL coordinator; result consumer is a WFL receipt/projection adapter with no further transition/outgoing loop. Future DEP is not registered. Actual authority derives from current service/delegation/permit, not message fields. Timer receipt binding includes timer generation. Registry golden fixtures must exercise wrong scope/type/version/unknown fields and no default JSON fallback. All registrations remain disabled until T12.3 activation gates; tests may compile isolated exact fixture catalog.

## Reauthorization là command riêng, không thay immutable business spec

POST `/api/v1/workflows/{id}/reauthorizations` (schemaVersion1, requestId, If-Match) chỉ cho original actor hiện được xác thực, workflow.start + release.read trên exact environment, state BlockedAuthority và không unresolved transaction/step. Owner revalidates release/approval/resources/policy hiện hành; tạo delegation generation mới và append authority-binding history + AUD trong một root, invalidate generation cũ/advance authority epoch. DelegationId/permitId là authority history, **không nằm trong immutable business specificationHash**; các resource/release/template hashes và original workflow deadline không đổi. Không đổi actor, reset scheduler/retry budget, resurrect terminal workflow hoặc chuyển tenant. Actor khác cần một workflow mới được admit; không có implicit maintainer override. Approval khác chỉ được tham chiếu khi owner chứng minh nó áp dụng đúng original release revision/hash, không nhận altered release spec để resume.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.

## T12.2a structural foundation source — 2026-10-31

FEAT-WFL-001 owns the first pure Domain/Contracts identity/state/version implementation and WorkflowOrchestration.Tests, with BCL-only solution/architecture graph additions. Canonical spec/template, golden vectors and remaining12.2a work are pending; no database, permission activation, actual authority, evidence-based reconciliation or worker runtime is claimed by structural guards. Existing accepted contracts/ADRs are unchanged. Targeted validation evidence is private under .amr-local/t12-remaining-design/.

## T12.2a canonical persisted representation

Current pure implementation writes schemaVersion, ordered IDs (installation,tenant,instance,request,project,application,environment,target,release), releaseRevision/hash, environmentClass, templateId/version/hash, createdAtUtc/deadlineUtc and ordered steps (ordinal,kind,dueAtUtc). Checkpoint/Complete dueAtUtc is explicitly null. Template hash is SHA256 of closed templateId/version/ordered kind descriptor; instance dueAt is in specificationHash. Delegation/permit/trace/state/expectedVersion remain excluded as non-business history. Canonical persisted import requires byte-exact writer output (no whitespace/reordered keys/alternate GUID or time spellings); HTTP command parsing is separate and not implemented here. Golden fixture checkpoint-v1.json and its independently generated SHA256 are under WorkflowOrchestration.Tests/Fixtures. Structural state guards never supply current authority, recovery evidence or database CAS.

## T12.2b persistence preparation — 2026-11-01

Revision1 owner records/model and caller-root-only staging now exist under WFL Infrastructure. No wire/API contract changes or runtime activation. Production DDL has not been applied. The separately approved synthetic UUID-schema PostgreSQL fixture passed and verified cleanup; this does not authorize a product migration. Append-only EF guards are not a claim of final worker SQL-function ACL enforcement; that remains T12.3. See the owning FEAT-WFL-001 for implementation limits.

## Initial custody implementation conformance — 2026-11-01

PR25-WFL-CUSTODY-001 now serializes the current instance read with cancellation using FOR UPDATE before the step, in the same caller root. Owner identifiers are closed/quoted and resource IDs remain parameters. Direct locked reads deliberately bypass stale tracked snapshots. This implements the existing instance-before-step rule; no contract version, authority or takeover semantics change.

## T12.2c transition-requested codec increment — 2026-11-01

Command DTO and Host schema codec now implement the ten fields listed above. TimerDue requires nonempty timerId/positive generation, while other kinds require explicit null values for both. Canonical writer uses listed field order; unknown/duplicate/missing fields, non-lowercase-D IDs, non-integer/invalid revisions, invalid hashes/kinds and authority-shaped extra fields reject. No native registration or handler is activated, and data is not authority. Result/recovery protocols and actual multi-owner qualification remain pending.

## Inactive T12.2d timer owner increment — 2026-11-01

The first timer staging port takes an owner-resolved identity, exact WaitUntil step/operation/specification hash, instance/step CAS and worker/boot/epoch/custody version. It grants no authority. It uses the current fixed pilot template (WaitUntil ordinal1), canonical due time and original workflow deadline; prior steps must have succeeded. One caller root stages Running→WaitingTimer for instance/step, settles the exact active custody with its operation receipt reference, and appends timer/transition evidence. Existing custody storage codes1(active) and3(settled) are an internal mapping, not a new takeover or provider state. The initial timer generation is1; successor-generation dispatch/recovery remains later D work. Same-operation duplicates return the original timer reference/workflow version; changed immutable binding or original workflow version conflicts. No standalone commit or runtime registration is added.

Due discovery is metadata-only, installation/tenant scoped, ordered by due/time ID and bounded1–100. A returned row does not authorize dispatch or imply the parent is executable: the scheduler still needs current owner state, original deadline, clock/floor, pending/unknown/custody reconciliation and authority gates. Pure due-window calculation keeps each newly admitted wake≤120s and at least one tick before the original workflow deadline. Attempt arithmetic cannot exceed10 or provide permission to retry. Timer due identity/time and persisted attempt monotonicity are guarded; active/unknown history deletion remains forbidden. The separately approved actual owner-store DB fixture now passes rollback/commit, original duplicate, wrong scope/custody/hash/order, stale CAS/cancellation and102-row bounded discovery with namespace cleanup. This qualifies the inactive initial waiting-timer owner port only; paired AUD/MSG wake dispatch, full D/E, current worker authority and positive activation remain incomplete.

## Canonical time versus scalar persistence precision

Canonical workflow JSON retains its seven fractional digits and original hash. Scalar timestamp equality checks at the timer owner boundary run through typed PostgreSQL parameters, matching the provider representation used at insertion. This applies to creation/deadline and duplicate due-date checks, and (PR29-WFL-TIMESTAMP-002) to the owner metadata projection's deadline binding and successor staging's creation/deadline/original-timer due checks. It is not a time tolerance or a clock/deadline extension: an adjacent persisted microsecond remains a conflicting binding. Existing schema revision1 and wire/canonical encodings are unchanged. Actual DB regression covers sub-microsecond dates on both sides of the PostgreSQL epoch with original-byte/hash preservation.

## Inactive native wake representation

D2 implements an internal canonical schemaVersion1 descriptor (19 closed fields) with immutable instance/request/step/timer/generation/attempt/epoch, distinct scheduler and wake operations, delivery ID, independent workflow/step versions, spec/template hashes and original times. It is recovery data, never authority. Host wire preparation maps it to the already accepted transition-requested v1, preserving identity/deadlines, without consumer invocation or registration in production. An opt-in revision2 owner model adds only wake_operations; original revision1 remains intact. No wire contract version, grant, worker activation or operation permission changes. Persistence/dispatch/actual recovery qualification is not complete.

## Initial scheduler staging conformance — pending actual qualification

The inactive typed initial-dispatch path checks the locked current WaitingTimer owner and settled predecessor, advances instance/timer CAS, preserves the step version and appends scheduler history/custody plus immutable wake binding. The wake expects the resulting workflow version. Its distinct operation/message IDs are paired only through this typed path on the same WFL/AUD/MSG root; generic publication binding is not relaxed. One new confirmed root commit is required before native flush. Duplicate owner evidence rolls the fresh root back rather than republishing. Generation/attempt/epoch successors, current service authority, clock-source qualification and production activation remain separate incomplete work; no new permission or wire schema is introduced.

## D2 initial root actual result and recovery continuation

The initial producer-only timer-root fixture now passed with real WFL/AUD/MSG and cleanup under exact operation approval. An additive owner-only wake receipt expectation binds the existing scheduler expectation to timer/generation/descriptor hash. The read-only inspector matches canonical bytes, immutable scheduler transition and settled custody; MatchingObserved reports WFL facts only, not current authority, complete multi-owner reconciliation or predecessor terminal proof. No public wire schema or execution permission changes.

## TimerDue receive path — implementation in progress

A new internal delivery expectation transports the already accepted envelope/payload facts without adding wire fields or authority. The inactive TimerDue owner path matches them to retained canonical wake data and completes the side-effect-free WaitUntil step through its existing state graph inside one caller root. Workflow returns to Running for the next compiled checkpoint; this is not workflow success or deployment verification. Original duplicate evidence cannot publish or claim another generation. Current worker authority, actual native receiver and successor qualification remain pending.

## Initial compiled transition-recorded encoding

The inactive TimerDue result codec writes exactly instanceId, stepId, operationId, resultingVersion, specHash, result, state, reason. The current compiled tuple is Completed / Running / TimerDueElapsed; unknown tuples reject, and no generic result deserialization is enabled. Payload resultingVersion is workflow expectedVersion+1, while envelope stepVersion is the completed WaitUntil version (original stepVersion+2 through Running then Succeeded). Envelope operation is the original wake operation; correlation is original requestId and causation is the input wake messageId. Occurrence/deadline remain the original wake window. The sole root validates this tuple and owner binding before publication. This implements the already accepted v1 result registration, not a new wire envelope, state/permission model or deployment success. Actual receivers/consumers and further tuples need their own applicable qualification/compatibility gates before activation.

## D2 native receive/redelivery batch preparation

The compiled lifecycle fixture will check native receive, atomic inbox/WaitUntil/AUD/result, committed owner/MSG inspection and same-envelope redelivery across sequential host objects. Both are in one test process; this is not separate worker/process takeover or off-Mac evidence. The fixture remains operation-gated and production registration remains disabled.

## D2 native receive/redelivery actual result

The exact native fixture now qualifies the current initial scheduler→native receive→pure WaitUntil completion→AUD/floor/result/inbox path and same-envelope redelivery without repeating the handler. This is the frozen isolated test profile only; no production service authority, default consumer activation, process takeover or successor permission follows. Missing owner/MSG/AUD/terminal evidence continues to require reconciliation rather than replay.

## D2 combined recovery reader preparation

The inactive combined recovery reader compares the original scheduler WFL/MSG/AUD evidence and completed wake WFL/logical/result/AUD evidence. A matching result still grants no replay/takeover or terminal-backend proof. Missing evidence remains StillUnknown; no boolean absence-to-rollback conversion is added. The clock comparator clarification is tracked in the owning feature; existing accepted clock wording is preserved until the owner answers.

## Owner clock clarification — 2026-11-02

NukeGeng explicitly confirmed “Chốt theo mẫu thời gian hiện thời; floor không lùi”. This resolves the ambiguous comparator wording above without changing the one-second bound, immutable deadlines or operational rights. The new clock guard/descriptor-bound monotonic budget and checked root floor path are implemented but still need actual clock/fault qualification; worker startup must load its owner floor before positive activation. Older missing-operation observations remain StillUnknown, not rollback or replay evidence.

## D2 exact transaction provenance preparation

The inactive timer root now exposes opaque in-process provenance capture from the actual transaction, not caller-provided PID/XID. Its independent probe observes full transaction status and conservatively blocks on prepared transactions, changed server generation or unavailable status. An observed terminal transaction is not worker quiescence, complete logical-operation outcome or permission to replay; all owner/MSG/AUD/native-attempt/authority gates still apply. No origin import across restart is implemented yet.

#### D2 clock/provenance fault qualification boundary

The prepared consolidated fixture distinguishes confirmed root rollback, monotonic operation expiry before COMMIT and genuinely unknown COMMIT response. Terminal PostgreSQL observation and matching WFL/MSG/AUD receipts remain observations, never a command to replay, reopen admission or advance generation. Synthetic `TimeProvider` advancement is test-local; no OS/DB clock change is required. Until that new fixture is separately approved and executed, these paths have source/ordinary-test evidence only; the previous frozen DB receipts do not qualify newer source.

#### D2 scheduler observation consistency correction — 2026-11-03

An independent scheduler read can observe the parent before the expected transaction commits. An older version without the exact descriptor remains StillUnknown, not Conflict or confirmed rollback. When that descriptor appears between READ COMMITTED statements, refresh the parent version before classifying inconsistency. A visible descriptor with a still-inconsistent current parent remains Conflict. This does not grant replay or relax canonical binding, transaction outcome, authority or custody gates. Fault attempt01 stopped earlier at the fixture's exact-floor guard and does not prove these newer assertions.

#### D2 actual producer fault evidence — 2026-11-03

The owner-approved revised fixture now verifies actual active/rolled-back/committed root observations and exact WFL–MSG–AUD scheduler receipts, including StillUnknown at older precommit parent state. Checked DB clock/floor plus synthetic monotonic precommit expiry commits no effects and leaves admission open. Real COMMIT-response loss remains Unknown at the root, keeps admission closed and forbids flush even when independent readers observe the original committed transaction and matching effects. Cleanup passed. This is bounded producer evidence, not restart provenance, consumer fault qualification, authority or permission for successor/replay.

#### D2 next native receiver qualification boundary

Producer lost-response proof does not qualify the consumer root. The prepared receiver fault case must independently observe a fully staged but uncommitted completion as StillUnknown; after actual COMMIT-response loss, the root remains Unknown/closed and original inbox/WFL/logical/AUD/result facts must reconcile on a separate connection. Hooks are test-only and do not change wire semantics, authority, production registrations or Unknown failure policy. New source/binary is not covered by the earlier frozen producer pass; receiver execution awaits its own operation approval. Full D2 cancellation/recovery/retention remains incomplete.

#### D2 receiver response-loss evidence — 2026-11-03

The exact owner-approved receiver fixture now passes actual native execution: independent precommit StillUnknown, root Unknown after actual COMMIT-response loss, admission stays closed, and original committed inbox/logical/WFL/AUD/result facts match on an independent connection. Original result ID/hash and one owner effect were asserted, then owned hosts stopped and schema absence was independently verified. This qualifies the bounded receiver transaction/observation path only; previous pending-execution wording for this case is superseded. No production failure policy, process-restart proof, successor/replay permission, or full D2 acceptance is implied.

#### D2 cancellation intent storage refinement — 2026-11-03

The inactive owner implementation persists cancellation intent separately from terminal workflow state. Opt-in schema revision3 adds append-only cancellation_intents within WFL ownership, without changing the original business spec, wire v1, accepted graph or earlier schema models. The first supported closed reason is OperatorRequested. The command hash binds exact trusted scope/instance/request/actor/spec hash/expected version; receipt UTC is not part of retry content. A known matching request returns its original intent result, even after later state changes; a changed binding or replacement request conflicts.

The owner stages the intent and CAS in the caller root; its coordinator must supply current workflow.cancel admission and atomically append AUD/floor. Pending and Reconciling may keep their existing graph state while the sticky intent prevents new effects. Intent neither resolves Unknown nor declares confirmed effects undone. Fresh owner transition/custody/timer paths must use the cancellation-capable model and check under the current instance lock. Terminal Cancelled requires no unsettled owner custody, Unknown step or published timer; additional native/predecessor reconciliation is not inferred from these structural checks. Legacy fixture factories and inactive Host profiles are not permission to activate cancellation across mixed writers; current authority/SQL-function ACL coverage remains T12.3.

Additive SQL/source and no-IO tests are preparation only. The new cancellation database case needs separate immutable operation approval; public cancel/reauthorization/reconciliation APIs remain unactivated.

### D2 correlation of original transaction and receipts

The isolated recovery reader binds the original transaction to the fixture, workflow identity, operation ID and wake descriptor hash. Scheduler and completion require their respective origins. The reader first observes a terminal root and verifies that its original backend has exited. A committed root plus matching receipts confirms the original commit. An aborted root with matching receipts conflicts with that attribution. An aborted root with incomplete receipts leaves the logical outcome unresolved. Successor admission still requires exact custody and quiescence evidence.

Fresh creation checks the caller-provided effective UTC against the immutable half-open creation window after looking up the exact request receipt. Missing receipts at or after the original deadline produce RecoveryHorizonExceeded. Matching retained receipts still return the original result. This guard limits admission and leaves the minimum retention duration in force. Owner transition staging locks the instance before reading operation history, so a retry waiting for a concurrent commit can observe the original receipt.

The isolated custody fixture derives its registered advisory key from a fixture namespace and the installation ID. It owns a dedicated PostgreSQL session with pooling disabled, checks the original backend before admission, and closes admission when the session breaks or is disposed. The isolated process test verified PostgreSQL contention, acquisition after owned child exit and release on successor disposal. Successor execution also requires reconciled transaction and owner evidence.

The revision 3 recovery reader distinguishes an exact published waiting state without receiver history from an unavailable or inconsistent owner read. The observation requires a disconnected aborted receiver root, matching original scheduler evidence and exact missing completion receipt keys. It does not combine owner reads into an atomic snapshot or establish native delivery settlement. The in-process origin now binds the staged audit identity and canonical hash, the publication delivery and binding hash, and the consumed delivery and binding hash when present. Reconciliation rejects substituted evidence before probing the original root. Successor staging still needs current custody, native settlement and owner-lock revalidation.

The structural successor validator preserves the original workflow specification and step version, advances generation/attempt/epoch/workflow version once, and rejects reused operation or delivery IDs. It requires the predecessor deadline to have elapsed and the new occurrence to equal the effective instant. The existing original-workflow deadline and ten-attempt limit still apply.

The isolated successor composition revalidates terminal provenance, matching scheduler receipts, exact waiting state and no receiver logical/result effect under the owner instance lock. It additionally requires native Expired quarantine and current dedicated-session custody. A terminal committed scheduler can supply predecessor provenance when the delivery expired without completion; an attempted receiver requires its disconnected aborted origin. The opaque in-process evidence retains the original binding and hash. It does not establish distributed service authority or survive process restart.

The isolated committed-scheduler/no-completion successor path has actual passing integration evidence. Original canonical bindings and settled native delivery preceded successor creation under owner lock. Atomic root rollback/commit and exact duplicate/no-republish were verified. This does not qualify an unknown receiver, independent worker authority or OS-process recovery.

The isolated WF04 profile has actual process-crash evidence for the four accepted producer/consumer root boundaries. This evidence covers committed intent recovery and exactly-once logical outcome under the fixed synthetic profile. Separate current authority and independent-host qualification remain required.

The owner store rejects every terminal transition while custody is unsettled, a step is Unknown or a published timer remains unresolved. For a disconnected aborted receiver, successor observation may use a uniquely handled native inbox delivery with an exact expired journal entry and no conflicting DLQ or pending copy. Receiver provenance and independent absence of logical, result and audit effects remain required.

The approved successor guard batch passed both native settlement variants, terminal denial while a timer is unresolved, and original receipt recovery after the horizon. The T12.2e owner read projection now scopes by installation, tenant, instance and resolved project/environment. It returns at most 32 steps and 50 history entries with an exclusive version cursor and checks the workflow version again before returning. The IAM exact-environment eligibility reader is implemented. Command-level admission, durable U/D disclosure and live projection qualification are pending.

### T12.2e Host disclosure composition

The inactive Host coordinator requires action-specific policy allow evidence for the exact workflow scope. Each page requires durable U before reading and durable D covering the exact payload digest and length, including an authorized not-found payload. It revalidates authority and checks the serialized projection again before D. The handoff checks session, route, cursor, scope, expiry, byte limit and hash, and permits one non-cacheable response. An unresolved audit commit closes admission only when commit was attempted. The policy port has no runtime implementation or fallback to the existing target.read policy. No HTTP route or listener is activated by this slice.

### T12.2e scoped cancellation transaction

The inactive Host root stages the human cancellation intent, workflow version CAS, exact mutation audit and trusted floor in one transaction. The owner validates the current project and environment under the instance lock before looking up any duplicate. A matching duplicate returns the original version and rolls back the fresh root. Cancellation cannot consume or publish a message. Other owner mutation branches retain their existing publication requirements. The caller still owns current policy, session/grant checks and protected response disclosure. No command or route is registered.

### T12.2e cancellation transport isolation

The isolated cancellation transaction now accepts only a caller PostgreSQL root and uses WFL/AUD directly. It does not enlist a native message context. Current eligibility is checked before owner work and again before commit or duplicate receipt return. If authority is lost before commit, the intent, audit and floor roll back. This fixture path does not register a runtime command.

### T12.2e start eligibility

The internal start eligibility selection is owner-resolved. APR resolves the unique request for the exact release revision within installation, tenant, project and environment. It is separate from the proposed HTTP payload. The reader binds both permissions to the same current session and project owner and caps the returned facts at the earliest observed grant or approval limit. The facts do not authorize creation, provider execution or a transferable delegation.

### T12.2e scoped timer reconciliation evidence

The inactive timer receiver reader accepts a closed decision selection and separately resolved typed owner evidence. It checks tenant, project, environment, instance, operation, expected version and evidence digest before entering fixture custody. Matching commit requires terminal origin plus matching WFL/MSG/AUD receipts. Known rollback requires a disconnected aborted origin and exact absence of completion effects. The version check binds the selected historical effect. A future decision writer must independently compare the current owner version under its root. Current workflow.reconcile policy, durable decision append and audited disclosure still gate activation.

### T12.2e start retry fingerprint

The internal start command computes a scoped key hash from installation, tenant, actor, action and exact idempotency key. Its separate business hash binds project, environment, request ID, release ID/revision/hash, template and original deadline. Server-generated instance IDs and timestamps are outside both hashes. A future durable receipt lookup must compare both before allocating IDs, then verify current authority before returning the original result. This implementation defines the fingerprint only.

### T12.2e start receipt storage

The opt-in revision 4 owner model adds an append-only start receipt linked to the original request receipt. It keys by installation, tenant, actor and scoped key hash and uniquely binds the original request ID within installation/tenant. The owner checks the business hash and canonical original specification before returning its original instance/version. A duplicate lookup precedes the specification factory. Fresh creation and its start receipt share the caller root. Existing request receipts carry no inferred start key. DDL is an offline artifact and has not been applied.

### T12.2e start validation

The inactive start decoder accepts only the eight v1 business fields with a canonical UTC deadline and one bounded idempotency header. The environment route value is a selection. Tenant, actor and approval remain server-resolved. No route is registered.

### T12.2e isolated start root composition

The opt-in Host start fixture root stages the exact start receipt and workflow through the owner store, checks current admission before and after staging, and rechecks before commit. A duplicate rolls back the new root and returns its original owner result. A fresh start requires its exact actor/environment/command mutation audit, matching clock floor and the first compiled checkpoint publication. The generic creation entry point is disabled on the revision 4 start profile. This fixture entry point has no runtime registration or IAM delegation writer; it does not qualify distributed start authority.

### ADR-0024 Host policy composition

Host workflow policy evidence now includes a stable digest of current owner, binding and policy facts. The disclosure coordinator compares that digest and revision identity separately from the fresh validity cap. A changed owner/classification or policy content denies the in-flight handoff.

The local command eligibility snapshot binds exact action/scope and current IAM/PRJ evidence. Later evidence may shorten validity but cannot revive an expired original snapshot. Admitted isolated cancellation derives its audit from that snapshot and rechecks it before owner commit and duplicate return. The snapshot itself is not a transferable permit or worker delegation.

### Operator reconciliation command identity

The closed reconciliation kind is owned by WFL Domain. The immutable operator command binds installation, tenant, instance, request, authenticated actor, operation, expected owner version, decision kind, evidence hash and canonical specification hash. Its hash excludes server receipt time. The Host selection is bound to a server-resolved owner specification before persistence. This identity confers no authority and cannot substitute for current custody/evidence validation. Revision 5 now prepares durable operator decision storage; audit/custody pairing remains pending; the isolated owner database fixture is qualified separately.

### T12.2e operator decision receipt preparation

The opt-in revision 5 owner model adds only `operator_decisions`; revisions 1–4 remain unchanged. Its scoped request key pins actor, operation, exact decision kind, command/evidence/specification hashes and original/resulting owner versions. A unique scoped operation/kind prevents recording the same decision under multiple requests. The caller must supply a clean root and server-resolved project/environment. The store locks the owner, checks canonical scope, rejects terminal owners for fresh decisions, validates current evidence through the caller callback, and advances the expected version exactly once before appending the immutable receipt. A matching retry returns its original receipt; changed bindings fail. This metadata operation neither changes workflow state nor publishes or replays an effect.

Offline SQL and model/guard tests are implementation evidence only. Current authorization, custody, AUD/floor atomicity and unknown-commit handling must be paired by the coordinator before activation. The exact approved revision 5 PostgreSQL fixture passed rollback/commit, duplicate, changed actor/hash, wrong tenant/project, rejected evidence, stale CAS and operation uniqueness; a separate read-only connection verified cleanup. This does not qualify concurrent operator races, paired AUD/custody, or applying the SQL to an installation.

### T12.2e paired operator reconciliation composition — 2026-11-07

FEAT-WFL-001 / MOD-WFL adds inactive Host `WorkflowReconciliationTransaction`: exact server owner/session selection, workflow.reconcile admission/policy, retained fixture custody, receipt/CAS + AUD/floor on one root, final authority/evidence checks, duplicate rollback and existing Unknown-commit classification. `WorkflowReconciliationTransactionTests` proves audit substitutions and missing custody reject before DB access; targeted Host/recovery/classification/architecture tests passed 40 cases without skips. `WorkflowReconciliationDatabaseTests` prepares real owner/AUD/lease atomicity with synthetic authority/evidence callbacks; export passes offline, live execution awaits exact new fixture approval. No API registration, delegation protocol, provider operation, architecture/ADR change or distributed-fencing claim follows. Real admitted paths and original timer-evidence integration remain pending.

The pre-commit guard checks admission/custody after asynchronous authority/evidence callbacks, and duplicate receipt return likewise checks after its authority callback. The unexecuted paired fixture proposal was superseded by v2 adding final-callback gate-closure rollback; target/SQL/key/budgets are unchanged. Current-source targeted checks passed 41 cases, no skips; live v2 remains pending operation approval.

### Qualified isolated reconciliation root and inactive operator selections — 2026-11-08

The owner-approved frozen paired fixture v2 passed actual WFL/AUD/floor commit and late-denial rollback, including admission closing during the final callback. Duplicate appended no second AUD. Independent read-only checks verified cleanup of both exact namespace and installation advisory key. Authority/evidence callbacks were synthetic; actual session/grant and typed timer-origin integration remain unqualified. The receipt binds the executed binary and is not approval to replay after subsequent builds.

The inactive cancel decoder accepts exactly schemaVersion1, requestId and reasonCode=OperatorRequested, plus a caller-decoded If-Match version in [1, Int64.MaxValue-1]. It does not define HTTP ETag syntax. The inactive reconcile decoder accepts exactly schemaVersion1, requestId, operationId, expectedVersion, kind and evidenceHash. Kind is one of the two approved symbolic names; evidenceHash is lowercase hex64. The route instance binds to the server owner; tenant, actor, canonical specification and evidence contents never come from the body. JSON is bounded with duplicate/unknown fields rejected. These are selections, not authority or active routes. Current-session/owner U/D and T12.3 activation gates remain required.

### T12.2e persisted session/grant fixture preparation — 2026-11-08

FEAT-WFL-001 / MOD-WFL adds `WorkflowPersistedAuthorityDatabaseTests` to qualify actual IAM current-session/principal, immutable role, TEN membership/grant and PRJ policy/scope reads for workflow admission, cancellation and audited read. It seeds synthetic persisted authority in one new UUID schema; invalidations are fixture updates, not proof of production writer-fence coverage. No login issuance/startup/real-account grant or runtime host is added. Offline build/export and current pure authority/policy regressions passed 49 cases; new live execution requires exact fixture operation approval. Actual timer-origin reconciliation, REL/APR start and T12.3/T12.4 remain open.

### Persisted-authority fixture seed correction — 2026-11-08

The first approved fixture attempt failed on the IAM required password_hash column while seeding the first synthetic principal, before current-authority assertions. Cleanup completed and an independent read-only connection verified namespace absence; failed evidence is preserved. The fixture now supplies an opaque synthetic password hash and explicit security/concurrency stamps required by the unchanged IAM model. An offline model-metadata regression checks every required principal field without database access. Fifty offline/export/authority/policy cases pass, zero skips. SQL/UUID/target/resource limits and scenarios remain unchanged; the changed executable awaits exact delta operation approval. This is a fixture correction, not a Product authentication change or evidence that the live authority test passed.

### Persisted-authority fixture v2 qualified — 2026-11-08

The exact owner-approved v2 executable passed one live test (5 seconds, no skips). It used real IAM current-session/principal and immutable-role readers, TEN current membership/grant resolution, PRJ owner/scope/binding and IAM policy readers. Each candidate initially admitted all four explicitly provisioned actions; current revoked/expired session, disabled principal, revoked/expired assignment and retired definition denied, and wrong tenant denied. An active session without membership/assignment could not acquire workflow.cancel. Actual admitted cancellation committed WFL intent with AUD/floor; actual persisted-authority read completed U/D, while a newer policy head published after U prevented disclosure. Final AUD counts matched the expected committed mutation and reads. Independent read-only cleanup confirmed the namespace absent. The failed v1 execution and its cleanup evidence are retained.

This verifies seeded synthetic persisted sessions and grants, not login issuance, enrollment, boot startup or real-account permission changes. Fixture invalidation updates are not production writer/fence evidence. The run did not execute a native host, provider or original timer reconciliation; REL/APR start and original-evidence reconcile integration plus T12.3/T12.4 remain open. The latest stable full gates passed docs/scope/tooling/clients/restore/build/mapping but product-test still failed with 553 missing isolated-fixture configuration errors. This targeted qualification does not override that failure or mark T12 complete.

### T12.2e original-evidence admitted reconciliation fixture preparation — 2026-11-08

FEAT-WFL-001 / MOD-WFL prepares `WorkflowReconciliationOriginDatabaseTests` plus a test-only persisted-authority adapter. It combines the existing native receiver COMMIT-response-loss boundary and original typed WFL/MSG/AUD evidence with actual session/role/TEN-grant/PRJ-policy admission and a retained quiesced fixture custody lease. It checks rejected evidence/result/rollback substitutions, one metadata receipt/AUD without message replay or reopening the worker gate, duplicate idempotency and post-revocation receipt denial. Two fixture hosts run sequentially, then both stop before administrative reconciliation. No Product runtime registration or dependency changes. Forty-one offline/export/recovery/audit-guard/classification/architecture cases pass; real execution requires exact new SQL/binary/host/custody operation approval and is not yet qualified.

### Admitted original committed-timer reconciliation qualified — 2026-11-08

The frozen owner-approved origin fixture passed one live test in 3 seconds with zero skips. Native producer and receiver ran sequentially; the actual receiver COMMIT response was lost, the original root was independently observed committed with its backend disconnected, and both hosts stopped before administrative custody. Actual persisted-session/role/TEN-grant/PRJ-policy admission and the typed original WFL/MSG/AUD evidence authorized one metadata decision (owner version 6 to 7). Substituted evidence/result and a rollback claim against that committed origin rejected without mutation. Duplicate returned its original receipt without another audit or publication; revoking the persisted session then denied receipt retrieval. Original native handler count remained one, native publication receipts did not grow, and the original Unknown worker gate stayed closed. A separate read-only connection verified namespace absence and advisory-key release after cleanup.

This qualifies the BindMatchingCommittedReceipt path with original evidence and current seeded persisted authority. A positive ConfirmKnownRollback decision, full current REL/APR start/root proof, remaining command recovery/concurrency acceptance and T12.3/T12.4 are not established by this case. Login issuance/startup, production invalidating writers/global fencing and provider operations remain outside its scope. Forty-one offline/export/recovery/guard/classification/architecture cases passed. Stable full gates still fail product-test on 553 missing fixture configurations; targeted qualification does not override that gate or complete T12.

### Known-rollback original-evidence fixture preparation — 2026-11-08

FEAT-WFL-001 / MOD-WFL adds a separately scoped rollback-origin integration case: fail receiver before actual COMMIT, deny a rollback inference while backend is unresolved, then require Aborted/disconnected origin plus exact absent completion effects before actual admitted ConfirmKnownRollback metadata. It preserves no replay/new publication, one metadata/AUD decision on duplicate, and denial after persisted-session revocation. Shared test authority uses closed case identities and separate EF schema models; both original-evidence classes serialize through the existing nonparallel Product collection. Forty-two offline/export/guard/recovery/classification/architecture tests pass, zero skips. The new live case remains pending exact schema/executable/native-host/custody approval; prior committed proof remains historical and is not replayed. Full start/recovery/concurrency/T12.3/T12.4 and global product gate requirements remain open.

### Actual admitted known-rollback original evidence qualified — 2026-11-08

The exact owner-approved rollback fixture passed one live test in 3 seconds with no skips. A synthetic receiver exception occurred after staging but before actual COMMIT; no receiver commit outcome was emitted. While the original backend was unresolved, the reader rejected an aborted-outcome claim. After both fixture hosts stopped, an independent reader confirmed Aborted/disconnected origin and exact absence of WFL/MSG/AUD completion effects. Actual persisted-session/role/TEN-grant/PRJ-policy admission plus dedicated custody then recorded ConfirmKnownRollback metadata once (parent version 5 to 6), without adding a completion transition, result publication or handler execution. A false committed claim and substituted evidence/result rejected; duplicate added no AUD/publication and persisted-session revocation denied receipt access. Independent read-only cleanup confirmed exact namespace absence and advisory-key release.

The 42 current offline/export/recovery/guard/classification/architecture cases also pass. This is metadata reconciliation of a known rolled-back owner root; it is not proof of native settlement, successor execution permission, OS-process crash handling or Product runtime activation. Full current REL/APR start, remaining command recovery/concurrency evidence, T12.3/T12.4 and applicable global gates remain required. No company/provider/Clients/Website operations, new dependency or ADR changes were introduced.

### Current start admission before allocation — 2026-11-08

FEAT-WFL-001 / MOD-WFL adds inactive `WorkflowStartAdmissionReader`/opaque evidence and trusted Host `IWorkflowStartEligibilityReader`. The real adapter binds installation plus exact selection; old/unbound eligibility cannot authorize start. Environment workflow.start policy now has a typed preallocation request scope and shares the existing policy evaluator, preserving the scoped-key-before-instance-allocation rule. The join matches workflow.start + release.read current subjects/grants, owner revisions/classification, approval/release/app/target and policy/binding digest. Stable command binding and original expiry caps survive refreshed observations; substituted or expired facts deny. Existing instance-based read/cancel/reconcile callers retain their scope behavior.

The start root admission guard checks closed state before and after awaiting authority both at staging and before COMMIT; actual gate-close-during-callback transaction proof remains to be added to the full start fixture. Seventy-seven targeted synthetic-eligibility/join/expiry/negative/parser/policy/architecture/classification/offline-export cases pass, no skips. This is composition evidence, not actual REL/APR/root/delegation or Product activation proof. No wire/persistence-schema/technology/ADR changes, migration, infrastructure effect, reviewer dispatch or Clients/Website changes.

### Retained admitted-start root binding — 2026-11-08

FEAT-WFL-001 / MOD-WFL implements inactive `WorkflowRootTransaction.StageAdmittedStartAsync`. One root operation guard spans current snapshot revalidation, lazy allocation and retaining the typed admission. It checks root installation/current session, derives the stable command, and binds a fresh spec to exact selected project/environment/release revision/hash plus admitted application/target/classification and creation window. The owner store also validates the canonical original against current owner binding on key retry without allocating a new spec or applying the fresh-creation clock window to historical data. It does not append a new mutation audit on duplicate.

`WorkflowStartAdmission.CreateMutationAudit` produces exact actor/resource/request/invocation/policy/assignment fields; the root rejects substituted invocation or policy/assignment revisions in addition to its existing command/spec audit checks. The existing generic isolated start seam remains for prior fixtures, with shared core logic and the same serial/root/Unknown semantics. No default WaitUntil schedule, delegation, API, consumer or new wire/persistence schema is introduced.

Current synthetic join/owner-binding/creation-window/audit-factory/parser/eligibility/policy/architecture/classification/offline-export tests pass 87 cases, no skips; owner-store/command regressions are run separately. These do not prove the new typed root against actual REL/APR/WFL/AUD/MSG on PostgreSQL. That integrated fixture, audit-substitution/late gate-close/duplicate concurrency and Unknown recovery qualification remain required. Existing frozen live receipts are historical and have not been replayed after source changes. ADR-0021/0024 remain unchanged.

### Current start qualification evidence — 2026-11-08

The [owner feature](../features/workflow-orchestration/features/durable-workflow.md#five-cycle-actual-start-fault-qualification--2026-11-08) records exact approved live start/duplicate, three gate boundaries, contention and actual COMMIT-loss observations with independent cleanup. These qualify only the inactive isolated compositions on their recorded revisions. No wire or authority contract changed; observation grants no replay/reopen or operator recovery action. Cancel/reconcile metadata-root COMMIT-loss qualification is prepared separately and remains unverified until its own exact operation approval and execution. T12.3/T12.4, applicable full gates and runtime activation remain open.

### ADR-0025 test-only regression selection

Accepted ADR-0025 permits additive explicit disposable target bindings through isolated and offline test compositions. Resource-policy and approval-policy test parsers/canonical bytes retain the exact selected database and test role; their default parsers preserve existing input/canonical behavior. This changes no workflow wire schema, business permission, authority provenance or commit outcome semantics. No Product entry point reads the private test-runner descriptor; its source/manifest identity does not grant operation rights.

ADR-0025 regression composition also retains connection-pool saturation coverage through a separate AUD single-connection-pool test entry. This is a test options selection, not a workflow wire/permission change. Captured fixture DI uses the same owner services/transactions and retains injected fault factories; default Product startup never reads the regression descriptor.

Native fixture selection under ADR-0025 preserves the real root/probe/custody/reconciliation paths and source schemas. Independent connection creation validates the selected target before opening it; SQL helpers shared with exports use explicit offline metadata selection. This changes neither workflow state transitions nor current-authority/evidence requirements and does not grant native host, process-kill or database-operation rights.

The disposable regression profile also covers offline release and approval receipt recovery in fixtures. The selected DB is validated before opening, and the existing custody and receipt evidence rules apply.

## Reauthorization clarifications — 12.2f (owner decisions D3/D4/D6, 2026-11-12)

No shape change. `delegationId` is authority history stored in `authority_bindings`, not in the instance record or specificationHash (D6). A successful reauthorization only appends the next binding generation and keeps `BlockedAuthority`; `Running` follows only a later admission under the current binding (D3). If-Match is the workflow version; idempotency is `requestId` + command hash recorded on the binding row, schema revision 6 (D4). Generation 1 is written with the IAM delegation at start (12.3b).
