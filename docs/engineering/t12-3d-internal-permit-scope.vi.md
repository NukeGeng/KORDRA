---
id: ENG-T12-3D-INTERNAL-PERMIT-SCOPE
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-27
---

# T12.3d — listener mTLS nội bộ, RPC cấp permit và CLI `service`: phạm vi và thiết kế chi tiết

> **Accepted scoped design ngày 2026-11-27** (D1–D11; D7, D8, D9 theo phương án (a)); xem *Exact scoped acceptance* ở cuối file. Chưa có implementation. Tài liệu hiện thực phần còn lại của [Worker Authority v1](../contracts/worker-authority-v1.md) ở hai mục *Service identity và internal listener* và *Delegation, permits và wire*, theo thiết kế T12 đã accepted ngày 2026-10-31 ([ADR-0022](../decisions/0022-controlworker-authority.md)) và bảng chia slice của [12.3b](t12-3b-iam-authority-scope.vi.md). Không đổi ADR hay nội dung contract. Không cấp quyền DB, role, CA, cert, listener, activation hay merge.

## 1. Mục tiêu

Cho API cấp permit cho worker qua một kênh nội bộ được xác thực bằng mTLS:

- Listener riêng, tắt mặc định.
- Chứng chỉ worker được xác thực theo chain CA riêng, SAN, EKU và hạn. Pin SPKI được kiểm với registry IAM **dưới fence** ở mỗi yêu cầu.
- RPC `POST /internal/v1/workflow-admissions` gọi `WorkflowPermitIssuer` đã có.
- CLI offline `service` để bootstrap, rotate, retire và revoke service.
- Worker có file credential.

Mọi thành phần đều inactive. Fixture chỉ dùng CA và cert test.

## 2. Hiện trạng có bằng chứng (main `75f5cf1`)

| Thành phần | Hiện trạng |
|---|---|
| IAM artifact `iam-workflow-authority-v2.sql` (inactive, chưa deploy) | `issue_permit` (12 tham số) chỉ cấp cho `kordra_api_runtime`. Caller phải là service `kind = 'api'` qua `current_service()`, hàm này ánh xạ **chỉ** theo `session_user`. Worker đích do caller truyền vào (`p_service_id`). Hàm kiểm status, kind, binding và delegation nhưng **không kiểm pin chứng chỉ nào**: nó không đọc `service_pins`. |
| Pin và manifest | Bảng `service_pins(spki_sha256, not_before, not_after, retired_at)`. Rotate giữ generation, cho pin cũ hết hạn tại `LEAST(now + 10 phút, not_after)` và giữ tối đa 2 pin hiệu lực (`KD023`). Retire/revoke cho mọi pin hết hạn ngay. Mọi loại manifest đều tăng epoch. `apply_service_manifest` không được cấp cho role nào. |
| `WorkflowPermitIssuer` (Host, 12.3b) | Fence → delegation hiện hành → đánh giá lại principal, session, release, resource và policy → `issue_permit`. Chỉ test gọi (2 file); không có route, RPC hay đăng ký DI. |
| Host API | `LocalApiHost` có một listener HTTPS `127.0.0.1:5128` với chứng chỉ server và không yêu cầu chứng chỉ client. Không có route `/internal`. |
| CLI offline (`Program.cs`) | Có `identity`, `policy`, `credential`, `registry-credential`, `approval-policy` và các lệnh `*-recovery`; **không có `service`**. Các lệnh offline dùng locator admin offline; theo D7 của 12.3c, login này được bootstrap thành một service `api` riêng. |
| Worker | Module WFL không có HTTP client hay code X509. Chưa có worker host (thuộc 12.3e). |

## 3. Phạm vi

**Trong 12.3d** (theo bảng chia slice đã accepted):

- Listener mTLS nội bộ, tắt mặc định.
- Xác thực cert, pin, SAN và EKU.
- RPC `POST /internal/v1/workflow-admissions`.
- File credential của worker.
- CLI bootstrap/rotate/retire/revoke.
- Chỉ CA và cert **test** trong fixture.

**Ngoài 12.3d:**

- Worker host, custody epoch, readiness và drain (12.3e).
- Conformance hai process, route HTTP, migration và ACL production, activation (12.3f).
- Host độc lập (T12.4).
- Tạo CA, cert hay listener thật (cần operation approval riêng).
- Các quyết định đã mở từ 12.3c: phạm vi credential registry (`sec.registry-binding`), DRIFT-T12-002 và bước runbook D6.

## 4. Quyết định đề xuất (chủ dự án chốt)

