---
id: ENG-T12-3A-OWNER-FUNCTION-POC-SCOPE
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-14
---

# T12.3a — PoC owner function/ACL và root transaction: đề xuất phạm vi

> Accepted scoped design ngày 2026-11-14; xem Exact scoped acceptance ở cuối file. **Implemented / PoC verified (inactive)** — xem mục Implementation evidence.
>
> **Lịch sử:** Chủ dự án đã chốt D1–D6 ngày 2026-11-13 (mục 5a) và duyệt đúng nội dung mục 8 ngày 2026-11-14. Tài liệu không thay đổi [ADR-0022](../decisions/0022-controlworker-authority.md), [Worker Authority v1](../contracts/worker-authority-v1.md) hay [conformance plan](t12-conformance-plan.md). Không cấp quyền DB, role, operation hay activation. Chưa có implementation.

## 1. Mục tiêu của 12.3a

ADR-0022 đặt 12.3a là **gate trước host adoption**: chứng minh bằng PostgreSQL thật rằng mô hình "worker login quyền tối thiểu + owner-owned `SECURITY DEFINER` functions + một root transaction" hoạt động và an toàn. **Nếu PoC fail thì dừng adoption**; không được mở raw table privileges làm fallback (Worker Authority v1, mục Database rights).

Câu hỏi 12.3a phải trả lời bằng evidence:

1. Một login `NOINHERIT`, không owner/superuser/CREATEROLE/CREATEDB/BYPASSRLS, chỉ có `EXECUTE` trên các function đã liệt kê, có thể trong **một** root transaction: lock fence → consume permit → stage một WFL transition → append AUD → nâng floor, rồi commit nguyên tử.
2. Cùng login đó **không** thể: DML/SELECT trực tiếp bảng WFL/IAM/AUD, đọc/ghi schema owner khác, tạo/sửa/xoá object, chiếm quyền qua `search_path`, tự khai tenant/actor/GUC, dùng lại permit ở root khác (DB01, một phần AU02).
3. AUD append qua function **giữ nguyên** canonical hash/idempotency/floor behavior hiện có (Worker Authority v1, mục Database rights).
4. Global fence tuần tự hoá đúng: writer đã commit epoch mới trước thì worker bị reject; worker giữ fence trước thì writer chờ có giới hạn (tập con AU03, không phải writer matrix đầy đủ — phần đó là 12.3c).

## 2. Hiện trạng có bằng chứng (main `79f3228`)

