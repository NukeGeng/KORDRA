---
id: CONTRACT-DURABLE-MESSAGING-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-30
---

# Durable messaging v1 — hợp đồng kernel T12.1

> Accepted ngày 2026-10-30 theo exact scoped acceptance ở cuối file. Chỉ phạm vi T12.1a–f; chưa triển khai hoặc cấp quyền vận hành thật.


Phạm vi [ADR-0020](../decisions/0020-messaging-runtime-adoption.md), FEAT-MSG-001. Đây là phiên bản đầu **đề xuất**, chưa có public consumer production; không thay contract authorization, release hoặc audit đã accepted.

## Ownership và entry points

MSG sở hữu delivery envelope, native transport metadata, logical receipt và quarantine metadata; business owner sở hữu state, expected version và outcome nghiệp vụ. Infrastructure factory nhận connection/transaction của caller, kiểm tra cùng connection/database/schema identity và transaction còn mở. Contracts chỉ nhận typed message descriptor, immutable scope và outcomes; không nhận delegate SQL tùy ý, raw CLR type, connection string hay client-selected schema/queue.

Publish có hai kết quả khác nhau: `Staged` chỉ tồn tại trong transaction; `Durable` chỉ có sau caller commit được xác nhận. Outcomes kỹ thuật gồm Rejected / Unavailable / Unknown / Durable; reason code đóng, không chứa payload. API lookup không tự là public endpoint và không trả data xuyên tenant. Producer intent, transport ACK, logical completion và deployment verification là bốn sự kiện khác nhau.

## Envelope và canonical binding

Envelope schemaVersion=1 có: messageId, registered typeId, typeSchemaVersion, installationId, scopeKind (`tenant` hoặc `system`), tenantId bắt buộc chỉ ở tenant scope, resourceRef typed nếu loại message yêu cầu; operationId, stepId, stepVersion; correlationId, causationId; occurredAtUtc, deadlineUtc; payloadHash và payload typed.

- Registry đóng ánh xạ `(typeId,typeSchemaVersion)` → DTO, canonicalizer, route, scope rules, consumer và failure policy. Không dùng CLR name từ wire để chọn handler/assembly. Unknown type/version/content-type bị quarantine trước handler; không fallback sang serializer mặc định permissive.
- Chỉ chấp nhận canonical UTF-8 JSON theo schema type: property order được writer cố định; integer/boolean/GUID lowercase-D và UTC timestamp có định dạng cố định; reject duplicate/unknown/missing fields, floating numbers, free-form maps và string không có bounds. Hash SHA-256 trên canonical business binding, không hash raw JSON byte order hoặc serialized CLR object.
- Binding bao gồm installation/scope/resource, logical operation/step/version, type/version, payload và **original deadline**. Không bao gồm delivery messageId, trace IDs hoặc lần retry. Deadline không được làm mới bằng message ID mới. Hash nhận từ producer phải được tính lại trước handler.
- Logical key: `(installationId,scopeKind,tenant-or-explicit-system-marker,operationId,stepId,stepVersion,typeId)`. Cùng key/cùng hash trả receipt hiện có và settle delivery mới; cùng key/khác hash quarantine Conflict, không sửa receipt cũ. `system` không là null-tenant fallback hoặc quyền platform-wide.
- Tenant/actor trong envelope là claimed metadata. Chỉ trusted owner adapter sau current admission mới tạo message nghiệp vụ ở mốc được duyệt sau. T12.1 chưa có business registration nên không có cách dùng envelope để cấp quyền.

Wire envelope tối đa 32 KiB, payload tối đa 16 KiB, JSON depth tối đa 8; từng string tối đa 256 UTF-8 bytes, type-specific bounds có thể nhỏ hơn. Reject trước allocation/deserialize không giới hạn. Không password, token, cookie/session reference, kubeconfig, arbitrary shell, credential locator hoặc exception dump. Log/DLQ mặc định chỉ reason + IDs/hash; không raw body. Registry schema phải loại secret fields; hash không biến secret thành dữ liệu được phép lưu. Native unknown-body path phải scrub/drop body vào bounded diagnostic record, không tự lưu raw payload qua default DLQ; không chứng minh được bằng executable test thì consumer chưa được activate.

## Commit protocol

