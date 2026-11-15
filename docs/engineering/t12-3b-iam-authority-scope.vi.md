---
id: ENG-T12-3B-IAM-AUTHORITY-SCOPE
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-14
---

# T12.3b — IAM service/delegation/permit/fence: phạm vi và thiết kế chi tiết

> Accepted scoped design ngày 2026-11-14 (D1–D6 như đề xuất); xem Exact scoped acceptance ở cuối file. **Implemented (inactive)** — xem Implementation evidence. Không thay đổi [ADR-0022](../decisions/0022-controlworker-authority.md), [Worker Authority v1](../contracts/worker-authority-v1.md), [Durable Workflow v1](../contracts/durable-workflow-v1.md) hay [conformance plan](t12-conformance-plan.md). Không cấp quyền DB, role, cert, listener, operation, activation hay merge. Chưa có implementation.

## 1. Mục tiêu và lộ trình 12.3

12.3b hiện thực phần IAM của Worker Authority v1 trên nền đã chứng minh ở [12.3a](t12-3a-owner-function-poc-scope.vi.md): service registry, delegation lưu bền, permit cấp/tiêu thụ dưới global fence và các owner port công khai; đồng thời đóng phần IAM của WF01 ([WF01 scope](t12-wf01-reauthorization-scope.vi.md) §9.9): ghi generation 1 khi start và implementation production của `IWorkflowDelegationReissuer`.

| Slice | Nội dung | Cổng duyệt riêng |
|---|---|---|
| **12.3b** | Tài liệu này | thiết kế; batch DB; merge |
| 12.3c | Mọi invalidating writer (session/logout/boot, role/policy, tenant/membership/assignment, lifecycle/target, REL/APR, SEC, service) lock fence + tăng epoch trong cùng owner/AUD root; race test AU03/AU04 theo từng entry point | thiết kế; batch DB; merge |
| 12.3d | Listener mTLS nội bộ (tắt mặc định), xác thực cert/pin/SAN/EKU, RPC `POST /internal/v1/workflow-admissions`, credential file của worker, CLI bootstrap/rotate/revoke; chỉ dùng CA/cert **test** trong fixture | thiết kế; batch DB; merge; tạo CA/cert/listener thật cần operation approval riêng |
| 12.3e | `Kordra.ControlWorker`: composition, consumer tĩnh, khóa một holder, readiness/drain/restart, function surface còn lại của worker (12.3a §8.2), ledger CAP01 của MSG, rời `BlockedAuthority` theo generation hiện hành | thiết kế; batch DB/process; merge |
| 12.3f | Conformance hai process cục bộ, artifact migration/ACL/upgrade offline, ma trận AU/DB/HO, route HTTP AU05/WF01 | thiết kế; batch; merge |

Mỗi slice là một PR, theo đúng thứ tự phụ thuộc; remote/independent-host là T12.4.

## 2. Hiện trạng có bằng chứng (main `f7dd783`)

- `iam-workflow-authority-v1.sql` (PoC 12.3a): `authority_fences`, `service_logins`, `permits` tối thiểu, 6 function; chưa có delegation, service registry đầy đủ hay issuance. Chưa từng được triển khai ngoài database tạm.
- Start root (`WorkflowRootTransaction.StageAdmittedStartAsync`) ghi WFL + AUD + floor nhưng **không tạo delegation**; `authority_bindings` generation 1 hiện chỉ là dữ liệu fixture.
- `WorkflowReauthorizationTransaction` (12.2f) có port `IWorkflowDelegationReissuer` chưa có implementation production.
- IAM có `IDurablePrincipalReader`/`IDurableGrantEvidenceReader` đọc theo principal (không cần browser session) và `KordraSession.IsRevoked` cho logout/thu hồi tường minh.
- `ck_authority_bindings_versions` (WFL rev6) bắt buộc `expected_version > 0`, nên generation 1 lúc start (tạo từ 0 → 1) chưa biểu diễn được.

