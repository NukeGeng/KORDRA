---
id: ENG-T12-WF01-REAUTH-SCOPE
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-12
---

# WF01 reauthorization — đề xuất phạm vi giữa T12.2 và T12.3

> Accepted scoped design ngày 2026-11-12; xem Exact scoped acceptance ở cuối file. Chưa có implementation.
>
> **Lịch sử:** Chủ dự án đã chốt D1–D7 ngày 2026-11-12 (mục 6). Mục 9 là bản nháp thiết kế slice 12.2f, **chờ duyệt đúng nội dung** trước khi viết code. Tài liệu không tự thay đổi ADR-0021/0022, [Durable Workflow v1](../contracts/durable-workflow-v1.md), [Worker Authority v1](../contracts/worker-authority-v1.md) hay [conformance plan](t12-conformance-plan.md); các chỗ cần sửa câu chữ được liệt kê ở mục 9.8. Không cấp quyền DB, operation hay activation.

## 1. Vấn đề

- [Conformance plan](t12-conformance-plan.md) đặt điều kiện thoát T12.2 là WF01–WF08 cộng HO03 áp dụng. Đoạn "Reauthorization cases AU05/WF01 bổ sung" gắn reauthorization vào cả WF01 và AU05.
- [Durable Workflow v1 §Reauthorization](../contracts/durable-workflow-v1.md) yêu cầu trong **một root**: tạo delegation generation mới, append authority-binding history và AUD, vô hiệu generation cũ và **tăng authority epoch**.
- Theo [bảng slice](t12-remaining-design.vi.md), 12.2e chỉ gồm start/read/cancel/reconcile. Delegation, permit và fence là **12.3b**; mọi invalidating writer join fence là **12.3c**; AU05 là điều kiện thoát T12.3.

Vì vậy WF01 phần reauthorization **không thể hoàn tất end-to-end trong T12.2** nếu giữ đúng thứ tự dependency đã duyệt (12.3a là gate ACL/function PoC trước 12.3b). Cần chủ dự án quyết cách tách, thay vì tự chọn baseline.

## 2. Hiện trạng có bằng chứng (nhánh PR #29, head `048f79c`)

| Hạng mục | Trạng thái | Bằng chứng |
|---|---|---|
| State `BlockedAuthority` và các cạnh chuyển | Có, chỉ là luật cấu trúc | `src/Modules/WorkflowOrchestration/Domain/WorkflowStateRules.cs`; test thuần `tests/WorkflowOrchestration.Tests/WorkflowFoundationTests.cs` |
| Bảng `authority_bindings` | Có model (khóa theo instance + generation, unique), **chưa có writer/reader** | `WorkflowRecords.cs` (`WorkflowAuthorityBindingRecord`), `WorkflowDbContext.cs` |
| IAM delegation, permit, authority fence | **Chưa có** trong IAM | Root start ghi rõ "Does not create IAM delegation" (`WorkflowRootTransaction.cs`) |
| Kiểm `workflow.start` + `release.read` cùng subject | Có, dùng cho start; tái dùng được cho eligibility của reauth | `WorkflowStartEligibilityReader.cs` |
| Lệnh/route `POST /api/v1/workflows/{id}/reauthorizations` | **Chưa có**, chưa đăng ký | — |
| Test reauthorization (AU05/WF01) | **Chưa có** | — |

**Khoảng trống cần lưu ý (chưa bị khai thác vì chưa có consumer):** `WorkflowStore.StageTransitionAsync` chấp nhận mọi cạnh hợp lệ về cấu trúc, kể cả `BlockedAuthority → Running`, mà không đòi authority mới. Khi có consumer, đây là đường vượt quyền. Đề xuất xử lý ở mục 5.

**Điểm cần làm rõ:** contract liệt kê `delegationId` trong instance schemaVersion1 (ngoài specificationHash), còn implementation không lưu nó trong instance hay spec canonical mà chỉ có ở `authority_bindings`. Chưa chắc đây là drift hay chỉ là cách lưu; cần chốt (D6).

