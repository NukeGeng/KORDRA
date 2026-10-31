---
id: ENG-T12-REMAINING-DESIGN-VI
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# T12.2–T12.4 — thiết kế đầy đủ để duyệt và bàn giao Developer

## 1. Trạng thái thật và kết quả cuối

Baseline được quan sát: PR #23 đã merge vào main `15c764917e030ff256ebed5191c9771a41a58755`; reviewed head `7589662a1dd6aec0d7e4175caa11703739cb52fe`, native Codex vòng2 completed/no findings, ba CI checks đạt. Local worktree vẫn ở reviewed head; không tự pull/reset khi còn user drafts. MSG runtime kernel T12.1 có source/tests, WFL và ControlWorker hiện chỉ là reserved README. Tất cả nội dung mới ở đây **Proposed**, chưa có implementation/acceptance/operation evidence.

Bản này cụ thể hoá ba lát còn lại của [T12](t12-design.vi.md), không đổi số milestone hay tạo hệ AMR khác. Kết quả cuối: workflow persisted, worker riêng có current authority, lỗi/crash phục hồi đúng và evidence host ngoài Mac. Sau đó T13 mới nhận deployment driver + immutable execution plan + provider fencing/verify/rollback. Workflow Completed không được hiển thị như deployment Verified.

## 2. Tài liệu cần duyệt cùng nhau

| Lát | Quyết định | Contract / acceptance |
|---|---|---|
| T12.2 | [ADR-0021](../decisions/0021-durable-workflow-persistence.md) | [Durable Workflow v1](../contracts/durable-workflow-v1.md): identity, states, CAS, timer, Unknown, API/permissions |
| T12.3 | [ADR-0022](../decisions/0022-controlworker-authority.md) | [Worker Authority v1](../contracts/worker-authority-v1.md): service/TLS/permit/fence/SQL owner ports/host |
| T12.4 | [ADR-0023](../decisions/0023-workflow-qualification.md) | [Conformance plan](t12-conformance-plan.md) + [operation runbook](../operations/workflow-worker-pilot.md) |

NukeGeng là custodian đề xuất WFL/IAM/AUD/Host và architecture/security; existing TEN/PRJ/INV/REL/APR/SEC ownership không chuyển sang WFL. Ba ADR vẫn proposed. Duyệt gói thiết kế không cấp quyền migration/role/HBA/CA/trust/host/network/company/provider hay quyền merge. Những lựa chọn công nghệ/boundary mới được liệt kê trong ADR-0022 để tránh adoption ẩn.

## 3. Luồng hệ thống sau implementation và activation được phép

```text
Browser/local console → existing authenticated API owner commands
                       → IAM workflow delegation + WFL instance + AUD + MSG (one root)

ControlWorker → internal API mTLS admission (outside a DB transaction)
              ← exact short-lived permit reference
              → one PostgreSQL root: authority fence + consume permit
                   + WFL CAS/history/timer + AUD/floor + MSG receipt/outbox
              → confirmed commit → native flush/ACK
              → unknown commit → stop admission + exact reconciliation
```

Timer dài nằm trong WFL database; MSG envelope giữ nguyên deadline≤120s. Không biến envelope thành quyền, không cache permit qua authority change. API giữ sole authority writers, worker giữ workflow transitions. Global authority fence là lựa chọn pilot có trade-off throughput và availability, không gọi nó là HA/active-active.

## 4. Những gì người dùng thấy

Người có workflow.start + release.read tại environment Development/Test có thể tạo workflow checkpoint từ đúng release revision/hash đã được owner xác nhận; trả reference sau durable commit. Người có workflow.read xem step/timer/version/reasonCode qua U/D; workflow.cancel chỉ ghi cancellation intent. workflow.reconcile cho phép trình evidence để backend kiểm tra, không cho đặt outcome tuỳ ý. Không cấp các grant này tự động cho role hiện có.

Template pilot thực hiện checkpoint và delay để chứng minh orchestration; không chạm Kubernetes/Nomad/registry. T11 approval giữ executionAuthorized=false. T13 sẽ đăng ký template execution mới sau khi driver/plan/approval/secret/health/fencing được duyệt. Không nhét deploy thật vào template test để sớm hiển thị demo thành công.

## 5. T12.2 — task plan có đầu ra và dependency

| Task | Owner / output dự kiến | Exit / dependency |
|---|---|---|
| 12.2a | WFL Domain/Contracts: typed states, immutable binding, transition rules, canonical vectors | ADR-0021 accepted; pure negative tests; no infrastructure |
| 12.2b | WFL Infrastructure persistence, schema revision/DDL manifests, request receipt/CAS/custody | a; actual isolated PostgreSQL + rollback/uniqueness evidence; offline migration artifacts only |
| 12.2c | Caller-owned WFL/AUD/MSG coordinator ports and actual transaction integration | b + T12.1 public adapter; stage failure/COMMIT-loss proof; no separate AUD commit |
| 12.2d | Timer, bounded scheduler operation, cancellation, reconciliation and retention guards | c; WF04–WF08; deadline120s vs workflow24h explicitly tested |
| 12.2e | Scoped start/read/cancel/reconciliation owner commands + DTO/permission mapping | c/d; current auth/owner U/D and negative permission/tenant tests; positive background activation waits12.3 |

