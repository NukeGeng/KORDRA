---
id: ENG-T12-POC-RESULTS
status: implemented
owner: NukeGeng
last_reviewed: 2026-10-30
---

# T12.0 — Kết quả thử nghiệm durability có giới hạn

Phạm vi: FEAT-MSG-001 / MOD-MSG, [ADR-0019](../decisions/0019-durable-transport-workflow-bootstrap.md). Đây là test-only PoC, chưa nhận MSG/WFL/ControlWorker vào runtime. Custodian NukeGeng; actor chỉ là process fixture, tenant/installation chỉ là ID tổng hợp. Không có quyền thao tác provider, Keychain, môi trường công ty hoặc merge.

## Implementation và bằng chứng

Source/test: `tests/Messaging.Durability.Tests/`; solution chỉ thêm project kiểm thử. Wolverine ba package 6.59.0, EF 10.0.11, Npgsql EF 10.0.3, SDK 10.0.400; lock giữ graph. Handler Static được sinh trước bằng command trong feature spec, không thêm runtime compiler. Không thay pin/package của production projects.

| Điều kiện PoC | Bằng chứng hiện có | Giới hạn |
|---|---|---|
| Hai DbContext + AUD/floor + outgoing cùng transaction | Commit/rollback thật, reader độc lập thấy zero trước commit | Fixture tổng hợp, chưa nối owner production |
| Hai OS process | Producer/consumer khởi động bằng executable riêng; marker và PID riêng | Cùng Mac/PostgreSQL; không chứng minh Mac-close |
| Crash producer trước/sau commit | Exit trước commit không có state/envelope; exit sau commit trước flush được recovery | Timeout fixture riêng |
| Crash consumer trước effect/trước commit/sau commit trước ACK | Inbox còn bền vững; rollback hoặc Handled/outgoing theo đúng commit; restart, một logical effect/result | Adapter EF candidate-specific trong test; chưa chấp nhận API nội bộ đó cho runtime |
| Duplicate envelope/logical operation | Envelope ID mới cùng logical key không thêm effect/result | Không phải exactly-once effect trên provider ngoài DB |
| Mất COMMIT reply | Proxy loopback cắt CommandComplete(COMMIT) từ server; client lỗi, lookup mới tìm thấy record đã commit | Không suy mọi lỗi kết nối thành commit thành công |
| Runtime không DDL | Role riêng, AutoCreate.None; startup/handling và catalog relation inventory không đổi; SQL DDL/foreign owner/cross-DB bị deny | Quyền fixture/HBA tạm tách biệt, không đưa vào production |
| Closed schema/type/payload | Serializer 16 KiB/depth 8, reject field lạ/secret tổng hợp, duplicate keys, CLR type sai; unknown serialized type vào DLQ không chạy handler | Chưa có production envelope/service identity |
| Tenant/case/version/deadline | Sai scope/version, hết deadline trước hoặc trong queue: zero effect, DLQ hữu hạn | Claimed metadata trong test, không phải trusted IAM |
| Retry/scheduling | Năm attempts, lịch thật 1/5/15/60 giây, DLQ sau hết budget; kiểm tra timestamp | Không gọi thời gian fixture là SLO |
| Retention | Giữ 10 phút mặc định; case 3 giây kiểm tra duplicate rồi chờ Wolverine tự cleanup, không xóa inbox thủ công | Retention production chưa chốt |

Cấu hình fixture: listener sequential; health/reassignment/scheduler poll 1 giây, stale node 3 giây; deadline 120 giây. Giữ inbox/receipt trong các case recovery. Không auto redrive DLQ. Native queue và envelope persistence đều dùng schema UUID của case; migrations chỉ ở setup bằng owner fixture, consumer dùng AutoCreate.None.

## Sai lệch đã phát hiện và xử lý

DRIFT-T12-001: các lượt đầu chỉ đặt envelope schema, transport ghi vào schema mặc định. Restricted-role test phát hiện SQLSTATE 42501. Đã sửa dùng cấu hình đặt cả hai schema và thêm regression. Chủ repo duyệt riêng cleanup đúng hai bảng queue kỹ thuật; attribution xác nhận message thuộc fixed case. DROP TABLE không CASCADE sau khi không còn process fixture; schema giữ nguyên và zero relations. Role/schema/HBA tạm đều đã cleanup và hash HBA khôi phục. [Drift](architecture-drift.md), [debt](technical-debt.md) giữ lịch sử; receipt private không xuất credential/payload.

Lượt unknown-type đầu chỉ sửa cột message_type chẩn đoán nên không đổi header của envelope thật; test thất bại được giữ. Bản đúng sửa serialized envelope header bằng codec của package và bật UnknownMessageBehavior.DeadLetterQueue, kiểm chứng không chạy handler. Không sửa test để bỏ yêu cầu quarantine.

## Trạng thái nghiệm thu

Các lượt targeted có bằng chứng riêng trong `.amr-local/t12-design/`; `final-targeted-no-retry.log` đạt 29 ca gồm transaction, crash/result, retention, closed payload, unknown type và queued expiry. Retry thật có log riêng và phải được chạy lại trong full gates trên nội dung cuối. Quality gates, hosted CI, Codex Auto PR Review và human merge vẫn là các gate riêng; chưa ghi toàn bộ T12 hoàn tất.

## Gate adoption tiếp theo

T12.1–T12.4 vẫn chưa được duyệt: cần chốt production schema/migration/ACL, API transaction adapter được hỗ trợ, closed envelope/catalog, service identity/tenant authority, retention/recovery horizon, worker authority và vận hành host độc lập. PoC không thay accepted single-process admission/U-C-M-D hoặc T11 executionAuthorized=false. Các kết quả tốt không cấp quyền tự deploy hay tự mở ControlWorker.


## Final local validation — 2026-10-30

Canonical `gates --base 69c5d86f3bc85a23636a475a8785543f1641df84 --product-test-timeout 600 --validation-context <private-nonsecret-epoch>` passed with stable source: docs, scope, 220 tooling tests, Clients pin metadata only, locked restore, Release build, serial Product tests and source mapping. Product result: 1,594 passed, zero failed, one existing T09 live Nomad test skipped; all 34 T12.0 tests passed with zero skips. No existing Keychain item, VM or real provider was accessed to replace the skipped test.

The full PoC implementation is locally validated. Hosted CI, exact PR review evidence and human merge remain separate. T12.1–T12.4/runtime adoption is still unapproved. Earlier partial/failing receipts remain retained, including default-schema escape, initial unknown-type fixture error, missing graph entry, missing synthetic fixture-root environment and the 300-second serial-suite timeout. The corrected final run used the approved isolated synthetic Keychain root, explicit bounded timeout and a rotated fixture epoch after interruption. Existing historical GOAL checkpoints were not rewritten or replayed. A pending bounded CLI plan is not represented as completed native execution for the user-authorized Medium session; actual coordinator/model/source/test receipts are retained separately.
