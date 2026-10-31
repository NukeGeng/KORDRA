---
id: OPS-MSG
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-31
---

# Runbook — Backlog, poison message and DLQ redrive

## Detection
Queue depth, oldest-message age, outbox relay age, failure rate, `messageId/correlationId`, consumer version, broker health.

## Triage
Classify transient provider timeout vs schema incompatibility vs tenant/authorization failure. Schema/security errors must be quarantined, not endlessly retried. Check if external deployment side effect may have already occurred.

## Controlled recovery
1. Preserve DLQ message ID, sanitized envelope, original attempts, failure reason and target.
2. Ensure consumer version/schema fixed and tenant/policy are still valid.
3. For side-effect commands, **observe target + operation journal** before replay; duplicate dedup protection is not proof of exactly-once physical mutation.
4. Redrive explicitly with scoped admin approval, dedicated audit and rate limit.
5. Verify stuck queue cleared, no duplicate deployment, record post-incident follow-up.

## Never
Bulk replay all DLQ messages without categorization, replay expired production approval, delete queue to suppress incident, or expose secrets in diagnostics.

## T12.1 kernel — trạng thái và phạm vi thực thi

[ADR-0020](../decisions/0020-messaging-runtime-adoption.md) và [Durable Messaging v1](../contracts/durable-messaging-v1.md) chỉ duyệt library kernel, chưa kích hoạt business API/ControlWorker hoặc distributed authority. Hướng dẫn redrive tổng quát phía trên là quy trình tương lai, **không phải lệnh replay đã triển khai hoặc quyền thao tác hiện tại**. T12.1 chưa có CLI replay/migrate/repair installation; không tự tạo SQL sửa receipt hay gọi native ClearAll/Rebuild để phục hồi. Runbook này tiếp tục là đầu mối hiện có, không tạo runbook MSG thứ hai chỉ vì bảng thiết kế nêu tên file khác.

Actor: maintainer được cấp quyền đúng installation; owner kỹ thuật MOD-MSG phối hợp owner nghiệp vụ và AUD. Tenant/system scope phải rõ ràng; metadata message không cấp permission. LAB-01/company mặc định observe-only. Mọi DB/role/HBA/credential operation cần quyền riêng cho target/case, độc lập với duyệt thiết kế.

### Preflight trước inspect hoặc thay đổi

1. Ghi incident/case ID và target identity riêng tư; xác nhận schema/profile/lock graph cùng source revision đang chạy. Không công bố locator, password, body hoặc topology công ty.
2. Xác định process nào đang giữ transaction/custody, commit đã được thử hay chưa và outcome thực. Không restart process khác hoặc đoán process đã dừng từ một lock file.
3. Xác minh exact logical key: installation + explicit tenant/system + operation + step/version + type, canonical binding hash, original deadline, native delivery ID **và destination**. Không lookup chỉ theo operationId hoặc tin tenantId từ body như authority.
4. Kiểm tra native/owned schema revision và manifest hash; sai/missing/unavailable thì từ chối activation. Artifact review không cấp quyền apply. Sau đổi profile IdAndDestination cần manifest hiện hành; kết quả ACL lịch sử không thay thế restricted-startup proof.

### Phân loại outcome và hành động an toàn

| Evidence | Hành động |
|---|---|
| Setup lỗi trước commit-attempt | Phân biệt với Unknown; kết thúc/rollback transaction theo owner và xác nhận cleanup. Không đóng host admission chỉ vì best-effort setup failure. |
| Root rollback được xác nhận | Không có durable receipt/effect từ transaction đó. Chỉ tiến hành lần thử được policy/deadline/budget cho phép sau khi transaction cũ terminal. |
| Confirmed commit | Receipt/effect/inbox/result đã durable theo root transaction. Chỉ lúc này mới flush; mất flush/ACK để native outbox recovery xử lý, không chạy lại business callback. |
| Có thể commit nhưng mất reply | Unknown: dừng đường thực thi của owner, giữ custody, không trả success hoặc đưa generic retry chạy lại callback. Cancellation, timeout hay Dispose không chứng minh rollback. |
| Lookup exact key/hash thấy terminal receipt | Đối chiếu đúng scope/hash/outcome bằng reader độc lập và custody; ghi reconcile evidence. Transport ACK không đồng nghĩa deploy đã verify. |
| Lookup thấy hash khác | Conflict: giữ receipt cũ, quarantine metadata; không sửa hash/outcome hoặc tráo artifact để “khớp”. |
| Không thấy receipt khi operation cũ chưa terminal | StillUnknown; tiếp tục điều tra, không suy no-commit, không mở writer cạnh tranh hoặc redrive. |