## 3. Phân chia theo owner

| Yêu cầu trong contract/plan | Owner | Slice sớm nhất khả thi |
|---|---|---|
| Chỉ chấp nhận ở `BlockedAuthority`; không có transaction/step/timer chưa rõ | WFL | T12.2 |
| Spec, hash, deadline gốc, scheduler/retry counter không đổi; không resurrect terminal; không đổi tenant | WFL | T12.2 |
| Append-only `authority_bindings` với generation tăng đơn điệu; actor trùng actor gốc | WFL (tham chiếu, không copy grant IAM) | T12.2 |
| Idempotency theo `requestId`, If-Match (expectedVersion), changed hash → conflict | WFL | T12.2 (cần quyết lưu receipt ở đâu, D4) |
| Kiểm quyền hiện hành `workflow.start` + `release.read` trên đúng environment, subject là actor gốc | IAM facts qua Host | T12.2 (tái dùng reader hiện có) |
| Tạo delegation generation mới, vô hiệu generation cũ, tăng authority epoch | IAM | **12.3b/c** |
| Approval khác chỉ được nhận nếu owner chứng minh khớp release revision/hash gốc | APR/REL qua Host | T12.2 (đọc) — nhưng chỉ có ý nghĩa khi có delegation |
| Ai/khi nào đưa workflow vào `BlockedAuthority` (permit bị từ chối, delegation hết hạn) | Worker + IAM | **12.3e** |
| Route HTTP công khai và activation | Host | Sau gate T12.3 |

## 4. Phương án

**A. Dời toàn bộ reauthorization sang T12.3.** T12.2 thoát với WF01 = phần start (đã có bằng chứng). Reauthorization làm cùng AU05 sau 12.3b/c.
- Ưu: đúng dependency, không có code nửa vời.
- Nhược: lỗ hổng `BlockedAuthority → Running` ở store chung vẫn mở tới T12.3; luật WFL của reauth chưa được kiểm chứng sớm.

**B. Tách: phần WFL làm ở T12.2 (slice mới, tạm gọi 12.2f), phần IAM hoàn tất ở T12.3.** *(Khuyến nghị.)*
- Ưu: chặn sớm lỗ hổng chuyển state; luật WFL (state, unresolved work, bất biến spec/deadline/counter, generation đơn điệu, idempotency) có test thuần và DB fixture từ trước khi có delegation. 12.3b chỉ cần cắm IAM vào một port đã có test.
- Nhược: thêm một slice, cần định nghĩa port tới IAM trước khi IAM implement. WF01 vẫn không "đầy đủ" ở T12.2; điều kiện thoát phải ghi rõ phần nào dời sang T12.3.

**C. Kéo delegation IAM (12.3b) lên T12.2.** *(Không khuyến nghị.)* Bỏ qua gate 12.3a (ACL/function PoC); epoch chỉ có nghĩa khi mọi invalidating writer đã join fence (12.3c). Làm sớm sẽ tạo authority giả hoặc phải viết lại.

## 5. Nội dung slice 12.2f

Phương án B đã được chọn. Nội dung chi tiết, và là nguồn duy nhất cho 12.2f, nằm ở **mục 9**: luật domain, schema revision 6, owner store, cách đóng lỗ hổng chuyển state, port Host tới IAM, transaction inactive, test và tài liệu.

## 6. Quyết định của chủ dự án — 2026-11-12