## 3. Phạm vi

**Trong 12.3b:** IAM artifact v2; WFL revision 7 + owner function v2 (kiểm generation hiện hành); start root ghi delegation + binding generation 1; reissuer production; permit issuer (owner port, inactive, chưa có RPC); service registry functions; test offline + PostgreSQL; tài liệu owner.

**Ngoài 12.3b:** writer coverage (12.3c); mTLS/RPC/CLI/cert (12.3d); worker host, worker function surface còn lại và CAP01 (12.3e); route HTTP, two-process (12.3f); mọi activation, migration hay role trên database thật.

## 4. Quyết định đề xuất (chủ dự án chốt)

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D1 | Cách tiến hóa `iam_workflow` | `iam-workflow-authority-v2.sql` là **baseline thay thế đầy đủ** (không phải ALTER trên v1). v1 giữ nguyên làm artifact PoC lịch sử, không bao giờ triển khai; artifact migration thật là việc 12.3f. Tránh mọi `DROP`/`CREATE OR REPLACE`. |
| D2 | Thời hạn delegation | `valid_until = min(workflow deadline, hạn grant workflow.start và release.read, hạn policy, hạn approval nếu có)`. **Không** gắn với idle/absolute expiry của browser session; chỉ mất hiệu lực khi thu hồi tường minh, logout (`IsRevoked`), đổi `PrincipalRevision`/`BootGeneration`, hoặc grant/owner fact đổi (đúng Worker Authority v1). |
| D3 | Generation 1 lúc start | Start root tạo delegation generation 1 (IAM) và `authority_bindings` generation 1 (WFL) **nguyên tử** cùng WFL/AUD/floor. WFL revision 7 thay `ck_authority_bindings_versions` bằng ràng buộc cho phép đúng một dạng mới `(generation = 1, expected_version = 0, resulting_version = 1)`; version của instance lúc start giữ nguyên là 1. Phương án loại: tăng version instance khi start (đổi start receipt/If-Match). |
| D4 | Reauthorization | Reissuer là invalidating writer ngay ở 12.3b: trong cùng root reauth, lock fence → thu hồi delegation hiện hành → `advance_epoch` → tạo delegation generation kế tiếp; permit cũ chết ngay (AU05). Không đợi 12.3c. |
| D5 | Permit | Mỗi lần thử một permit: khóa `(operation_id, attempt)`, tối đa **một** permit được consume cho mỗi operation; TTL = `min(2 s, valid_until − now)`; issuer đánh giá lại toàn bộ fact qua reader bền (không cần browser session) rồi mới gọi `issue_permit` dưới fence. Hết hạn/thu hồi thì cấp permit mới, không gia hạn. Issuer là owner port inactive; RPC ở 12.3d. |
| D6 | Service registry | Function bootstrap/rotate/retire/revoke nhận manifest hash + AUD intent, chạy dưới fence; rotate/retire/revoke tăng epoch. Pin là SPKI SHA-256, tối đa 2 pin hiệu lực, chồng lấn ≤ 10 phút. Không online enrollment; CLI và cert ở 12.3d. |

## 5. Thiết kế chi tiết — bản nháp chờ duyệt

### 5.1 IAM artifact v2 (`iam-workflow-authority-v2.sql`, D1)

Cùng pattern 12.3a §8.3 (definer, `search_path` cố định, owner role riêng, `REVOKE … FROM PUBLIC`, grant đúng signature, lỗi `KD0xx`). Bảng (do `kordra_iam_owner` sở hữu):

