---
id: CONTRACT-DURABLE-WORKFLOW-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
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

Workflow lifetime tối đa24h, tối đa32 steps, timer due không vượt original workflow deadline. Clock dùng effective UTC không giảm + monotonic budgets; persisted floor từ cùng transaction. DB clock rollback hoặc chênh >1s so floor đóng admission; không sửa OS clock khi thử. Due query tối đa100 timers, index theo status/due/id, poll1s; gửi tối đa10 envelopes/160KiB serialized Data trong mỗi root, chia root trước staging, không dựa vào rollback savepoint để reset budget. Timer `(timerId,generation)` unique; CAS dispatch với state+MSG cùng commit, không xoá timer trước khi publish.

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

Canonical workflow JSON retains its seven fractional digits and original hash. Scalar timestamp equality checks at the timer owner boundary run through typed PostgreSQL parameters, matching the provider representation used at insertion. This applies to creation/deadline and duplicate due-date checks. It is not a time tolerance or a clock/deadline extension: an adjacent persisted microsecond remains a conflicting binding. Existing schema revision1 and wire/canonical encodings are unchanged. Actual DB regression covers sub-microsecond dates on both sides of the PostgreSQL epoch with original-byte/hash preservation.