| # | Chủ đề | Đề xuất |
|---|---|---|
| D1 | Listener nội bộ | Một `WebApplication` Kestrel **riêng**, không dùng chung pipeline API cho trình duyệt. Bind đúng một địa chỉ IP:port lấy từ manifest listener (không wildcard, không địa chỉ mặc định). TLS 1.3, HTTP/1.1, `ClientCertificateMode.RequireCertificate`. Không có middleware cookie, auth, CORS, CSRF hay static. Chỉ hai endpoint: `POST /internal/v1/workflow-admissions` và `GET /internal/v1/ready` (trạng thái tối thiểu, cũng đòi mTLS). Request có header `Cookie` hoặc `Authorization` bị từ chối, và response không bao giờ set cookie. **Trong 12.3d không có composition runtime nào dựng listener**; chỉ test dựng qua một factory tường minh. |
| D2 | Xác thực TLS (không I/O DB trong handshake) | Trust tùy chỉnh đúng CA riêng của installation: `CustomRootTrust`, không dùng trust store hệ thống, `RevocationMode.NoCheck`, `DisableCertificateDownloads` (không AIA/OCSP). Leaf phải có EKU `clientAuth`, còn hạn, khóa ECDSA P-256/P-384 hoặc RSA ≥ 3072, và **đúng một** URI SAN `urn:kordra:installation:<installationId>:service:<serviceId>` với installation của host. Kết quả là `(serviceId, spkiSha256)` của leaf. Thu hồi đi qua registry (pin hết hạn, revoke, epoch), không qua CRL hay OCSP. |
| D3 | IAM artifact revision 3 | `iam-workflow-authority-v3.sql` thay v2 (chưa deploy, giống cách v2 đã thay v1). `issue_permit` thêm tham số `p_spki_sha256` và lưu nó trên permit. **Dưới fence**, hàm đòi một pin của generation hiện hành sao cho: SPKI khớp, `not_before ≤ clock_timestamp() < not_after`, và `retired_at` rỗng hoặc sau `clock_timestamp()`. Nếu không, trả `KD024 pin_not_current`. Replay cùng permit cũng phải cùng pin. Không còn biến thể không pin. Thứ tự lock và các mã lỗi khác giữ như v2. |
| D4 | Wire RPC schemaVersion 1 | Body có đúng các trường `schemaVersion`, `requestId` (= `permitId`), `tenantId`, `instanceId`, `stepId`, `operationId`, `expectedVersion`, `specificationHash`, `transitionKind`, `bootId`, `custodyEpoch`; trường lạ bị từ chối. Body **không** chứa `serviceId`, actor, grant hay epoch, vì service lấy từ mTLS. Giới hạn theo contract: request ≤ 16 KiB, depth ≤ 8, deadline 2 s, response ≤ 8 KiB, tối đa 16 request đang xử lý cộng hàng đợi 16 (vượt thì trả `unavailable` ngay), không redirect. Response là `issued` kèm `permitId`, `attempt`, `authorityEpoch`, `expiresAtUtc`, hoặc `denied`/`unavailable` kèm `reasonCode` cố định. Không có cookie, token hay evidence. Retry cùng `requestId` trả đúng permit cũ (duplicate) hoặc conflict. |
| D5 | Thứ tự root phía API | Read lease của admission gate → root `READ COMMITTED` → issuer lấy fence → đọc spec WFL theo installation/tenant/instance và đòi hash khớp → đánh giá lại fact → `issue_permit` (pin kiểm dưới fence) → AUD intent "permit issued" cùng root → clock floor → commit. Bất kỳ lỗi nào trước commit trả `unavailable`, không ghi nửa chừng. Kết quả chỉ được trả sau commit. |
| D6 | CLI `service` | `service inspect <manifest> <admin-locator>`, `service apply <manifest> <admin-locator> <approved-sha256>` và `service spki <certificate.pem>`. Lệnh `spki` in SPKI SHA-256 của chứng chỉ công khai và không đọc private key. Mỗi file manifest chỉ một loại (bootstrap, rotate, retire hoặc revoke), gồm database và role mong đợi, installation, service, kind, login, pin (spki, notBefore, notAfter) và binding. Hash tính trên dạng canonical. API phải dừng (offline lease như các lệnh khác). AUD intent ghi cùng root với `apply_service_manifest`; replay trả từ receipt của manifest. |
| D7 | Danh tính chạy `apply_service_manifest` offline | **Đã chốt (a), 2026-11-27:** artifact v3 tạo một role NOLOGIN chỉ có EXECUTE trên `apply_service_manifest`; provisioning cấp role này cho login admin offline. Không chọn (b): CLI dùng locator riêng của owner IAM, vì owner có quyền DDL trên toàn schema, rộng hơn mức cần. Theo cả hai cách, không role runtime nào có quyền này. |
| D8 | File credential và client phía worker | **Đã chốt (a), 2026-11-27, đúng bảng chia slice:** 12.3d thêm vào WFL Infrastructure một loader file credential và một client RPC inactive. Loader đòi thư mục 0700 và file 0600 thuộc tài khoản service; file chứa cert/key PEM, CA, pin SPKI của server API, endpoint và installation/service id; không đọc env hay args, không ghi log. Client trust CA riêng cộng pin server, không qua proxy, không redirect, deadline 2 s. Không chọn (b): dời client sang 12.3e, để 12.3d chỉ có loader và một test client. |
| D9 | Audit permit bị từ chối | **Đã chốt (a), 2026-11-27:** chỉ audit permit được cấp, bằng AUD intent trong root cấp. Denial trả mã cố định cộng một bộ đếm không định danh, không ghi AUD. Không chọn (b): ghi thêm sự kiện AUD cho denial, vì cách này cần ghi AUD ngoài một root thành công và gây nhiều nhiễu hơn. |
| D10 | Test PKI | CA và cert sinh ngay trong test (`CertificateRequest`, ECDSA P-256, hạn ngắn). Không ghi vào repo; file tạm có quyền 0600. Không dùng CA, cert hay listener thật. |
| D11 | Chia PR | **12.3d-1:** artifact IAM v3, port, CLI `service` và test DB. **12.3d-2:** listener, xác thực TLS, RPC, loader và client (tùy D8), ma trận TLS âm và test DB end-to-end qua loopback. |