- `authority_fences(installation_id PK, epoch ≥ 1, manifest_revision ≥ 1)` — như v1.
- `services(service_id PK, installation_id FK, generation ≥ 1, status ∈ {active, retired, revoked}, login_name UNIQUE, revoke_revision ≥ 0, created_at, changed_at)`.
- `service_bindings(service_id, generation, tenant_id, environment_id NULL, template_id, template_version, transition_kind ∈ {admit, checkpoint, timer_due, cancel})` — allowlist; không wildcard tenant.
- `service_pins(service_id, generation, spki_sha256 char(64), not_before, not_after, retired_at NULL)` — check tối đa 2 pin hiệu lực.
- `service_manifests(manifest_sha256 PK, kind, service_id, applied_at, audit_intent_id)` — idempotent theo hash.
- `delegations(delegation_id PK, installation_id, tenant_id, instance_id, generation ≥ 1, actor_id, session_id, boot_generation, principal_revision, project_id, environment_id, release_id, release_revision, release_hash, template_id, template_version, specification_hash, transition_class, valid_until, created_at, revoked_at NULL, revoke_reason NULL)`; unique `(installation_id, tenant_id, instance_id, generation)`; tối đa một generation chưa thu hồi cho mỗi instance.
- `permits(permit_id PK, attempt ≥ 1, service_id, generation, installation_id, tenant_id, instance_id, step_id, operation_id, delegation_id FK, expected_version, specification_hash, template_hash, release_hash, transition_kind, authority_epoch, boot_id, custody_epoch, issued_at, expires_at, consumed_xact NULL, consumed_at NULL)`; unique `(operation_id, attempt)`; partial unique `(operation_id) WHERE consumed_xact IS NOT NULL`; TTL ≤ 2 s.

Function (signature cuối cùng ghi trong manifest; grant như bảng):

| Function | Gọi bởi | Ghi chú |
|---|---|---|
| `current_service`, `acquire_fence`, `advance_epoch`, `advance_floor`, `require_consumed` | như 12.3a | `require_consumed` trả thêm `delegation_id`, `generation` |
| `create_delegation(...)` | API | start root; không tăng epoch; idempotent theo `(instance, generation)` + hash |
| `reauthorize_delegation(instance, expected_generation, ...)` | API | D4: fence → revoke hiện hành → epoch++ → generation + 1 |
| `revoke_delegation(delegation_id, reason)` | API | fence + epoch++ (12.3c gắn vào các writer) |
| `issue_permit(...)` | API | fence; delegation chưa thu hồi, còn hạn, đúng generation hiện hành; service active + binding cho phép; ghi epoch hiện tại; TTL theo D5 |
| `consume_permit(...)` | Worker | như v1 + kiểm delegation chưa thu hồi/còn hạn, service còn active |
| `apply_service_manifest(kind, manifest_sha256, ...)` | offline owner (12.3d CLI) | D6; AUD intent cùng root |

### 5.2 WFL revision 7 và owner function v2

- `wfl-v7-original-binding.sql`: thay `ck_authority_bindings_versions` theo D3 (manifest ghi rõ `constraintReplaced`, không xóa dữ liệu); `WorkflowDbContext` revision 7.
- `WorkflowAuthorityBindingStore.StageOriginalAsync` (start root, generation 1, `expected_version = 0`, `resulting_version = 1`, request/command hash của start).
- `wfl-owner-functions-v2.sql` (baseline thay thế, như D1): `stage_transition` kiểm `delegation_id` của permit trùng delegation của binding generation **hiện hành**; generation cũ bị từ chối (`KD109`).

### 5.3 Host (inactive, không route/consumer)

- Start root: sau khi stage WFL, gọi `create_delegation` và `StageOriginalAsync` trên **cùng** root; `valid_until` theo D2 từ fact đã admit; delegation và binding nằm trong cùng duplicate/rollback như start hiện có.
- `IamWorkflowDelegationReissuer` (production `IWorkflowDelegationReissuer`): gọi `reauthorize_delegation` trên root reauth hiện có (D4).
- `WorkflowPermitIssuer` (owner port, inactive): nhận service identity đã xác thực (12.3d sẽ cung cấp), instance/step/operation/expected version/hash/kind; đọc delegation hiện hành; đánh giá lại principal (`IDurablePrincipalReader`: active, revision, boot), session không bị thu hồi, grant/policy/approval/release qua reader bền; rồi `issue_permit`. Không giữ transaction qua I/O mạng.

