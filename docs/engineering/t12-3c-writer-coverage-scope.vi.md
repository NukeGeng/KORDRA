---
id: ENG-T12-3C-WRITER-COVERAGE-SCOPE
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-16
---

# T12.3c — mọi invalidating writer tham gia global fence: phạm vi và thiết kế chi tiết

> Accepted scoped design ngày 2026-11-16 (D1–D7 như đề xuất); xem Exact scoped acceptance ở cuối file. **12.3c-1, 12.3c-2 và 12.3c-3 implemented (inactive mặc định)**; activation vẫn bị chặn bởi bước runbook D6. Không thay đổi [ADR-0022](../decisions/0022-controlworker-authority.md), [Worker Authority v1](../contracts/worker-authority-v1.md) hay [authority/admission v1](../contracts/authority-admission-v1.md). Không cấp quyền DB, role, operation, activation hay merge.

## 1. Mục tiêu

Worker Authority v1 (mục Linearization) yêu cầu: mọi invalidating writer, kể cả offline CLI, lấy API writer gate khi online rồi **lock fence `FOR UPDATE` trên root connection trước khi đọc authorization fact**, thay đổi owner state, **tăng epoch** và ghi AUD/floor trong cùng root. Thiếu một writer hoặc thiếu race test cho nó là **blocker activation**; registry/grep tĩnh không đủ (AU04). 12.3c làm đúng việc đó trên nền 12.3b (fence/epoch đã có trong `iam-workflow-authority-v2.sql`).

## 2. Hiện trạng có bằng chứng (main `8eb4db0`)

`AuthorityWriterRegistry` (Host) liệt kê 20 writer Foundation, chưa writer nào chạm fence:

| Nhóm | Writer (id → entry point) | Online/offline |
|---|---|---|
| IAM | `iam.boot` InitializeBootAsync, `iam.login` LoginAsync, `iam.logout` LogoutAsync, `iam.logout-all` LogoutAllAsync, `iam.password` ChangePasswordAsync (LocalAuthenticationService) | boot offline, còn lại online |
| IAM | `iam.enroll` EnrollOfflineAsync, `iam.recover` RecoverOfflineAsync, `iam.disable` DisableOfflineAsync (LocalAuthenticationService); `iam.definition` RoleDefinitionStore.PublishOfflineAsync | offline |
| TEN | `ten.initialize`, `ten.member`, `ten.grant` (offline); `ten.mutate` ExecuteAsync (online) — TenantMembershipService | hỗn hợp |
| Catalog (PRJ/INV) | `catalog.mutate`, `catalog.environment-v2` — CatalogMutationCoordinator | online |
| Offline admin | `sec.binding` OfflineCredentialCoordinator, `policy.publish-bind` OfflineResourcePolicyCoordinator, `approval.policy` OfflineApprovalPolicyCoordinator | offline |
| REL/APR | `release.mutate` ReleaseMutationTransaction, `approval.mutate` ApprovalMutationTransaction | online |

Các writer mới của 12.3b đã tăng epoch bên trong function IAM nhưng chưa có trong registry: tái cấp quyền workflow (`reauthorize_delegation`), thu hồi delegation (`revoke_delegation`), service manifest (`apply_service_manifest`). **Khôi phục installation** (contract có liệt kê) chưa có công cụ nào trong repo.

## 3. Phạm vi

**Trong 12.3c:** port fence của IAM; gắn vào đủ 20 writer + đăng ký 3 writer của 12.3b; registry ghi trạng thái phủ fence; race test AU03/AU04 theo **entry point thật** cho từng writer; guard để không activation khi chưa phủ đủ; tài liệu owner.

**Ngoài 12.3c:** mTLS/RPC (12.3d); worker host và readiness thật (12.3e); migration/role production, two-process, HTTP end-to-end (12.3f); công cụ khôi phục installation (D6).