1. Validate registry, size/hash/scope/deadline; chưa phát sinh owner effect. Resolve caller transaction identity, không mở fallback connection/transaction.
2. Producer stages owner state, AUD/floor nếu use case yêu cầu, logical publication identity/hash và outgoing envelope cùng transaction. Một replay cùng publication identity không tạo envelope mới; key khác hash là conflict.
3. Consumer dùng database uniqueness cho logical key. Claim và terminal receipt/effect/result cùng một transaction; không commit claim riêng trước effect. Tranh chấp key chờ hữu hạn; rollback khi không thể xác định, không chạy effect song song. Native delivery identity gồm cả ID và destination/inbox identity; không retire receipt của destination khác.
4. Business owner quyết định sequence/expected version; MSG không reorder business state hoặc đoán gaps. Out-of-order step không đạt precondition không được success; defer có deadline/budget hoặc reject theo type policy. T12.1 chứng minh bằng owner fixture CAS, không thêm WFL state machine vào MSG.
5. Trước commit, stage inbox Handled/result outbox, AUD và persisted time floor trong cùng caller transaction. Commit duy nhất do coordinator giữ. Envelope memory status không được làm transport ACK trước durable commit.
6. Sau confirmed commit mới flush. Mất flush/ACK thì giữ delivery pending và recover native outbox; business effect không chạy lại. Lỗi commit có thể đã commit → Unknown, đóng đường admission theo protocol owner hiện có khi áp dụng, lookup exact key/hash qua connection mới. Missing receipt khi connection/transaction cũ chưa terminal **không chứng minh no-commit**; không mở writer cạnh tranh.
Nếu root commit trả Unknown thì coordinator/consumer không được trả success cho pipeline hoặc để generic retry policy gọi lại business callback. Đóng admission/stop consumer của host sở hữu, giữ custody tới khi operation cũ terminal và reconcile; transport redelivery chỉ được inspect receipt khi Unknown chưa giải quyết. Không gọi Dispose/Rollback như bằng chứng commit chưa xảy ra. Restart muốn tiếp tục cần evidence lookup + custody, không dựa vào timeout/lease expiry. Đây là kernel outcome contract; multi-process takeover/identity protocol vẫn là gate T12.3, chưa có automatic background consumer production.
7. Cancellation trước commit chỉ rollback nếu đã xác nhận rollback. Cancellation sau possible commit là cancellation intent, không xóa effect/receipt/envelope. Không đưa Unknown về Pending hoặc tự replay owner callback. Setup failure trước commit-attempt không được coi là unknown commit.

Không dùng owner query bằng MSG SQL, không để MSG commit AUD riêng; reject mọi enlistment khác database. Lookup receipt kỹ thuật không bypass authorization để disclose business result.

## Durable state, ACL và migration

| Store | Owner / quyền |
|---|---|
| `msg` native envelopes/node metadata | MSG; runtime chỉ quyền DML/function thực sự được migration manifest liệt kê |
| `msg_transport` ready/scheduled queues | MSG; đặt schema tường minh cùng envelope store; cấm fallback public/wolverine_queues |
| `msg` publication/logical receipts | MSG; unique key/hash, terminal records insert/select-only ở runtime; không update outcome hoặc tự purge |
| `msg` quarantine/recovery journal | MSG; append metadata an toàn; không thành business authority |
| AUD và persisted owner floor | Owner cũ; qua enlisted ports, không query/sửa bảng trực tiếp từ MSG |

Đề xuất login riêng `kordra_msg_runtime` cho host transport sau này, NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOBYPASSRLS/NOINHERIT; không là database/schema/table owner. Chỉ CONNECT đúng database, USAGE đúng hai schema, exact table/sequence/function allowlist; không EXECUTE ALL FUNCTIONS hoặc DML future-table blanket grant. Không quyền schema AUD/IAM/TEN/PRJ/INV/REL/APR hoặc PUBLIC-derived cross-database access. PostgreSQL PUBLIC privileges phải được đánh giá ở plan thực tế; không tuyên bố per-role REVOKE có thể phủ định PUBLIC CONNECT. HBA/role changes luôn cần quyền vận hành riêng.

Login này chỉ là transport identity, không dùng ghi owner state. Khi business worker được thiết kế sau, coordinator cần một identity/transaction có đúng quyền owner + enlisted MSG/AUD/floor để atomic commit; **không ghép hai login/two commits để giả least privilege**. Thiết kế authority/role đó thuộc gate T12.3, chưa cấp quyền rộng cho tài khoản transport ở T12.1.

Migrator offline bằng quyền owner hiện có được cấp riêng; sinh DDL của đúng lock graph, danh sách object/function/grants, hash manifest và schema revision. Runtime kiểm tra revision/hash read-only, AutoCreate.None, no auto-provision, không fallback create/update. Upgrade phải drain/quiesce writer trong custody đã được duyệt, backup/restore rehearsal và expand/contract plan; không rollback bằng DROP immutable history. Downgrade không tương thích → refuse startup. Không provision installation thật khi chỉ duyệt design.

## Retry, retention, capacity và recovery