### 5.4 Test bắt buộc

**Offline:** artifact/manifest/lint/grant như 12.3a; parity cạnh trạng thái; tính `valid_until` (D2) theo từng nguồn hạn; tính TTL (D5); không có Host composition runtime.

**PostgreSQL (gated, cần duyệt batch):** start ghi delegation + binding gen 1 nguyên tử (rollback/duplicate không để lại dấu vết); reauth tạo gen 2, permit gen 1 bị từ chối sau epoch++ (AU05); issue → consume → transition với generation hiện hành; permit hết hạn → attempt mới, chỉ một consume; service retired/revoked, binding không cho phép, sai tenant → deny; thu hồi delegation dưới fence trong khi worker giữ fence (chờ có giới hạn); DB01 cho các function mới.

### 5.5 Tài liệu cùng PR

FEAT-IAM-001, FEAT-WFL-001 (+ FEAT-AUD-001 nếu đổi), Worker Authority v1 chỉ thêm ghi chú implementation evidence, conformance plan (AU02/AU05 subset), traceability, change history.

## 6. Rủi ro

- `issue_permit` và reissuer giữ fence: tăng tranh chấp; ngân sách khóa 1 s như 12.3a.
- Đánh giá lại fact không có browser session là bề mặt mới; mọi trường hợp thiếu fact phải fail-closed.
- Chưa có writer coverage (12.3c): trong khoảng 12.3b–12.3c, chỉ reauth/revoke delegation tăng epoch; vẫn inactive nên không có runtime exposure.

## 7. Bước tiếp theo

Chủ dự án duyệt đúng nội dung (gồm D1–D6) → triển khai trên `codex/t12-3b-iam-authority` → test offline → xin duyệt batch DB (case + gate) → PR.

## Exact scoped acceptance — 2026-11-14

NukeGeng đã chọn **"Duyệt thiết kế"** cho đúng nội dung tài liệu này (gồm D1–D6 như đề xuất), trình với SHA-256 trước annotation `74f790e7f6a523058fd43c73e21baeec5dabe9e0e0668626d368b51e33ada05b`. Bằng chứng riêng tại `.amr-local/t12-integration/t12-3b-design-acceptance/`.

Accepted chỉ cho thiết kế slice 12.3b. Không cấp quyền DB, role, cert, listener, operation, activation hay merge; mỗi batch PostgreSQL cần manifest và approval riêng. Chủ dự án cho phép sẵn: push nhánh và mở PR cho từng slice 12.3 khi gate local đã qua.

## Implementation evidence — 2026-11-15

- Source: `iam-workflow-authority-v2.sql` (+ manifest), `WorkflowDelegationEvidence`, `WorkflowDelegationSessionReader`, `WorkflowAuthorityFunctionPort` v2; `wfl-v7-original-binding.sql`, `wfl-owner-functions-v2.sql`, `StageOriginalAsync`; Host `StageAdmittedAuthorityStartAsync`, `IamWorkflowDelegationReissuer`, `WorkflowPermitIssuer`, `WorkflowDelegationAuthority`.
- Bổ sung nhỏ không đổi thiết kế: `iam_workflow.services.kind` (api/worker) để `current_service` phân biệt login API và worker; permit kiểm thêm `step_id` (`KD101`); `read_current_delegation` cho issuer/reissuer; các field tùy chọn additive (`ApprovalValidUntilUtc`, `PolicyExpiresAtUtc`, `Generation`, facts trong `WorkflowDelegationReissueRequest`) để tính D2 đúng hạn thật thay vì cửa sổ đánh giá 10 s.
- Test offline: pass. PostgreSQL: `WorkflowAuthorityStartDatabaseTests`, `WorkflowAuthorityWorkerDatabaseTests` — chờ batch được duyệt.