- Chưa có schema `iam_workflow`, chưa có service registry, fence, permit hay delegation (12.3b).
- WFL ghi qua EF owner stores trên caller root (`WorkflowRootTransaction`, `WorkflowStore`); chưa có SQL function nào.
- AUD: `AuditIntentCanonicalizer.ComputeHash` (C#) là nguồn duy nhất của canonical JSON/SHA-256; `AuditTransactionContext` stage trên caller root.
- IAM floor: `AuthorityClockFloorStore` dùng `INSERT … ON CONFLICT … GREATEST(...)`.
- Role duy nhất có tiền lệ: `kordra_msg_runtime` (MSG transport-only, ACL review artifact `MessagingAclReview`). Không có `SECURITY DEFINER` function trong repo.
- PostgreSQL local fixture: 15.x (có `pg_current_xact_id()`, `sha256(bytea)` built-in).

## 3. Phạm vi đề xuất (Proposed)

**Trong 12.3a:**

- Offline SQL artifacts có manifest SHA-256 (cùng kiểu `wfl-v6-authorization.sql`): schema `iam_workflow` tối thiểu (fence, service-login mapping, permit), các owner functions IAM/WFL/AUD/floor cho **một** transition class, role/ACL manifest.
- Owner functions: `SECURITY DEFINER`, owner là role non-login non-superuser theo từng owner, `SET search_path = pg_catalog, pg_temp`, mọi object qualified, không dynamic SQL, `REVOKE ALL … FROM PUBLIC`, `GRANT EXECUTE` đúng signature.
- Binding root: permit consume ghi `pg_current_xact_id()`; WFL/AUD function chỉ chấp nhận permit đã consume **trong cùng xact id còn sống** và đúng operation/hash; repeat cùng operation trả receipt gốc.
- Service identity từ `session_user` → bảng mapping IAM; không tin `SET`/GUC/tham số tenant.
- Test PostgreSQL thật (gated, ADR-0025 disposable): positive root; DB01 negative (raw DML, foreign schema, DDL, AUD update/delete, hostile `search_path`, args giả); AU02 subset (permit sai service/instance/hash/version/tenant, expired, consumed, root khác); fence race subset; AUD hash parity với `AuditIntentCanonicalizer` (golden vectors).
- Owner docs, traceability, change history; ghi rõ "PoC evidence", không phải activation.

**Ngoài 12.3a (giữ ở slice sau):** delegation issuance/epoch đầy đủ và public owner ports (12.3b); writer matrix AU03/AU04 (12.3c); mTLS/RPC/credentials (12.3d); `Kordra.ControlWorker` và DI registration (12.3e); two-process/upgrade/HO (12.3f); production migration hay tạo role trên database ứng dụng thật.

## 4. Tiêu chí "fail → dừng"

PoC được coi là **fail** (dừng adoption, quay lại Architect/chủ dự án) nếu một trong các điều sau xảy ra mà không sửa được trong phạm vi function/ACL đã duyệt:

- Root transaction không nguyên tử được giữa các function owner khác nhau (một phần commit, một phần không).
- Least-privilege login vẫn đọc/ghi được bảng owner hoặc chiếm quyền qua `search_path`/argument.
- AUD qua function không tái tạo đúng canonical hash/idempotency hiện có.
- Npgsql/EF không gọi được function trên caller root mà không phá contract `WorkflowRootTransaction`/`AuditTransactionContext` hiện có.

## 5. Quyết định cần chủ dự án chốt

| # | Câu hỏi | Phương án | Đề xuất |
|---|---|---|---|
| D1 | Độ rộng PoC | (a) một lát dọc tối thiểu: fence + permit + **một** WFL transition (checkpoint step) + AUD + floor; (b) toàn bộ function surface của Worker Authority v1 | **(a)** — đủ trả lời câu hỏi compatibility; surface còn lại thêm dần ở 12.3b/e trên nền đã chứng minh |
| D2 | AUD canonical hash trong function | (a) C# vẫn canonicalize; function nhận canonical bytes + hash, kiểm `sha256(bytes) = hash` và tự trích cột từ JSON canonical; (b) viết lại canonicalizer bằng SQL | **(a)** — một nguồn canonical duy nhất, SQL chỉ kiểm và ràng buộc; tránh hai cài đặt lệch nhau |
| D3 | Function owner roles | (a) một role non-login riêng cho mỗi owner (IAM/WFL/AUD); (b) một role definer chung | **(a)** — đúng ranh giới owner; WFL gọi IAM chỉ qua `EXECUTE` function công khai của IAM |
| D4 | Role trong fixture PostgreSQL | Role là đối tượng **toàn cluster**, không nằm trong database tạm. (a) tạo role mang hậu tố run-id trên cluster local test, xoá khi cleanup, mỗi batch cần approval riêng; (b) chỉ kiểm offline (không login thật) | **(a)** — ADR-0022 yêu cầu login quyền tối thiểu thật; (b) không đạt gate. Cần CREATEROLE từ maintenance role của cluster local, **chỉ** cluster test trên Mac |
| D5 | MSG publication-admission ledger (CAP01) | (a) để ngoài PR 12.3a, làm cùng 12.3e/f; (b) gộp vào 12.3a | **(a)** — 12.3a tập trung câu hỏi function/ACL; ledger là thay đổi additive riêng của MSG owner |
| D6 | Cách duyệt thiết kế chi tiết | (a) Claude soạn mục thiết kế chi tiết (signatures, bảng, grants, test) để chủ dự án duyệt đúng nội dung, như 12.2f; (b) giao Architect task riêng | **(a)** — giống tiền lệ 12.2f (D7) |

## 5a. Quyết định của chủ dự án — 2026-11-13

- **D1:** lát dọc tối thiểu, **nhưng thiết kế phải bao phủ đầy đủ toàn bộ surface**. Diễn giải áp dụng: mục 8.2 liệt kê toàn bộ function surface của Worker Authority v1 với signature, owner, grant và slice; 12.3a hiện thực và kiểm thử lát dọc; mọi function còn lại được thêm ở slice ghi trong bảng theo **cùng** pattern (mục 8.3) và cùng bộ negative test DB01, không thiết kế lại.
- **D2:** C# tính canonical hash, SQL kiểm (`sha256(bytes) = hash`, trích cột từ JSON canonical).
- **D3:** một role non-login riêng cho mỗi owner IAM/WFL/AUD.
- **D4:** role mang hậu tố run-id trên cluster test local, xoá và kiểm khi cleanup; mỗi batch cần duyệt riêng.
- **D5:** ledger CAP01 của MSG làm ở 12.3e/f, ngoài PR 12.3a.
- **D6:** Claude soạn thiết kế chi tiết (mục 8) để chủ dự án duyệt đúng nội dung.

## 6. Rủi ro đã biết

- `SECURITY DEFINER` là attack surface mới; mọi grant/signature là reviewed artifact và phải có negative test trực tiếp bằng SQL.
- Role toàn cluster: cleanup phải kiểm role không còn và không có object/ACL treo; lỗi cleanup là blocker.
- Global fence giảm throughput; 12.3a chỉ đo đúng/sai, không đo tải (CAP01/12.3f).
- Không suy từ PoC ra quyền migrate/tạo role trên database ứng dụng thật hay LAB-01.

## 7. Bước tiếp theo

Chủ dự án duyệt đúng nội dung mục 8 → triển khai trên nhánh `codex/t12-3a-owner-function-poc` → test offline → xin duyệt batch DB (bao gồm tạo/xoá role theo D4) → gate → PR.

## 8. Thiết kế chi tiết 12.3a — bản nháp chờ duyệt

### 8.1 Database và schema trong fixture

PoC chạy trong một database tạm `kordra_test_<runId>` (ADR-0025), nên dùng **đúng tên schema production** (`app`, `wfl`, `iam_workflow`) thay vì schema theo case: artifact SQL được kiểm chính là artifact sẽ dùng sau này, không viết lại tên. Baseline trong database tạm: script WFL revision 6, script AUD hiện có (`app.aud_intents`), bảng floor IAM `app.iam_authority_clock_floors`, rồi artifact mới của 12.3a.

### 8.2 Toàn bộ function surface (D1)

Tất cả function: `SECURITY DEFINER`, `SET search_path = pg_catalog, pg_temp`, mọi object qualified, không dynamic SQL, `REVOKE ALL ON FUNCTION … FROM PUBLIC`, chỉ `GRANT EXECUTE` cho role ghi trong bảng. "Worker" = login `kordra_worker_runtime`; "API" = login `kordra_api_runtime` (profile API cho owner functions; các owner handler hiện có giữ nguyên).

| Owner | Function | Gọi bởi | Slice |
|---|---|---|---|
| IAM | `iam_workflow.current_service() → (service_id, generation, installation_id)` từ `session_user` | Worker, owner WFL/AUD | **12.3a** |
| IAM | `iam_workflow.acquire_fence(installation_id) → epoch` (`FOR UPDATE`, lock timeout ≤1s) | Worker, API | **12.3a** |
| IAM | `iam_workflow.advance_epoch(installation_id, reason_code) → epoch` (checked overflow) | API (invalidating writers) | **12.3a** (primitive); gắn vào từng writer ở 12.3c |
| IAM | `iam_workflow.consume_permit(permit_id, operation_id, instance_id, expected_version, specification_hash, boot_id, custody_epoch) → receipt` | Worker | **12.3a** |
| IAM | `iam_workflow.require_consumed(permit_id, operation_id, specification_hash) → binding` (cùng `pg_current_xact_id()`) | owner WFL, owner AUD | **12.3a** |
| IAM | `iam_workflow.advance_floor(installation_id, admitted_utc)` (đơn điệu, `GREATEST`) | Worker, API | **12.3a** |
| IAM | `iam_workflow.issue_permit(…)` | API | 12.3b |
| IAM | `iam_workflow.create_delegation(…)`, `reauthorize_delegation(…)`, `revoke_delegation(…)` | API | 12.3b |
| IAM | `iam_workflow.bootstrap_service(…)`, `rotate_service(…)`, `revoke_service(…)` (offline manifest) | offline owner CLI | 12.3b/d |
| WFL | `wfl.stage_transition(permit_id, installation_id, tenant_id, instance_id, step_id, operation_id, specification_hash, expected_version, next_state) → (resulting_version, duplicate)` | Worker | **12.3a** (chỉ kind `admit`: `Pending → Running`) |
| WFL | `wfl.stage_step(…)`, `wfl.stage_timer(…)`, `wfl.claim_custody(…)`, `wfl.heartbeat_custody(…)`, `wfl.claim_successor(…)` | Worker | 12.3e |
| WFL | `wfl.read_due_timers(…)`, `wfl.read_scoped_receipt(…)` (metadata-only, không mint authority) | Worker | 12.3e |
| AUD | `app.aud_append_bound_intent(permit_id, canonical_intent bytea, canonical_hash bytea) → (record_id, duplicate)` | Worker | **12.3a** |
| MSG | allowlist DML native (profile hiện có) + `msg.reserve_publication(…)`, `msg.settle_publication(…)` | Worker, API | 12.3e/f (D5) |

### 8.3 Pattern bắt buộc cho mọi function (12.3a và các slice sau)

- **Owner role:** `kordra_iam_owner`, `kordra_wfl_owner`, `kordra_aud_owner` — `NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOINHERIT`; sở hữu bảng và function của owner mình. Owner WFL/AUD chỉ có `EXECUTE` trên `iam_workflow.require_consumed`/`current_service`, không có quyền trên bảng IAM (D3).
- **Login role:** `kordra_worker_runtime`, `kordra_api_runtime` — `LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`; `USAGE` schema + `EXECUTE` đúng function; **không** quyền bảng nào trên `app`/`wfl`/`iam_workflow`; `REVOKE CREATE ON SCHEMA public`; `REVOKE ALL ON DATABASE … FROM PUBLIC` rồi `GRANT CONNECT` đích danh.
- **Identity:** function chỉ dùng `session_user` (không dùng `current_user`, GUC hay tham số tenant/actor). Tenant/installation/resource luôn đọc từ permit đã consume, tham số chỉ để **so khớp**, lệch là deny.
- **Lỗi:** `RAISE EXCEPTION` với SQLSTATE riêng của Kordra (lớp `KD…`) và reason code cố định; không đưa giá trị vào thông báo.
- **Artifact:** mỗi owner một file SQL + manifest (`revision`, `sha256`, `baselineSha256`, `destructiveOperations: false`), test offline so khớp byte. Tên role trong artifact là placeholder cố định; runner thay hậu tố run-id (D4) và test chứng minh phép thay chỉ chạm định danh role.

### 8.4 Bảng IAM tối thiểu (`iam_workflow`, 12.3a)

- `authority_fences(installation_id PK, epoch bigint ≥1, manifest_revision bigint)`.
- `service_logins(login_name PK, service_id, generation ≥1, installation_id, active bool)` — ánh xạ `session_user` → service.
- `permits(permit_id PK, service_id, generation, installation_id, tenant_id, instance_id, operation_id, expected_version, specification_hash char(64), transition_kind, authority_epoch, boot_id, custody_epoch, issued_at, expires_at, consumed_xact xid8 NULL, consumed_at NULL)`; check `expires_at - issued_at ≤ 2s`.
- 12.3a chưa có issuance: test seed permit bằng kết nối owner trong fixture (không cấp cho login nào). Bảng/cột đầy đủ của delegation và service generation thêm ở 12.3b theo expand-only.

### 8.5 Ngữ nghĩa một root transaction (worker)

Thứ tự bắt buộc, cùng một connection/transaction, `READ COMMITTED`, ngân sách ≤2s (timer monotonic phía C#, `lock_timeout` theo phần còn lại):

1. `acquire_fence(installation)` → khóa fence, đọc epoch.
2. `consume_permit(…)`: service từ `session_user` khớp permit; `authority_epoch` = epoch hiện tại; `clock_timestamp() < expires_at`; boot/custody/operation/version/hash khớp; nếu chưa consume → ghi `consumed_xact = pg_current_xact_id()`; nếu đã consume **trong cùng xact** → trả receipt cũ; xact khác → deny.
3. `wfl.stage_transition(…)`: gọi `require_consumed` (cùng xact, cùng operation/hash); khóa instance `FOR UPDATE`; cạnh trạng thái đọc từ bảng `wfl.state_edges` (sinh offline từ `WorkflowStateRules.CanChangeState`, có test parity); giữ mọi kiểm tra hiện có của `WorkflowStore.StageTransitionAsync` (cancellation intent, unsettled custody/Unknown/timer, hash, CAS); cùng operation đã ghi → trả kết quả gốc; ghi version + history.
4. `app.aud_append_bound_intent(…)`: gọi `require_consumed`; kiểm `sha256(canonical_intent) = canonical_hash`; parse JSON canonical bằng `jsonb`, trích cột như `AuditEntry`; tenant/resource/scope trong intent phải khớp permit; idempotent theo `intent_id` + hash (cùng hash → bản gốc, khác hash → conflict), giữ đúng các check constraint hiện có.
5. `advance_floor(installation, now)`.
6. `COMMIT` do caller (C#). Mất phản hồi COMMIT → Unknown, giữ custody; không retry business.

**Writer (API) song song:** `acquire_fence` → thay đổi → `advance_epoch` → commit. Nếu writer commit trước, bước 2 của worker thấy epoch khác → deny. Nếu worker giữ fence trước, writer chờ (≤1s) rồi chạy sau.

### 8.6 C# (inactive, không DI registration)

- Mỗi owner một adapter trong Infrastructure của chính owner đó, chỉ gọi function bằng `NpgsqlCommand` có tham số trên **caller root connection/transaction** hiện có: IAM `WorkflowAuthorityFunctionPort`, WFL `WorkflowTransitionFunctionPort`, AUD `AuditBoundAppendFunctionPort`. Không đổi `WorkflowRootTransaction`, `AuditTransactionContext` hay `AuditIntentCanonicalizer` (AUD adapter gọi canonicalizer hiện có để lấy bytes + hash).
- Không tạo `Kordra.ControlWorker`, route hay consumer. Architecture tests: adapter không được tham chiếu từ Host runtime composition.

### 8.7 Test bắt buộc

**Offline (CI):** artifact khớp byte + manifest; không có `DROP`/`TRUNCATE`/dynamic `EXECUTE`/`current_user`/`SET ROLE`; mọi function có `SECURITY DEFINER` + `search_path` cố định + `REVOKE … FROM PUBLIC`; ma trận grant đúng bảng 8.2; parity `wfl.state_edges` ↔ `WorkflowStateRules`; golden vectors AUD (C# canonical bytes/hash ↔ cột SQL trích ra); adapter guard không mở connection khi thiếu root.

**PostgreSQL thật (gated, ADR-0025, cần duyệt batch):**

- *Positive:* một root worker: fence → consume → `Pending → Running` → AUD → floor → commit; kiểm bằng kết nối độc lập; record AUD có hash trùng `AuditIntentCanonicalizer`.
- *Nguyên tử:* lỗi ở bước 4 hoặc 5 → không còn dấu vết bước 2–3; rollback giải phóng permit.
- *DB01:* login worker/API: `SELECT/INSERT/UPDATE/DELETE` trực tiếp bảng `app`/`wfl`/`iam_workflow` → deny; `CREATE`/`ALTER`/`DROP` → deny; update/delete `app.aud_intents` → deny; `search_path` thù địch (schema giả trước `pg_catalog`, function trùng tên) → không bị chiếm; `SET` GUC tenant/actor → không ảnh hưởng; gọi function owner khác ngoài grant → deny; kiểm thuộc tính role (`NOINHERIT`, không superuser/createrole/createdb/bypassrls).
- *AU02 subset:* permit sai service/generation, instance, hash, version, tenant, epoch; expired; consumed ở root khác; `require_consumed` từ root khác → deny, không đổi trạng thái.
- *Fence race subset:* writer commit epoch trước → worker deny; worker giữ fence → writer chờ rồi chạy sau, không vượt quá 1s lock timeout.
- *Idempotency:* lặp `stage_transition`/`aud_append_bound_intent` cùng operation trong cùng root → kết quả gốc; khác hash → conflict.
- *Cleanup:* database tạm đã xoá; mọi role hậu tố run-id đã xoá; không còn ACL/membership treo trên cluster.

### 8.8 Tài liệu cập nhật cùng PR

Feature IAM authorize-resource, AUD immutable-audit, WFL durable-workflow (source/test paths, trạng thái PoC); Worker Authority v1 chỉ thêm ghi chú implementation evidence, không đổi nội dung contract; conformance plan ghi DB01/AU02/AU03 subset là PoC evidence; traceability; change history. Nếu PoC fail theo mục 4: ghi kết quả thật, dừng, mở `[NEEDS-DECISION]`.

## Exact scoped acceptance — 2026-11-14

NukeGeng đã trả lời **"Duyệt thiết kế"** cho thiết kế chi tiết 12.3a (mục 8) được trình với SHA-256 trước annotation `d91b35cf0fb44410292c026fd159b3641539f6453197f9679e41bee1f520ac7a`. Bằng chứng riêng giữ tại `.amr-local/t12-integration/t12-3a-design-acceptance/`.

Accepted chỉ cho đúng thiết kế slice 12.3a: function surface và slice assignment ở 8.2, pattern 8.3, bảng IAM tối thiểu, ngữ nghĩa root transaction, adapter inactive và bộ test bắt buộc. **Không cấp quyền DB, tạo/xoá role, operation, activation hay merge**; mỗi batch PostgreSQL (kể cả role hậu tố run-id theo D4) cần manifest và approval riêng. Không thay đổi ADR-0022, Worker Authority v1 hay conformance plan.

## Implementation evidence — 2026-11-14

- Source: `iam-workflow-authority-v1.sql`, `wfl-owner-functions-v1.sql`, `aud-bound-append-v1.sql` (+ manifests) và ba adapter inactive; test `OwnerFunctionPocArtifactTests`, `OwnerFunctionPocDatabaseTests`.
- Batch DB lần 1 (`5dddd24`): fail trong helper test (rollback một root đã commit), không liên quan owner functions; dọn dẹp đã kiểm. Batch lần 2 (`8428054`): **pass**, database và năm role run-id đã xoá và kiểm độc lập. Kết luận mục 4: **không fail** → gate 12.3a đạt, tiếp tục 12.3b.
- Khác chữ so với mục 8 (đã báo chủ dự án): bảng hiện có giữ owner migration, role owner chỉ nhận quyền bảng tối thiểu (AUD owner chỉ SELECT/INSERT); `app.aud_append_bound_intent` nhận thêm `operation_id`/`specification_hash` vì `require_consumed` cần; chuyển owner function cấp tạm CREATE schema rồi thu hồi trong cùng artifact.

Full AMR gate (approved disposable batch, head `576b841`, base `79f3228`): `local_status: passed` — docs, scope, unit, clients, restore, build, product-test (2,467 passed / 0 failed / 28 expected operation-gated skips) and mapping; database cleanup verified. A first gate packet on the same head failed only its expected-skip comparison (it omitted the two operation-gated DB cases, each passed in its own approved batch); all checks and tests had passed.