- Một consumer instance trong harness; concurrency=1, native receive batch tối đa 16; một transaction tối đa 10 envelopes/160 KiB payload. Queue names registry-owned, không input tùy ý. Multi-node/business fairness để T12.3 chốt.
- Năm handler attempts tổng cộng: initial + 1/5/15/60 s durable delays; chỉ transient allowlist mới retry. Invalid type/hash/scope/secret/schema → terminal reject/quarantine, không retry. Delivery retries khác handler attempts; không dùng retry budget để suy effect chưa xảy ra.
- Deadline tối đa 120 s từ creation cho initial kernel profile; mỗi attempt kiểm tra lại deadline/effective time. Timer không mở rộng deadline, không chạy sớm sau restart. Số liệu này là giới hạn đề xuất cho pilot kernel, không SLO production. Không đổi thời gian OS để thử.
- Native handled inbox giữ tối thiểu 7 ngày; producer resend/transport recovery horizon tối đa 24 h trong profile này. Replays ngoài horizon bị reject/quarantine, không được chạy như operation mới. Logical receipts/quarantine metadata giữ ít nhất 90 ngày và **chưa có automatic purge**; AUD giữ giới hạn accepted riêng. Retention cleanup phải bounded, không xóa inbox đang active/scheduled hoặc receipt đang reconciliation. Thay horizon/retention cần review contract/config manifest trước activation.
- Readiness health và backpressure: số pending native envelopes >10.000 thì dừng admit publication mới, không drop committed work; DB/storage unavailable thì deny new work. Đây là high-water availability guard, không phải bảo đảm hard quota dưới concurrent writers. Disk budget, hard queue quota/multi-writer admission và sizing phải được chốt trước activation thật; T12.1 chỉ kiểm chứng harness hữu hạn và watermark failure path.
- Offline reconcile chỉ inspect exact operation/key/hash trong custody riêng, phân biệt Durable/Conflict/StillUnknown; không publish/redrive ngầm. Replay proposal gắn immutable spec/hash, deadline hiện hành, idempotency/custody và audit; thao tác thật cần owner approve target/scope riêng. Chưa có replay command làm effect ở T12.1. Quarantine không auto redrive.

## Compatibility và failure evidence bắt buộc

Không consumer business hiện hữu phải migrate ở v1. Producer/consumer mới phải đăng ký type version; version lạ không mặc định backward compatible. Registry/schema/lock manifest đổi có gate compatibility và migration rõ ràng, không serialize CLR names ra external contract.

Tests phải assert DB/queue/effect/receipt counts qua reader độc lập: multi-owner/AUD/floor all-or-nothing; crash từng boundary; lost COMMIT reply; duplicate delivery và same logical key/new ID; same key/different hash; reordered steps/CAS; destination identity; invalid content-type/duplicate JSON/secret/raw unknown body; wrong installation/tenant/system substitution; cancel trước/sau possible commit; expiry/timer/restart; five attempts + DLQ; retention boundary/horizon; runtime DDL/foreign-owner/cross-DB denial; startup mismatch; oversize/capacity; no body/secret logs. Có actual AUD fixture integration nhưng không claim actual business owner/authority integration khi chưa wiring. Unknown commit không được pass chỉ bằng mocked exception.


## Exact scoped acceptance — 2026-10-30

NukeGeng đã chấp thuận: **“Duyệt đúng phạm vi ADR-0020 và T12.1a–f”**, áp dụng đồng thời ADR-0020, Durable Messaging v1 và bản thiết kế tiếng Việt đã đối chiếu trùng SHA-256 với snapshot trình duyệt. Trạng thái accepted chỉ cho runtime kernel MSG, caller-owned commit/public API compatibility gate, schema/ACL design, envelope/recovery limits và guarded AUD isolated-test seam nêu trong bộ thiết kế. Các đoạn đề nghị/proposed trước annotation là nội dung proposal được giữ để đối chiếu lịch sử; approval này xác lập phạm vi thiết kế được duyệt, không phải kết quả triển khai.

Không bao gồm activation API/business worker, T12.2–T12.4, thay đổi authorization hiện tại hoặc quyền thực thi migration/role/HBA/credential/hạ tầng thật. Fixed-case operational approvals T12.0 không tự chuyển sang fixture mới. Developer bắt đầu bằng T12.1a compatibility gate; không đạt thì báo evidence và giữ các lát phụ thuộc blocked, không fallback sang Internals/native SQL hoặc tự đổi coordinator. Frozen proposal và approval receipt giữ tại `.amr-local/approvals/adr-0020-t12-1/`. Chưa có implementation/test evidence T12.1.

## Implementation clarification — transaction budget (PR23-MSG-BUDGET-001)

The accepted limits apply cumulatively to the caller's root DbTransaction, across batches, outgoing/incoming calls and all stager instances. Native publication persistence passes through the same budget; duplicate publication receipts do not stage a new envelope. Byte accounting uses the full serialized native envelope Data, including wire overhead, so untrusted payload-length metadata cannot reduce admission cost. Reservations precede native writes and remain consumed on an unresolved staging failure; the caller must roll back the root. This clarifies enforcement of the existing limits, without introducing authority, a new wire version or a commit-capable MSG path.

Budget identity additionally verifies the server full transaction ID via `SELECT pg_current_xact_id()::text` on the exact caller transaction (five-second command bound). Npgsql can recycle a transaction object after completion; a genuinely new server root gets a fresh budget, while a previously staged old stager rejects a changed root. Missing identity fails before native staging. This metadata query neither commits nor opens another connection. A fifth regression covers reused provider identity; the existing real PostgreSQL lifecycle/watermark loops cover successive roots. See [PostgreSQL transaction ID functions](https://www.postgresql.org/docs/15/functions-info.html).