## 5. Thiết kế chi tiết — bản nháp chờ duyệt

### 5.1 IAM (12.3d-1)

- v3 = v2 cộng cột `permits.spki_sha256 character(64) NOT NULL`, hàm `issue_permit(..., p_spki_sha256)`, mã lỗi `KD024 pin_not_current` và grant theo D7. Kèm manifest artifact mới (revision 3, `replaces` v2, có sha256).
- Port `WorkflowAuthorityFunctionPort.IssuePermitAsync` nhận SPKI. `WorkflowPermitIssuer` nhận cặp `(serviceId, spkiSha256)` đã xác thực và truyền xuống. Không đổi ngữ nghĩa epoch hay fence.
- Ảnh hưởng: race harness của 12.3c (`IssueProbeAsync`) dùng pin đã đăng ký; test 12.3b và 12.3c cập nhật theo chữ ký mới.

### 5.2 CLI `service` (12.3d-1)

- Theo mẫu `credential` và `approval-policy`: parse manifest chặt (trường lạ là invalid), tính hash canonical. `inspect` không ghi gì. `apply` đòi hash đã duyệt phải khớp, lấy offline lease, gọi `apply_service_manifest` và ghi AUD cùng root; replay và conflict trả từ receipt.
- `service spki` đọc chứng chỉ PEM, từ chối nếu thấy private key, và in SPKI SHA-256 dạng hex.

### 5.3 Listener và xác thực (12.3d-2)

- `InternalAdmissionListener` (Host) là một factory nhận manifest listener (địa chỉ, CA, chứng chỉ server, installation id) và issuer, rồi dựng Kestrel riêng theo D1. Manifest listener và cặp cert/key của server là file private (0600) nằm ngoài repo, không truyền qua env hay args.
- Validator TLS theo D2 chạy trong callback của handshake và không chạm DB.
- Handler RPC theo D4/D5: parse có giới hạn, semaphore 16 cộng hàng đợi 16, deadline 2 s, gọi issuer, rồi đổi các mã `KD0xx` sang `reasonCode` cố định.

### 5.4 Worker (12.3d-2, tùy D8)

- `WorkerCredentialFile` kiểm quyền và chủ sở hữu file trước khi đọc.
- `WorkflowAdmissionClient` (inactive) trust CA cộng pin server và không có đường fallback.

### 5.5 Test

**Offline:**

- Manifest, hash và SPKI; tham số CLI.
- Artifact chỉ cấp `issue_permit` có pin cho `kordra_api_runtime`.
- Ma trận TLS âm: CA sai, chứng chỉ tự ký, hết hạn, chưa hiệu lực, thiếu EKU, SAN thiếu/sai/nhiều, installation khác, khóa yếu, không gửi chứng chỉ.
- Request có `Cookie` hoặc `Authorization` bị từ chối; giới hạn body, depth và trường lạ; quá tải trả `unavailable`; response không set cookie.

**PostgreSQL** (operation-gated, DB disposable, mỗi batch cần duyệt hash):