T12.2 có thể test bằng synthetic admitted fixture trước12.3; fixture permit phải chỉ tồn tại trong tests và không được DI-register vào runtime. Không công bố actual distributed authority từ tests đó. API production route/consumer chỉ bật sau12.3 gates, tránh có queue nội dung nghiệp vụ mà chưa có quyền xử lý đúng.

## 6. T12.3 — cross-process protocol trước executable activation

| Task | Owner / output dự kiến | Exit / dependency |
|---|---|---|
| 12.3a | IAM/WFL/AUD function/ACL + root transaction compatibility PoC | ADR-0022 accepted; source/SQL reviewed, actual least-privilege login; direct SQL cross-tenant/raw-DML denial; fail thì stop adoption |
| 12.3b | IAM service/delegation/permit/fence persistence; exact public owner ports | a; mTLS-service/DB session identity binding, SQL allowlist, expiry/revoke/idempotency |
| 12.3c | Every invalidating API/offline writer joins global fence in same owner/AUD root | b; source-path writer inventory + real entry-point race tests AU03/04; no incomplete activation |
| 12.3d | API private mTLS issuer profile; worker private credentials/bootstrap/rotation/revoke | b/c; TLS negative matrix and no user-cookie forwarding; issuer outage fail-closed |
| 12.3e | Kordra.ControlWorker composition, static consumers, lock/readiness/drain/restart | 12.2 + a–d; one active holder, current permit consumes with WFL/AUD/MSG atomicity; no provider resolver |
| 12.3f | Two-process conformance, offline migration/ACL/upgrade artifacts, scope-safe API disclosure | e; AU/DB/HO matrix, exact writer coverage and current required gates/PR review |

Service permit bắt buộc bind workerBootId và custodyEpoch; same permit không được dùng ở DB root khác sau consumed. IAM consumption function ràng buộc actual PostgreSQL full transaction ID, WFL owner functions kiểm tra cùng root/operation/epoch trước staging. Custody epoch+1 chỉ sau predecessor backend terminal + independent reconciliation. Old process thức lại sau pause phải bị reject khi dùng old epoch/permit, kể cả vẫn giữ service certificate còn hạn. Đây là database transition fencing; không suy ra fencing provider.

Các metadata-only worker operations (inspect due timer, current scoped receipt, heartbeat) chỉ qua dedicated service-validated functions; không mutate business transition, tạo permit, publish wake hoặc mark complete khi issuer unavailable. Authority outage test phân biệt rõ metadata progress và workflow business progress.

## 7. Source/dependency plan — chưa phải implemented mapping

| Owner | Planned paths, chỉ tạo khi task được duyệt |
|---|---|
| WFL | `src/Modules/WorkflowOrchestration/Domain/`, `Contracts/`, `Infrastructure/` (Persistence/Features/Transactions/Recovery/Migrations); `tests/WorkflowOrchestration.Tests/` |
| IAM | existing `src/Modules/IdentityAccess/Contracts/`, `Infrastructure/`; new owner features/service-authority/fence functions trong owner project hiện có; `tests/IdentityAccess.Tests/` |
| AUD | existing `src/Modules/AuditCompliance/Contracts/`, `Infrastructure/`: exact worker append/floor port/profile, function migration; `tests/AuditCompliance.Tests/` |
| API/Worker | existing `src/Host/Kordra.Api/` composition folders; reserved `src/Host/Kordra.ControlWorker/` executable and host-specific readiness/config; `tests/ControlWorker.Tests/` |
| Integration | `Kordra.slnx`, current architecture tests/CI allowlist; only runtime-needed package refs/lock consolidation, not broad folder refactor |

WFL Domain/Contracts không phụ thuộc transport/persistence/Host. Infrastructure owns EF stores/functions, Host coordinates other owners through public contracts. IAM không đọc WFL private tables để tự đoán state; command carries exact owner-verified binding via port. WFL không query IAM/TEN/PRJ/AUD internals. DB public owner functions là explicit versioned integration contract, không private-table workaround. MSG kernel không chứa WFL state machine, Agent/Clients không bị thêm dependencies. Không thêm Shared god-module hoặc tools/amr.

New project names proposal: Kordra.Modules.WorkflowOrchestration.Domain, .Contracts, .Infrastructure; Kordra.ControlWorker. Existing IdentityAccess/AUD projects được dùng tiếp. Developer phải pin actual lock graph hợp nhất ở worker và chạy compatibility trước registration; tài liệu không thay package version hiện có. Không tạo speculative project trước slice cần dùng.

IAM feature authorize-resource, AUD immutable-audit và WFL durable-workflow cần ghi exact owned command/function paths khi implement; TEN/PRJ/INV/REL/APR/SEC feature specs phải cập nhật ở commit writer-fence tương ứng. Matrix không tự chuyển source_paths từ [] thành implemented. New API DTOs/internal admission v1 và template registry v1 có golden vectors/compatibility fixtures; schema changes nâng phiên bản nếu breaking, không silent alter accepted v1.

