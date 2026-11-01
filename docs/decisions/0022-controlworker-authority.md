---
id: ADR-0022
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# T12.3 — independent worker, current authority và owner-scoped database ports

## Bối cảnh và baseline

PR #23 đã human-merge vào main `15c764917e030ff256ebed5191c9771a41a58755` (2026-10-31). Reviewed head `7589662a1dd6aec0d7e4175caa11703739cb52fe`: ba CI checks đạt, native Codex vòng2 không finding. Đây là evidence MSG kernel, chưa có WFL/ControlWorker runtime. Không reset/replay checkpoint hay xem acceptance thiết kế như quyền vận hành.

## Quyết định đề nghị và phương án so sánh

Chọn sole API authority writers, installation-wide persisted fence, service-bound short-lived permit và independent ControlWorker với một active holder/installation. Phải thay đổi tất cả invalidating writer paths một cách có kiểm kê/race evidence trước activation; không chỉ thêm một token hoặc cache bool authorized. API unavailable chặn new business transitions, không đảm bảo offline execution.

Chọn new IAM-owned authority schema/functions, WFL/AUD owner-owned narrow transaction functions dùng SECURITY DEFINER có fixed search_path/qualified SQL, authenticated DB identity và exact grants, internal private mTLS listener, private service credential files và remote installation profile riêng. Đây là boundary/security adoption mới cần exact approval, không được suy từ ADR-0012 hoặc ADR-0020. Cấm fallback raw owner-table privileges khi ACL compatibility gate fail.

Không chọn: cookie forwarding; cached long-lived JWT; lease-only authority; worker đọc/sửa trực tiếp IAM/TEN/PRJ; RPC permit không fence lúc commit; gọi RPC khi đang giữ DB transaction; hai login/hai commit giả atomicity. Không chọn active-active issuer/worker ở pilot. Global fence trade-off giảm throughput để giữ một ordering chứng minh được.

Liveness/heartbeat không chứng minh effect đã dừng. Restart giữ Unknown/custody; T13 mới được chốt provider fencing và execution delegation. Không thay accepted local U/D limits hoặc đưa Agent vào Control Plane persistence.

Gate T12.3a trước host adoption: actual owner functions/roles/AUD transaction + SQL tenant isolation/fence races; sau đó complete writer coverage, service TLS bootstrap/rotation/revoke, host readiness/shutdown và two-process conformance. Chưa cấp quyền tạo DB role/HBA/trust/network listener hoặc remote host.

## Contract, phạm vi approval và hậu quả

Contract normative đề nghị: [worker-authority-v1.md](../contracts/worker-authority-v1.md); [thiết kế tiếng Việt](../engineering/t12-remaining-design.vi.md), [test plan](../engineering/t12-conformance-plan.md), [runbook đề xuất](../operations/workflow-worker-pilot.md). Không sửa accepted ADR-0012/0020 trong lần thiết kế này. Nếu được duyệt, chỉ bổ sung scope cụ thể ở đây; source/ACL/host thật phải có evidence riêng.

Owner/custodian đề xuất NukeGeng cho WFL/IAM/AUD/Host và architecture/security. Thiết kế chưa accepted. Human design approval, deterministic gates, PR Code Review, human merge và sensitive operations là các gate độc lập.

## Capacity adoption bổ sung

Nhận thêm MSG-owned logical publication reservation ledger cùng root/fence, bounded active workflows và fail-closed fresh storage-health admission theo Worker Authority v1. Không tự claim logical quota là hard physical disk bound. Actual storage/WAL/backup capacity phải được measured/configured trong target operation plan; thiếu monitoring hoặc sizing thì không activate.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.