- Cấp với pin hiện hành → `issued`. Pin lạ, pin của service khác hoặc pin đã hết hạn → `pin_not_current`.
- Trong 10 phút sau rotate cả hai pin dùng được; sau `retired_at` pin cũ bị từ chối.
- Retire/revoke → từ chối, và epoch tăng.
- Race: manifest phải chờ khi issuance đang giữ fence; nếu manifest commit trước thì issuance thấy pin đã hết hạn.
- CLI apply, replay, conflict và AUD.

**Loopback mTLS cộng DB:**

- End-to-end trả `issued`.
- Pin bị retire giữa handshake và request → `pin_not_current`, chứng tỏ registry được kiểm lại mỗi request, không cache.
- DB không sẵn sàng → `unavailable` (fail-closed).
- `requestId` lặp lại → cùng permit.

### 5.6 Tài liệu cùng mỗi PR

Scope này (phần evidence), FEAT-IAM-001, FEAT-WFL-001, traceability, change history và conformance plan. Worker Authority v1 có thêm một implementation note: bốn trường wire `tenantId`, `stepId`, `bootId`, `custodyEpoch` lấy theo mục *Owner function contract details*; nội dung contract không đổi.

## 6. Rủi ro

- mTLS và pin thêm gánh vận hành (giữ CA, rotate). Tạo CA và cert thật là một thao tác riêng, cần duyệt.
- Kiểm pin dưới fence ở mỗi request thêm độ trễ nhưng vẫn trong ngân sách 2 s. Fence toàn installation có thể khiến request phải chờ (≤ 1 s lock timeout), đúng như trade-off đã chấp nhận ở ADR-0022.
- Đồng hồ host API (dùng cho hạn TLS) và DB (`clock_timestamp()`, dùng cho hạn pin) có thể lệch nhau. Khi đó handshake qua nhưng pin bị từ chối; đây là fail-closed, chấp nhận được.
- D8 đã chọn (a), nên phần worker xác thực server có test ngay trong 12.3d-2.

## 7. Bước tiếp theo

Thiết kế đã được duyệt. 12.3d-1 (IAM v3, port, CLI `service`) → batch DB và gate → PR → 12.3d-2 (listener, RPC, phần worker) → batch DB và gate → PR.

## Exact scoped acceptance — 2026-11-27

NukeGeng đã trả lời **"Duyệt thiết kế"** cho đúng nội dung tài liệu này (D1–D11, với phương án đề xuất (a) cho D7, D8 và D9), trình với SHA-256 trước annotation `4889c66ad2ea21a23cec81bc90252180dd43b6e58083bcfbd33a06d82c4d4f95`. Bằng chứng riêng tại `.amr-local/t12-integration/t12-3d-design-acceptance/`.

Accepted chỉ cho thiết kế slice 12.3d (hai PR 12.3d-1 và 12.3d-2). Không cấp quyền DB, role, CA, cert, listener, operation, activation hay merge; mỗi batch PostgreSQL cần manifest và approval riêng. Quyền push và mở PR khi gate local qua vẫn áp dụng như các slice 12.3 trước.

## Implementation evidence — 12.3d-1 (2026-11-27)

- IAM: `iam-workflow-authority-v3.sql` (revision 3, thay v2 chưa deploy). `issue_permit` nhận `p_spki_sha256`, lưu trên permit, và **sau khi khóa fence** đòi pin của generation hiện hành còn hiệu lực theo `clock_timestamp()` và chưa hết hạn (`KD024 pin_not_current`); replay phải cùng pin; không còn biến thể không pin. `apply_service_manifest` chỉ cấp cho `kordra_iam_service_admin` (D7). WFL v2 nay đòi IAM v3; SQL WFL không đổi.
- Host: port và `WorkflowPermitIssuer` mang pin (pin sai định dạng là `request_invalid` trước fence). CLI `service inspect|apply|spki` (`OfflineServiceCoordinator`, qua `IamServiceManifestWriter` của IAM nên Host không tự dựng function port): writer gate → root IAM → hàm IAM khóa fence và tăng epoch → thời gian tin cậy → AUD intent `iam.service-manifest` + clock floor cùng root → commit; replay trả từ receipt. Registry thêm `iam.service-cli` kèm kịch bản race.
- Test offline: `OwnerFunctionV3ArtifactTests` (manifest, grant matrix, pin kiểm sau fence), `ServiceManifestCommandTests` (parse chặt, hash canonical, công cụ pin không đọc private key, từ chối trước khi chạm DB), `AuthorityFenceTests` (completeness gồm `iam.service-cli`). PostgreSQL: `AuthorityFenceServiceCliDatabaseTests` và các test 12.3b/12.3c chạy lại trên v3 — chờ batch được duyệt.