| # | Câu hỏi | Quyết định |
|---|---|---|
| D1 | Phương án | **B** — phần WFL làm ở slice 12.2f trong T12.2; phần IAM (delegation, epoch, fence) hoàn tất ở 12.3b/c |
| D2 | Điều kiện thoát T12.2 cho WF01 | Theo B: T12.2 cần start + phần WFL của reauthorization; phần delegation/epoch và end-to-end AU05/WF01 thuộc điều kiện thoát T12.3. Ghi bằng annotation trong conformance plan (mục 9.8) |
| D3 | Ngữ nghĩa sau reauth | **Chỉ ghi generation mới**; state giữ `BlockedAuthority`. Chỉ lần admission kế tiếp của worker (permit theo delegation mới, 12.3e) mới chuyển sang `Running` |
| D4 | Nơi lưu idempotency | **Thêm cột vào `authority_bindings`** (schema revision mới, additive) |
| D5 | Generation 1 khi start | Hệ quả của B: luồng start ghi generation 1 ở 12.3b, cùng root với IAM delegation. Trước đó 12.2f chỉ dùng dữ liệu fixture |
| D6 | Vị trí `delegationId` | **Giữ ở `authority_bindings`**; làm rõ câu chữ contract. Không thuộc instance record hay specificationHash |
| D7 | Cách duyệt thiết kế | **Chủ dự án duyệt trực tiếp** bản nháp mục 9, không mở task Architect riêng |

## 7. Rủi ro hiện tại

- Không thể tuyên bố T12.2 đạt WF01 đầy đủ; các tài liệu tiến độ phải ghi WF01 = phần start.
- Lỗ hổng `BlockedAuthority → Running` ở store chung tồn tại cho tới khi được đóng (B) hoặc tới T12.3 (A). Hiện chưa có consumer nên chưa khai thác được.
- Cho tới khi 12.2f merge, không được dựa vào `WorkflowStore.StageTransitionAsync` để rời `BlockedAuthority`.

## 8. Bước tiếp theo

1. Chủ dự án duyệt đúng nội dung mục 9 (theo SHA-256 của file tại thời điểm duyệt).
2. Sau khi PR #29 merge: tạo nhánh riêng từ `main` mới nhất, implement 12.2f theo mục 9, mỗi lỗi/luật có test hồi quy.
3. Ca PostgreSQL chuẩn bị manifest và xin approval riêng trước khi chạy.

## 9. Thiết kế slice 12.2f — bản nháp chờ duyệt

### 9.1 Phạm vi

Trong phạm vi: luật domain thuần, schema revision 6 cho `authority_bindings`, owner store stage trên root của caller, đóng lỗ hổng `BlockedAuthority → Running` ở store chung, port Host tới IAM (chỉ interface), transaction Host inactive cho fixture, test và tài liệu.

Ngoài phạm vi: IAM delegation persistence, authority epoch/fence, invalidating writers, ghi generation 1 khi start, đưa workflow vào `BlockedAuthority`, chuyển `BlockedAuthority → Running`, route HTTP, đăng ký DI production, mọi activation.

### 9.2 Domain (WFL Domain, BCL-only)

- `WorkflowReauthorizationCommand(InstallationId, TenantId, InstanceId, RequestId, ActorId, ExpectedVersion, SpecificationHash)`: mọi GUID khác rỗng; `ExpectedVersion` ≥ 1 và < `long.MaxValue`; hash 64 ký tự hex thường.
- `CommandHash`: SHA-256 trên canonical JSON đóng, thứ tự field cố định, theo đúng mẫu `WorkflowCancellationIntent`. Có golden vector độc lập.
- `WorkflowReauthorizationRules.RequireEligible(...)`, thuần, nhận snapshot owner đã đọc:
  - state hiện tại là `BlockedAuthority`;
  - không cancellation intent; không custody phase khác settled; không step `Unknown`; không timer đã publish chưa settle;
  - `CreatedAtUtc ≤ effectiveUtc < DeadlineUtc` (deadline gốc, không gia hạn);
  - actor của lệnh trùng actor của generation hiện tại;
  - generation mới = generation hiện tại + 1, kiểm overflow.
  Đây là điều kiện cấu trúc, không phải bằng chứng quyền.

### 9.3 Persistence — schema revision 6