T12.1 chỉ có fixture single-process cho Unknown/restart; không suy ra multi-node takeover hoặc distributed authorization (gate T12.3). Không mượn superuser làm runtime-ACL proof.

### Retry, deadline và dung lượng

- Tối đa 5 handler attempts: initial và các delay 1/5/15/60 giây cho transient allowlist. Reject/UnknownCommit không tự retry. Native delivery attempts khác business handler attempts.
- Original lifetime tối đa 120 giây; mỗi attempt kiểm tra current effective time. Timer chưa đến hạn không chạy, deadline không được gia hạn bằng delivery ID mới. Planner thuần chưa chứng minh timer đã persisted/recovered.
- Pending >10.000 thì ngừng admit publication mới; không drop committed envelopes để làm xanh health. DB unavailable thì deny new work. Đây là watermark availability guard, chưa là hard quota cho concurrent writers.
- Native Handled giữ ít nhất 7 ngày; resend/recovery horizon 24 giờ. Logical receipts/quarantine/recovery giữ ít nhất 90 ngày, hiện không automatic purge. Không xóa active/scheduled/reconciling rows hoặc immutable receipt để “unblock”.

### Quarantine, evidence và điều kiện dừng

Chỉ lưu reason đóng + IDs/hash và timestamps; không raw payload/exception dump. `RejectedEnvelopeDiagnostic` và `NativeMessageIngressRule` có direct/binary tests; Fixture native ingress/DLQ/log và fixture hết5lượt retry/DLQ đã đạt trên revision ghi trong receipts. Chính sách terminal vẫn là test-only; kiểm chứng sensitive-original-exception logger path và toàn bộ gate còn thiếu vẫn cần đạt trước activation. Không tuyên bố một helper đã bảo vệ default native error path khi chưa gắn và chạy kiểm chứng. Không automatic redrive hoặc biến quarantine thành authority.

Mỗi phép thử isolated phải có proposal/approval, SQL+source hashes, own PID/host list, observed pre/post counts và cleanup evidence. CREATE phải fail khi schema đã tồn tại; chỉ DROP schema mới thực sự do case tạo sau khi own work dừng. Không dùng permission case trước cho case mới. Cleanup lỗi hoặc unknown process/transaction phải được báo và giữ evidence; không tiếp tục bằng cách xóa rộng hơn.

Evidence hiện có và giới hạn cập nhật tại [FEAT-MSG-001](../features/messaging/features/durable-delivery.md). Green targeted tests không thay full gates/CI/PR review, không cấp quyền deploy/merge. Chỉ đóng incident/feature sau khi outcome, không lặp effect, native state, dữ liệu nhạy cảm và cleanup được kiểm chứng trên đúng revision. Acceptance T12.1 tổng thể vẫn chưa hoàn tất.


### GitHub CI fixture manifests

Product workflow creates fresh synthetic MSG manifest identities after its Release build using `tests/Messaging.Tests/prepare_ci_fixtures.py`. Only the existing ephemeral GitHub-hosted Linux PostgreSQL service is in scope; the helper emits offline DDL and JSON/env files, not SQL operations. Local operations continue to require their own exact owner approval. Environment markers are a misuse guard, not authenticated permission: never impersonate CI on a Mac/shared runner to manufacture approved_for_execution manifests. Do not copy runner manifests into local operation evidence or claim generated configuration proves a successful DB test/CI run. A failed generator exports nothing and must block dependent tests rather than selecting a fallback database. No Product operation may recursively inspect or test Clients/Website.

### Native logger coverage gap

Pinned native Executor logs the original exception before choosing a failure continuation. A sanitized terminal DLQ is therefore insufficient evidence for log safety. Follow the bounded [owner decision](../features/messaging/features/durable-delivery.md) before activation; do not silently disable logging or assume every logger/tracker/telemetry path is covered by the envelope diagnostic helper.

### Explicit diagnostic library components

MSG Infrastructure now contains MessageDiagnosticLoggingProvider and MessageTrackerDiagnosticExport. A dedicated composition must select the protected downstream sink explicitly; these components do not replace global logging or register a host. Do not pass raw tracked sessions/exception objects to artifacts or exporters instead of the closed projection. Provider protection preserves caller exceptions and cannot turn UnknownCommit into retry authority. Native success/processing visibility depends on the configured log threshold; targeted test Trace configuration is not a production default. Current-profile/activation/full gates remain required.