## 4. Quyết định đề xuất (chủ dự án chốt)

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D1 | Cơ chế tham gia fence | Port IAM Contracts `IAuthorityFence` (acquire trên root của writer → lease; `lease.AdvanceAsync(reasonCode)` trước commit), cài đặt `PostgresAuthorityFence` gọi `iam_workflow.acquire_fence/advance_epoch`. **Loại** trigger DB: khoá sau khi ghi (sai thứ tự "fence trước fact") và gắn chéo owner ở tầng DB. |
| D2 | Khi nào tăng epoch | Đúng **một** lần cho mỗi lần commit có thay đổi state liên quan quyền; **không** tăng cho nhánh chỉ ghi audit từ chối hoặc không đổi gì (ví dụ logout session đã hết hiệu lực). |
| D3 | Chế độ trước activation | Composition hiện tại dùng `AuthorityFence.Inactive` **tường minh** (không chạm DB, giữ nguyên hành vi và test hiện có). Chế độ `Enforced` chỉ bật trong composition có authority (test 12.3c, activation 12.3e/f). Activation phải từ chối nếu fence không ở `Enforced` hoặc registry còn writer chưa có race test pass. |
| D4 | Bằng chứng cho từng writer | Race test PostgreSQL gọi **entry point thật** với fence `Enforced`: (a) fence đang bị giữ thì writer chờ có giới hạn rồi chạy; (b) commit của writer tăng epoch đúng 1 và permit cấp trước đó bị từ chối (`KD005`); (c) nhánh không đổi gì thì epoch giữ nguyên. Registry ghi id test tương ứng; test completeness đối chiếu registry ↔ danh sách test. |
| D5 | Chia PR | Một lần duyệt thiết kế, ba PR: **12.3c-1** port + 9 writer IAM; **12.3c-2** TEN (4) + catalog (2) + `policy.publish-bind`; **12.3c-3** `sec.binding`, `approval.policy`, `release.mutate`, `approval.mutate`, 3 writer 12.3b vào registry, completeness + guard activation. Mỗi PR có batch DB, gate, review riêng. |
| D6 | Khôi phục installation | Không có công cụ trong repo → ghi vào registry dạng **operation offline chưa có entry point**, kèm `[NEEDS-DECISION]` trong runbook: quy trình restore phải dừng API/worker và tăng epoch (offline manifest) trước khi khởi động lại. Activation bị chặn cho tới khi runbook có bước này (12.3f/12.4). |
| D7 | Danh tính DB của writer offline | Writer offline chạy bằng login admin offline được bootstrap thành **một service `kind = 'api'` riêng** (service id/generation riêng) qua `apply_service_manifest`; không đổi schema IAM v2. |

## 5. Thiết kế chi tiết — bản nháp chờ duyệt

### 5.1 Port và cài đặt (IAM)

- `IAuthorityFence.AcquireAsync(DbConnection, DbTransaction, CancellationToken) → IAuthorityFenceLease` — gọi ngay sau khi writer mở root (sau API writer gate nếu online), **trước** mọi đọc fact; lease giữ epoch đã đọc.
- `IAuthorityFenceLease.AdvanceAsync(string reasonCode, CancellationToken)` — gọi tối đa một lần, chỉ trên nhánh có thay đổi, trước commit; idempotent trong cùng lease.
- `AuthorityFence.Inactive` (no-op, `Mode = Inactive`) và `PostgresAuthorityFence(installationId)` (`Mode = Enforced`, dùng `WorkflowAuthorityFunctionPort` trên đúng root của writer). Lỗi fence (`KD001/KD002/55P03`) → writer trả kết quả "authority unavailable" như lỗi owner hiện có, không commit.
- Thứ tự bắt buộc: gate → fence → owner rows; không gọi mạng khi giữ fence.

### 5.2 Gắn vào writer

Mỗi entry point trong registry nhận `IAuthorityFence` qua constructor/composition hiện có (không thêm DI runtime mới ngoài `Inactive` mặc định tường minh). Điểm chèn chung: sau `BeginTransactionAsync` của root, trước truy vấn owner đầu tiên; `AdvanceAsync` ngay trước bước commit cùng AUD. Reason code cố định theo id registry (ví dụ `iam.logout`).

### 5.3 Registry

`AuthorityWriter` thêm `FenceCoverage` (`Covered` + id race test, hoặc `OperationOnly` cho D6) và 3 entry 12.3b. `AuthorityWriterRegistry.Validate()` vẫn kiểm method tồn tại; thêm `RequireEnforcedCoverage()` dùng ở guard activation (12.3e) — fail nếu còn entry chưa `Covered`.