## 8. T12.4 — đầy đủ conformance và topology

12.4a chạy complete local synthetic matrix trên exact SHA với actual DB/process/native packages, không chỉ unit tests. 12.4b kiểm tra migration/upgrade/drain/readiness/backup-restore và redaction, pin image/executable/registry/ACL hashes. 12.4c chạy remote topology theo target-specific operation approval. 12.4d tổng hợp owner conformance report với từng case Pass/Fail/Pending, defects/debt, CI/review/merge evidence, cleanup và qualification label.

Remote topology yêu cầu **API internal authority + ControlWorker + PostgreSQL/durable disks đều ngoài Mac**. Mac chỉ client. Fresh installation với synthetic identities/resources/templates, internal RPC riêng; không mở browser login trên remote bằng cách bỏ localhost/CSRF guards. Bootstrap user/session/delegation fixture bằng offline owner manifest cho qualification; remote user-login/migration existing Mac installation là gate riêng. Không sao chép signing/session/Keychain secrets cũ.

Mac-close test: trước khi đóng, admit một workflow có timer≥180s; đóng client và cho Mac sleep≥120s trong window due đã được approved. Worker/API/DB remote tiếp tục, service-side receipts/AUD/versions chứng minh timer/step commit sau client disconnect. Mac mở lại đọc current scoped state. Không dùng log Mac hoặc VM chạy cùng Mac làm oracle. Operator cần cho phép sleep nếu ảnh hưởng session khác, không tự stop Website/VM hiện hữu.

API-off test là separate negative case: stop riêng issuer đã được cho phép, worker giữ custody/committed queues và chặn new business transition; restore issuer rồi reauthorize/resume đúng original spec/deadline. Không gọi trạng thái bị chặn đó là “deploy chạy khi mọi authority đã tắt”. Khi muốn business progress lúc đóng Mac, issuer phải nằm ngoài Mac và đang available.

Không có host ngoài Mac thì hoàn tất local cases, giữ remote qualification Pending và toàn T12 chưa qualified. Không tự provision cloud/Proxmox/Lima, không chuyển yêu cầu thành giả lập để đạt số liệu đẹp. Remote topology/credentials/firewall/backup budgets chỉ biết sau khi owner chọn host; đây là operation input, không bỏ trống thuật toán/state/authority contract.

## 9. Review, approval và handoff

Đề nghị chủ dự án đọc và duyệt ADR-0021/0022/0023 cùng hai contracts, plan này, conformance và runbook **đúng scope T12.2–T12.4**. Có thể duyệt design toàn bộ rồi triển khai từng feature-scoped PR. Không cần gọi Sol High lặp lại tại mỗi validate/accept; chỉ quay lại Architect khi phát hiện quyết định mới hoặc contract không đáp ứng evidence.

Sau acceptance: Luna Max hoặc model được owner cấp ngoại lệ tiếp tục12.2a→e rồi12.3a→f và12.4 theo dependency. Existing AMR chỉ chuẩn bị task/context, giữ checkpoint/history/private receipts; không replay T12.1, không sửa nguyên nhân failed test thành passed. Targeted tests trong implementation, full applicable gates ở PR boundary; Codex Auto PR Review/BIR và human merge riêng. Sol High kết thúc ở bounded design này, không tạo PR/comment/trigger hoặc triển khai runtime.

## 10. Các rủi ro phải đo trước activation

Global fence có thể gây contention và fail nhiều dưới tải; DB lock/time budgets phải đo với writer-preference tests, không nâng giới hạn để pass. SECURITY DEFINER/functions là attack surface mới: owner/non-login/fixed search_path/signatures/grants và direct SQL negative tests là điều kiện adoption. Native graph/AUD function compatibility chưa được chứng minh. Existing Mac-first installation không tự thành remote-ready. Certificate/DB credential compromise có quy trình revoke/rotate và epoch invalidation, không có arbitrary active-active failover. T13 còn phải chốt effects/health/rollback/provider custody riêng.

## 11. Nội dung phê duyệt cụ thể

Đề nghị xác nhận: “Duyệt ADR-0021/0022/0023 cùng Durable Workflow v1, Worker Authority v1 và kế hoạch T12.2–T12.4 đúng phạm vi thiết kế.” Bao gồm các workflow permission pairs/reauthorization, closed message registrations, global authority fence, mTLS service identity, owner-owned restricted SQL functions và independent-host qualification topology; không chỉ duyệt một bộ tên file. Trước execution, Developer vẫn cần submit exact fixture/host operation manifests cho DB/role/HBA/cert/network/backup/sleep effects. Không có approval nào được ghi tự động bởi câu đề nghị này.

Capacity adoption thuộc12.3a/f: MSG owner adds bounded publication-admission ledger to its own schema/ACL and tests; WFL active-instance cap and operational storage-health gate are normative in Worker Authority v1. Đây là additive proposal, không sửa MSG wire hay đưa scheduler thứ hai vào. CAP01 là mandatory local/remote applicable exit case.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.