- Hằng số mới `AuthorizationSchemaRevision = 6`; factory offline và fixture tương ứng, kể cả profile disposable (ADR-0025). Revision 1–5 giữ nguyên.
- `authority_bindings` thêm cột: `request_id`, `command_hash` (64 hex), `specification_hash`, `expected_version`, `resulting_version`.
- Ràng buộc: unique `(installation, tenant, instance, request_id)`; giữ unique `(installation, tenant, instance, generation)`; generation ≥ 1; `resulting_version = expected_version + 1`; hash đúng độ dài.
- SQL artifact sinh offline, không tự áp dụng. Vì bảng chưa từng có writer, migration **kiểm bảng rỗng rồi mới thêm cột NOT NULL**; nếu có dữ liệu thì dừng, không tự điền giá trị mặc định.
- Owner context: `authority_bindings` append-only (cấm update/delete), như các bảng history khác.

### 9.4 Owner store `WorkflowAuthorityBindingStore`

`StageReauthorizationAsync(command, WorkflowDelegationReference delegation, DateTimeOffset effectiveUtc)` trả `WorkflowAuthorityBindingResult(BindingId, Generation, ResultingVersion, Duplicate)`. Chỉ stage, không commit.

Trình tự:
1. Yêu cầu context revision ≥ 6, root của caller, change tracker sạch.
2. Khóa instance (`ReadLockedInstanceAsync`), kiểm spec hash và canonical binding; so timestamp bằng tham số PostgreSQL (bài học PR29-WFL-TIMESTAMP-002).
3. Nếu đã có dòng cùng `request_id`: trùng hoàn toàn (instance, actor, command hash, spec hash, expected/resulting version) → trả kết quả gốc, `Duplicate = true`; khác → conflict.
4. Đọc snapshot hiện hành (state, version, custody, step, timer, cancellation, generation hiện tại) và gọi `WorkflowReauthorizationRules.RequireEligible`.
5. CAS: `instance.Version` phải bằng `ExpectedVersion` (If-Match). Tăng version; **giữ nguyên state** (D3) — theo đúng tiền lệ cancellation intent: không ghi dòng `transitions`, bản ghi append-only tự giữ expected/resulting version.
6. Append dòng `authority_bindings` với generation mới, `DelegationId` lấy từ port, `ActorId` của lệnh.

### 9.5 Đóng lỗ hổng `BlockedAuthority → Running`

`WorkflowStore.StageTransitionAsync` từ chối khi state hiện tại là `BlockedAuthority` và state đích là `Running`. Các cạnh `BlockedAuthority → CancelRequested/Expired` giữ nguyên (chúng chỉ thu hẹp quyền). Đường hợp lệ duy nhất để rời sang `Running` sẽ là worker admission có permit theo generation hiện hành (12.3e). Có test hồi quy riêng.

### 9.6 Host (inactive)

- Port `IWorkflowDelegationReissuer` trong `src/Host/Kordra.Api/Composition/Workflows/`:
  `StageReissueAsync(DbConnection, DbTransaction, WorkflowDelegationReissueRequest, CancellationToken) → WorkflowDelegationReference?` với `WorkflowDelegationReference(DelegationId, ValidUntilUtc)`.
  Hợp đồng cho người implement ở 12.3b: stage trên **cùng root**, giữ fence, xác minh delegation cũ thuộc đúng instance và actor, tạo delegation mới, vô hiệu delegation cũ, tăng epoch kèm AUD.
  12.2f **không có implementation production**. Bản tổng hợp chỉ nằm trong test; architecture test bảo đảm không có type production nào implement port và không có đăng ký DI.
- `WorkflowReauthorizationTransaction` (chỉ factory isolated/disposable test, theo mẫu `WorkflowCancellationTransaction`):
  1. Admission dưới read gate: dựng selection từ spec bất biến, tra `ReleaseRevisionId` qua owner REL theo (releaseId, revision) và bắt buộc hash khớp spec, rồi gọi lại đúng `WorkflowStartEligibilityReader` (`workflow.start` + `release.read` + approval cho đúng release revision/hash, cùng subject). Subject phải là actor của lệnh.
  2. Mở root, stage port → store → AUD intent (Mutation, permission `workflow.start`, reason `workflow-reauthorize-<commandHash>`) → clock floor.
  3. Kiểm lại admission trước commit; commit một lần. Unknown commit đóng gate, không tự thử lại, giữ nguyên quy ước của các transaction hiện có.