### 5.4 Test

- **Offline:** registry ↔ race test completeness; `Inactive` không mở connection; mỗi writer dùng fence trước truy vấn đầu tiên (kiểm qua fence giả ghi lại thứ tự lệnh — bổ trợ, không thay race test).
- **PostgreSQL (gated, cần duyệt batch):** với mỗi writer của PR: (a) chờ khi fence bị giữ; (b) epoch +1 và permit cũ `KD005`; (c) nhánh không đổi gì giữ epoch. Fixture database tạm + role run-id như 12.3a/12.3b; writer chạy bằng login có `kind = 'api'`.

### 5.5 Tài liệu cùng mỗi PR

Feature spec của owner bị chạm, authority/admission v1 và Worker Authority v1 chỉ thêm ghi chú implementation evidence, conformance plan (AU03/AU04), traceability, change history; runbook `[NEEDS-DECISION]` cho D6 ở PR 12.3c-3.

## 6. Rủi ro

- Fence toàn installation làm writer API chờ khi worker giữ fence (≤ 1 s lock timeout) → có thể trả "authority unavailable" dưới tải; đúng trade-off đã chấp nhận ở ADR-0022.
- 20+ entry point: sai sót ở một writer là lỗ hổng authority; vì vậy race test theo từng writer là bắt buộc, không chỉ grep.
- Chế độ `Inactive` là mặc định trước activation; guard activation là thứ ngăn bật worker khi chưa `Enforced`.

## 7. Bước tiếp theo

Chủ dự án duyệt đúng nội dung (gồm D1–D7) → 12.3c-1 trên `codex/t12-3c-writer-coverage` → batch DB + gate → PR → 12.3c-2 → 12.3c-3.

## Exact scoped acceptance — 2026-11-16

NukeGeng đã trả lời **"Duyệt thiết kế"** cho đúng nội dung tài liệu này (gồm D1–D7 như đề xuất), trình với SHA-256 trước annotation `c08a06d79ad6b35e48a48e6e80072c59a498deb83fb65cb4d1056b2929e42bb3`. Bằng chứng riêng tại `.amr-local/t12-integration/t12-3c-design-acceptance/`.

Accepted chỉ cho thiết kế slice 12.3c (ba PR 12.3c-1..3). Không cấp quyền DB, role, operation, activation hay merge; mỗi batch PostgreSQL cần manifest và approval riêng. Quyền push + mở PR khi gate local qua vẫn áp dụng như các slice 12.3 trước.

## Implementation evidence — 12.3c-1 (2026-11-17)

- Source: `AuthorityFence` (IAM Contracts, `Inactive` tường minh), `PostgresAuthorityFence`; `LocalAuthenticationService` (8 writer) và `RoleDefinitionStore.PublishOfflineAsync` khoá fence ngay sau khi mở root, trước khi đọc fact; tăng epoch theo D2: boot, logout, logout-all, đổi mật khẩu, recover, disable, revision mới/retire của definition đã có luôn tăng; login thành công, enroll và definition hoàn toàn mới không tăng; login sai chỉ tăng ở lần bắt đầu lockout; audit từ chối thuần không chạm fence. `AuthorityWriterRegistry` thêm `FenceRaceScenario` cho 9 writer IAM — đây là `FenceCoverage` của §5.3 ở dạng `Covered` + id race test; dạng `OperationOnly` (D6) thêm ở 12.3c-3 — và `RequireEnforcedCoverage()` (đang từ chối vì 11 writer còn lại thuộc 12.3c-2/3).
- Test offline: `AuthorityFenceTests` pass. PostgreSQL: `AuthorityFenceIamWriterDatabaseTests` **pass** ngay lần chạy đầu trên batch được duyệt (`e666b37`; database và role run-id đã xoá và kiểm độc lập).
- Gate AMR đầy đủ trên `e666b37` (base `8eb4db0`): `local_status: passed`, 2.489 pass / 0 fail / 31 skip có gate như dự kiến (30 của 12.3b + race test mới).
- Sửa sau review vòng 1 của PR #35 (Codex P2, `PR35-IAM-FENCE-TIME-001`): 8 writer IAM đọc thời gian tin cậy **trước** khi chờ fence, nên session hết hạn trong lúc chờ vẫn qua được kiểm tra của logout, logout-all và đổi mật khẩu. Nay cả 9 writer đọc thời gian ngay **sau** khi khoá fence; `RoleDefinitionStore` vốn đã vậy. Không đổi thiết kế D1–D7. Hồi quy (d) trong `AuthorityFenceIamWriterDatabaseTests`: session hết hạn trong lúc writer đang chờ fence bị từ chối, không commit, epoch giữ nguyên. Race test (gồm ca (d)) **pass** trên batch được duyệt (`d8d9b91`; database và role run-id đã xoá và kiểm); gate AMR đầy đủ trên `d8d9b91` (base `8eb4db0`): `local_status: passed`, 2.489 pass / 0 fail / 31 skip có gate như dự kiến.

