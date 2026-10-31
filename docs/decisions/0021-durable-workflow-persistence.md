---
id: ADR-0021
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# T12.2 — persisted workflow, CAS và recovery

## Bối cảnh và baseline

PR #23 đã human-merge vào main `15c764917e030ff256ebed5191c9771a41a58755` (2026-10-31). Reviewed head `7589662a1dd6aec0d7e4175caa11703739cb52fe`: ba CI checks đạt, native Codex vòng2 không finding. Đây là evidence MSG kernel, chưa có WFL/ControlWorker runtime. Không reset/replay checkpoint hay xem acceptance thiết kế như quyền vận hành.

## Quyết định đề nghị và phương án so sánh

Chọn owner WFL với relational persistence EF/Npgsql và closed versioned templates. Không dùng native message inbox như workflow state, không tự viết queue engine, không thêm Marten hoặc durable engine thứ hai. Cần state/version/timer/Unknown tường minh, paired atomic MSG/AUD và business transition độc lập transport retries.

Nhận new workflow.start/read/cancel/reconcile environment pairs, current delegation và pilot.checkpoint.v1 chỉ Development/Test; không deployment authority. Proposed projects Domain, Contracts, Infrastructure và tests trong reservation WFL hiện tại. Chưa tạo source.

Thay thế phương án task state trong memory (mất khi restart), dùng delivery ID làm logical identity (duplicate effects), hoặc cho terminal Failed thay Unknown (blind replay). Chi phí: relational CAS/schema compatibility, bounded native retry khác workflow scheduler budget, không tự purge hoặc force-resume.

Gate T12.2a–e: contract/golden vectors; actual WFL+AUD+MSG root transaction; timer horizon/deadline/cancellation; fault/reconciliation; exact owner API disclosure. AUD reference trong dòng này chỉ nói owner qua port, không runtime dependency Domain. Phần distributed authority ở ADR-0022 phải đạt trước positive worker activation.

## Contract, phạm vi approval và hậu quả

Contract normative đề nghị: [durable-workflow-v1.md](../contracts/durable-workflow-v1.md); [thiết kế tiếng Việt](../engineering/t12-remaining-design.vi.md), [test plan](../engineering/t12-conformance-plan.md), [runbook đề xuất](../operations/workflow-worker-pilot.md). Không sửa accepted ADR-0012/0020 trong lần thiết kế này. Nếu được duyệt, chỉ bổ sung scope cụ thể ở đây; source/ACL/host thật phải có evidence riêng.

Owner/custodian đề xuất NukeGeng cho WFL/IAM/AUD/Host và architecture/security. Thiết kế chưa accepted. Human design approval, deterministic gates, PR Code Review, human merge và sensitive operations là các gate độc lập.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.