### 9.7 Test bắt buộc

- **Thuần:** golden vector `CommandHash`; điều kiện của `RequireEligible` cho từng state (Pending, Running, WaitingTimer, Reconciling, CancelRequested, terminal), custody/step/timer chưa rõ, cancellation intent, biên deadline (bằng và sau), sai actor, overflow generation.
- **Model/offline:** revision 6 chỉ thêm cột/ràng buộc như mục 9.3, revision 1–5 không đổi; append-only guard; SQL artifact khớp model.
- **Store (DB fixture, cần approval riêng):** duplicate cùng hash → kết quả gốc; khác hash → conflict; If-Match cũ → conflict; sai tenant/instance; spec, deadline, timer counter giữ nguyên; generation unique và đơn điệu; rollback không để lại dòng.
- **Lỗ hổng 9.5:** `BlockedAuthority → Running` qua store chung bị từ chối; `→ CancelRequested/Expired` vẫn được.
- **Host (DB fixture):** thiếu `workflow.start` hoặc `release.read`; sai subject; approval không khớp release hash; AUD + floor + binding atomic; quyền bị thu hồi giữa staging và commit → không commit.
- **Kiến trúc:** WFL Domain/Contracts vẫn BCL-only; không type production nào implement `IWorkflowDelegationReissuer`; không đăng ký DI hay route.

### 9.8 Tài liệu cập nhật cùng PR

- FEAT-WFL-001: mục 12.2f, `source_paths`/`test_paths`.
- [Durable Workflow v1](../contracts/durable-workflow-v1.md): làm rõ (không đổi shape) — `delegationId` là authority history trong `authority_bindings` (D6); reauth chỉ ghi generation, `Running` chỉ sau admission (D3); If-Match là workflow version; idempotency theo `requestId` + command hash.
- [Conformance plan](t12-conformance-plan.md): annotation D1/D2 — phần nào của WF01 thuộc T12.2, phần nào dời sang T12.3 cùng AU05.
- Traceability, change history. Không cần ADR mới: thiết kế nằm trong ADR-0021/0022 đã duyệt; nếu khi implement phát hiện cần đổi boundary hay state machine thì dừng lại và xin quyết định.

### 9.9 Phần còn lại ở T12.3

12.3b: IAM delegation persistence, ghi generation 1 khi start, implementation production của `IWorkflowDelegationReissuer`. 12.3c: epoch/fence cho mọi invalidating writer. 12.3e: worker đưa workflow vào `BlockedAuthority` khi permit bị từ chối, và chuyển sang `Running` theo generation hiện hành. 12.3f: route HTTP và các ca AU05/WF01 end-to-end.

## Exact scoped acceptance — 2026-11-12

NukeGeng đã trả lời **"Duyệt thiết kế"** cho đúng nội dung tài liệu này tại SHA-256 `91026ffc86a0a961d3febd665e45d4ec5012a0fc31aa05d0570bb5798aed7c63` (trước khi thêm chú thích). Bản trước chú thích và bằng chứng duyệt được giữ riêng tại `.amr-local/t12-integration/wf01-design-acceptance/`.

Chấp thuận phương án B, các quyết định D1–D7 (mục 6) và thiết kế slice 12.2f (mục 9). Developer được implement 12.2f trên nhánh riêng sau khi PR #29 merge. **Không cấp quyền thao tác DB, operation, activation hay merge.** Ca PostgreSQL vẫn cần manifest và approval riêng. Nếu khi implement cần đổi boundary, state machine hay contract ngoài mục 9.8 thì dừng lại và xin quyết định.