## Implementation evidence — 12.3c-2 (2026-11-17)

- Source: `TenantMembershipService` (4 writer), `CatalogMutationCoordinator` (`ExecuteAsync` cho catalog/target, `ExecuteEnvironmentV2Async`) và `OfflineResourcePolicyCoordinator.ExecuteAsync` nhận `IAuthorityFence` (mặc định `Inactive`) và khoá fence ngay sau khi mở root. Các lần đọc subject/grant/tenant từng nằm trước root (kiểm subject của TEN online, xác định principal của catalog, kiểm tenant của policy) nay chạy sau fence. Theo D2, mọi commit thành công của 7 writer đều đổi dữ liệu quyền mà permit đánh giá lại (membership, grant, trạng thái tenant, sở hữu/gắn catalog, binding và head của policy), nên tăng epoch đúng một lần trong commit có audit; replay, từ chối, xung đột và input sai không commit nên không tăng. Lỗi fence → `unavailable`, không commit. Replay receipt của policy không đọc fact quyền, không commit nên không lấy fence. Registry ghi race scenario cho 7 writer; `RequireEnforcedCoverage()` vẫn từ chối vì còn 4 writer của 12.3c-3.
- Test offline: `AuthorityFenceTests` pass (thêm mặc định `Inactive` cho 3 owner và completeness IAM + TEN/catalog/policy). PostgreSQL: `AuthorityFenceTenantCatalogWriterDatabaseTests` trên harness chung `AuthorityFenceRaceHarness` **pass** trên batch được duyệt (`af8b1b0`; database và role run-id đã xoá và kiểm độc lập).
- Gate AMR đầy đủ trên `af8b1b0` (base chồng `94ab25d`): `local_status: passed`, 2.491 pass / 0 fail / 32 skip có gate như dự kiến (31 trước đó + race test mới).

## Implementation evidence — 12.3c-3 (2026-11-18)

