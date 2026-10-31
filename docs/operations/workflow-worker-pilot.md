---
id: OPS-WORKFLOW-WORKER-PILOT
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# Workflow/ControlWorker pilot — operation proposal runbook

Companion [thiết kế T12.2–T12.4](../engineering/t12-remaining-design.vi.md). Chưa có executable/migration/command được verified. Không copy command giả vào terminal. Design acceptance không cấp quyền vận hành.

## Operation manifest trước bất kỳ effect thật

Owner chọn exact host/installation/database/schema/service/port IDs và resource owner; record rights-owner approval, immutable executable/SQL/ACL/config hashes, current baseline/backup verified restore, step/case IDs, read/write scope, maximum duration/storage/process/connection counts, immutable rollback/cleanup và independent evidence reader. Dùng fixture UUID mới, no company targets. Secret locators private, không secret values trong manifest công khai. Các schema/role/HBA/trust approval T12.0/1 không tự kế thừa.

Fixture proposal phải liệt kê CREATE/DROP exact schemas/roles, temporary HBA blocks nếu thật sự cần, exact CA/cert fingerprints, network listener bind/ports và files. Check service/port conflict chỉ trong Product scope. Không stop container/DB/VM/Website khác. Mọi deviation ngoài allowlist dừng để đối chiếu, không cleanup bằng wildcard/CASCADE blanket hoặc overwrite unknown resources.

## Bootstrap và activate có thứ tự

1. Verify source SHA/graph/schema/template hashes và complete applicable gate/CI/review evidence; verify all writer-fence entries. Chưa đạt thì chỉ sinh artifacts, không chạy migration.
2. Verify protected backup + restore rehearsal và exclusive quiescence/custody; create exact fresh installation/roles/schema/functions bằng owner credentials được cấp riêng. Runtime never DDL. App database có sẵn không dùng như disposable fixture.
3. Provision independent API service identity, worker identity/generation và DB session mapping, current synthetic delegation và allowlisted environment/template. Mount private key/locator files theo OS permissions; validate TLS identities/validity, không disable validation để startup.
4. Start DB/issuer rồi worker đã được phép, one active holder; verify readiness includes epoch/floor/schema/ACL/custody. Listener chỉ internal mTLS, no public wildcard default hoặc browser-cookie exposure. Chạy read-only readiness trước pilot start.
5. Submit one exact admitted checkpoint workflow, independent reader verifies root transaction receipts/AUD/MSG. Sau đó chạy approved crash/restart/cancel/revoke/partition cases; không kill process ngoài recorded PID/host.
6. Remote/Mac-close case chỉ chạy nếu API/worker/DB thật ở ngoài Mac và owner cho phép sleep/affected sessions. Capture service-side evidence, không thay bằng VM cùng Mac.

## Unknown, revoke và rollback

Unknown commit: stop new admissions/consumer; preserve backend/session/custody/receipt IDs privately; inspect exact hashes via independent connection. Không force retry hoặc tự release target custody vì heartbeat timeout. Confirm backend terminal, absence of prepared transaction và exact committed/rolled-back state trước action. Recovery decision cần current workflow.reconcile + exact expectedVersion, immutable evidence hash và independent backend verification; operator-provided assertion không tự đủ.

Service compromise: revoke service generation/epoch qua API/offline guarded owner path, stop exact worker và disable exact DB identity theo approved plan; keep committed messages/state/history. Grant revoke không xoá lịch sử. Credential rotation không in keys/password hoặc token ra logs; không thay trust CA khác.

Upgrade: drain, preserve pending/Unknown, verify backups, expand compatible schema/functions và current manifest; migrate writer sau reader compatibility. Incompatible downgrade refuses startup. Rollback runtime chỉ về version chứng minh compatible; không DROP business/history hoặc reset epoch/floor để “quay về trước”. Restored DB authority epochs/floors phải rebootstrap/invalidate delegations/services có thể stale theo manifest riêng, không coi backup restore là recovery đã authorize.

## Cleanup và report

Stop only recorded fixture processes; verify backend terminal before cleanup. Remove only exact disposable schema/role/temporary trust/HBA/files, reload only approved service and compare pre/post hashes. Unknown ownership/data thì preserve and report; không mở rộng quyền xoá. Real installation retains state/history and requires separate decommission approval.

Report: Feature ID/module, exact SHA/host identities (private as needed), actor/action/scope, operation approval, case matrix and failed attempts, no-secret logs/hash, current counts, cleanup, remaining debt and qualification label. Không ghi independent-host verified nếu remote cases pending, không ghi production-ready hoặc provider deploy verified từ checkpoint workflow.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.