- Source: `OfflineCredentialCoordinator` (`sec.binding`), `OfflineApprovalPolicyCoordinator` (`approval.policy`), `ReleaseMutationTransaction` (`release.mutate`) và `ApprovalMutationTransaction` (`approval.mutate`) nhận `IAuthorityFence` tuỳ chọn (mặc định `Inactive`) và khoá fence ngay sau khi root IAM mở. Mọi lần đọc thời gian tin cậy dùng trên root đều nằm sau khi chờ fence; lần đọc trước root chỉ để từ chối sớm (bài học `PR35-IAM-FENCE-TIME-001`). D2 khi triển khai: mọi commit của 4 writer này tăng epoch đúng một lần; replay, từ chối, xung đột và input sai không commit. Replay của `sec.binding` được trả lời trước khi mở root nên không lấy fence; replay của 3 writer còn lại đọc receipt dưới fence và không commit.
- 3 writer 12.3b vào registry: `iam.delegation-reissue` qua port `IWorkflowDelegationReissuer`. Reissuer nay khoá fence trước khi đọc principal và delegation hiện hành (D1); `reauthorize_delegation` khoá lại trong cùng transaction và chỉ tăng epoch một lần. `iam.delegation-revoke` và `iam.service-manifest` do IAM tự khai báo trong `IamAuthorityFunctionWriters`, nên Host không nhắc tên function port ngoài composition workflow (giữ guard của 12.3b). Manifest trùng được trả lời từ receipt trước khi khoá fence (không chờ, không tăng epoch).
- D6: `AuthorityWriterRegistry.Operations` ghi `installation.restore` (không có entry point) kèm [runbook backup/restore](../operations/backup-restore-runbook.md), trong đó có bước `[NEEDS-DECISION]`. `RequireEnforcedCoverage()` vẫn từ chối activation cho tới khi bước đó được duyệt (12.3f/12.4). Mọi writer đã tham gia fence đều có kịch bản race; writer credential registry `sec.registry-binding` được đăng ký không có kịch bản (xem mục phạm vi inventory), nên guard cũng nêu tên nó.
- `[NEEDS-DECISION]` DRIFT-T12-002: reauthorize WF01 tăng epoch (12.3b D4) nhưng vẫn chạy dưới read lease của admission gate, trong khi authority/admission v1 yêu cầu writer gate cho mọi handler đổi authority. Root này cũng ghi thời điểm admission quan sát trước khi chờ fence (`ObservedAtUtc`, hành vi 12.2f) làm thời gian audit, receipt và WFL floor; quyết định commit vẫn được kiểm lại sau fence (`stillCurrent` đọc lại authority với thời điểm mới, `reauthorize_delegation` từ chối validity ≤ `clock_timestamp()`). 12.3c-3 không đổi hành vi này; xem [sổ drift](architecture-drift.md).
- `[NEEDS-DECISION]` Phạm vi inventory: `OfflineRegistryCredentialCoordinator` (binding credential cho artifact registry, SEC) cũng ghi state SEC nhưng chưa từng có trong registry writer (D5 chỉ nêu `sec.binding`), trong khi Worker Authority v1 xếp writer SEC vào nhóm invalidating. Chủ dự án cần quyết: đưa nó vào registry kèm race test (mở rộng 12.3c-3), hay ghi rõ credential của registry không phải fact mà permit đánh giá lại. Các coordinator recovery offline (release, approval, artifact) chỉ đối chiếu bằng chứng, không tạo hay replay mutation, nên không đưa vào. Trong lúc chờ, `AuthorityWriterRegistry` đăng ký writer này là `sec.registry-binding` không có kịch bản race, nên `RequireEnforcedCoverage()` từ chối activation và nêu tên nó (finding Codex `PR37-SEC-REGISTRY-FENCE-001`; chủ dự án chọn chặn activation ngày 2026-11-20). Khi có quyết định: fence writer này kèm race test, hoặc ghi rõ credential của registry không phải fact của permit rồi bỏ nó khỏi registry.
- Test offline: `AuthorityFenceTests` (thêm mặc định `Inactive` cho 4 owner; completeness đủ 3 lớp race; guard nêu `sec.registry-binding` và `installation.restore`). PostgreSQL: `AuthorityFenceRemainingWriterDatabaseTests` (7 kịch bản) **pass** trên batch được duyệt (`108fabe`; database và role run-id đã xoá và kiểm độc lập). Batch đầu tiên được duyệt (`2f09f22`) fail vì lỗi setup của test: target ở phạm vi project không có policy binding `target.read` cấp project, nên writer credential trả `denied` đúng thiết kế sau khi chờ fence. Batch thứ hai (`b5f7a14`) xác nhận bản sửa đó, rồi fail trước race `approval.policy` vì manifest approval policy của chính test cho phép provenance chưa attest với `Staging`. Cả hai batch đều đã kiểm cleanup; product không đổi. `RaceInputsParseWithoutDatabaseAccess` (offline) parse mọi đầu vào tĩnh của race để lỗi đầu vào fail ngay trên máy.
- Gate AMR đầy đủ trên `a92b286` (base chồng `e240c23`): `local_status: passed`, 2.494 pass / 0 fail / 33 skip có gate như dự kiến (32 trước đó + race test mới). Gate đầu tiên trên `108fabe` fail 4 case của `ReleaseHttpTests` vì pin source của OpenAPI `release-approval-v1` cho `ApprovalMutationTransaction.cs` chưa cập nhật sau thay đổi của 12.3c-3; `a92b286` cập nhật pin (wire không đổi).
