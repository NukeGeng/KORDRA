---
id: ENG-T12-1-DESIGN-VI
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-30
---

# T12.1 — Thiết kế để chủ dự án phê duyệt

> Accepted ngày 2026-10-30 theo exact scoped acceptance ở cuối file. Chỉ phạm vi T12.1a–f; chưa triển khai hoặc cấp quyền vận hành thật.


## Mục tiêu dễ hiểu

T12.0 đã thử cơ chế bằng chương trình kiểm thử. T12.1 chuyển phần đó thành **thư viện messaging có ranh giới rõ ràng**, có contract, migration artifacts và kiểm thử lỗi đầy đủ để các module dùng sau này. Chưa bật worker triển khai hay cho message quyền thực thi trên cluster.

Tài liệu cần duyệt cùng nhau: [ADR-0020](../decisions/0020-messaging-runtime-adoption.md), [Durable Messaging v1](../contracts/durable-messaging-v1.md) và các lát dưới đây. Cả bộ vẫn proposed; đọc hoặc thiết kế không tự cấp quyền code/adoption.

## Những lựa chọn quan trọng

1. Giữ EF/Npgsql và kiến trúc owner hiện tại. Runtime MSG dùng hai package Wolverine 6.59.0 đã có trong graph PoC; EF integration package chỉ cần ở PoC. Không đổi database engine hay thêm broker.
2. Coordinator hiện có vẫn là người bấm nút commit cuối cùng. MSG chỉ đặt envelope/receipt vào cùng transaction. Không copy hàm commit của PoC vì nó có thể commit trước AUD hoặc giành quyền commit.
3. Chọn public transaction APIs của Wolverine sau wrapper có pin và compatibility tests. Nếu không đáp ứng thì dừng đúng gate, trình vấn đề cụ thể; không tự fallback sang Internals/native SQL hoặc thay owner protocol.
4. Đặt hai schema riêng cho envelopes và queue, runtime không tự tạo/sửa schema. Tài khoản transport tương lai không được quyền business tables; tài khoản thực sự chạy owner mutation sẽ được chốt ở gate worker/authority sau, không giả atomicity bằng hai tài khoản/two commits.
5. Message có type/schema đăng ký trước và logical key/hash bất biến. Gửi trùng không lặp effect; cùng key khác nội dung bị quarantine. Message không phải tờ giấy cho phép deploy và không mang password/token/session.
6. Retention 7 ngày inbox, ít nhất 90 ngày logical receipts, replay horizon 24 giờ; không auto purge receipts. Giới hạn kernel: deadline 120 giây, 5 attempts, 1/5/15/60 giây, payload 16 KiB/depth 8, concurrency 1. Đây là đề xuất cần duyệt, chưa là cấu hình đã chạy production.
7. Kernel không được activate khi chưa có business catalog/service identity/authority/worker design được duyệt. Không thêm một queue engine riêng để giải quyết phần còn thiếu.

## Các lát để Developer thực hiện sau phê duyệt

| Lát | Deliverable và paths dự kiến | Tiêu chí kết thúc |
|---|---|---|
| T12.1a — compatibility adapter | `tests/Messaging.Tests/TransactionAdapterCompatibilityTests.cs`; infrastructure adapter prototype theo contract | Public API exact pin; actual AUD context + owner fixture/floor + MSG state cùng transaction; early send, double commit, cross-connection và Unknown đều được chứng minh. Không đạt thì dừng các lát phụ thuộc |
| T12.1b — Contracts/registry | `src/Modules/Messaging/Contracts/` + `tests/Messaging.Tests/EnvelopeContractTests.cs` | BCL-only, canonical binding, logical key/hash conflict, closed type/content-type/size/depth, tenant/system separation; không fake trusted authority |
| T12.1c — persistence/DDL artifacts | `src/Modules/Messaging/Infrastructure/Persistence/`, `Migrations/`; `tests/Messaging.Tests/MessagingAclTests.cs` | Exact native schema manifest/hash, own receipts, explicit two schemas, least privilege/startup DDL denial, migration review artifacts; chưa chạy migration thật |
| T12.1d — transactional delivery | `src/Modules/Messaging/Infrastructure/Transactions/`, `Delivery/`; `tests/Messaging.Tests/CommitBoundaryTests.cs` | Producer/consumer stage-only adapter, one commit path, atomic effect/receipt/inbox/result, duplicate/hash conflict/reorder, actual reply loss and no blind replay |
| T12.1e — bounded recovery | `src/Modules/Messaging/Infrastructure/Recovery/`; `tests/Messaging.Tests/RecoveryConformanceTests.cs` | Retry/deadline/cancel/timer/retention/quarantine/capacity evidence; offline inspect/plan only, không replay effect |
| T12.1f — integration readiness | solution, architecture tests, owner feature mapping, `docs/operations/messaging-recovery.md` | Full regression/gates, schema/lock/consumer manifest, runbook, Vietnamese limitations report; CI/Codex PR review và human merge độc lập |

T12.1a gồm guarded isolated-test seam tại ba file AUD Infrastructure nêu trong ADR-0020, vì actual AUD hiện dùng schema app. Đây là thay đổi testability được đề nghị duyệt rõ ràng; runtime target/schema/permission không được nới. Không thử trên app schema có sẵn.

T12.1a dùng runtime adapter prototype dưới `src/Modules/Messaging/Infrastructure/Transactions/` nếu cần; không có production registration. T12.1b/c chỉ tiếp tục sau a; d sau b/c; e sau d; f sau tất cả. Các test file/path ở đây là kế hoạch, chưa tồn tại. Developer khai báo exact files trước mỗi slice, cập nhật planned/actual mappings theo file thật. Project names/graph cụ thể được giới hạn bởi ADR-0020; không tạo host/project ngoài bảng đó.

## Quyền thực thi tách khỏi duyệt thiết kế

Approval đề nghị cho code và tests dùng fixture tổng hợp. Trước DB tests phải xác nhận locator đúng target, UUID schema mới, identity và scope đã được owner cho phép. Quyền role/HBA ở T12.0 gắn fixed case không tự chuyển sang case mới. Developer có thể làm pure Contracts/unit tests độc lập trong lúc chờ quyền fixture. Không dùng postgres/superuser làm runtime ACL proof, không đọc secret vì contract chỉ nhắc đến credential locator.

Migration tài liệu/SQL artifact không phải permission thực thi DDL trên installation thật. Với `kordra`, account mới, HBA, trust store, credential hoặc provider phải có operation plan/backup/rollback và approval riêng; không tự khởi động VM hoặc service. T12.1f không cài module vào API hiện tại hay tạo worker thật.

## Những gì chưa được chứng minh và không được gộp vào approval

- PoC kiểm tra synthetic owner/AUD/floor; adapter public API và actual AUD integration mới là acceptance của T12.1a, chưa có kết quả.
- Production authentication/rotation/revocation của service identity, distributed authorization, owner writer fencing và thời điểm effect vẫn cần T12.3; tenant field và DB login không giải quyết các vấn đề đó.
- WFL state machine/Unknown workflow thuộc T12.2; lease không fence được external provider. MAC-close cần host ngoài Mac ở T12.4. Không hứa deploy chạy tiếp khi authority issuer dừng.
- SQL/version upgrade phải rehearsal; public package API không là cam kết upstream ổn định. Consumer raw-secret quarantine cần test native pipeline, không chỉ unit serializer.
- Hard backlog quota, disk sizing và production retention/purge không được suy ra từ watermark/harness. Chưa activation cho deployment, business consumer hoặc public ingress.

## Đề nghị quyết định của NukeGeng

**Duyệt ADR-0020 + Durable Messaging v1 đúng phạm vi runtime kernel T12.1a–f**, gồm graph/package choice, caller-owned commit, schema/ACL design, envelope/hash rules và limits nêu trên; không bao gồm runtime activation, T12.2–T12.4 hoặc quyền thao tác installation/hạ tầng thật. Nếu chưa duyệt thì Developer không bắt đầu adoption. Sol High bàn giao thiết kế tại đây; không chạy thêm AI review, tests sản phẩm hoặc thao tác PR.


## Exact scoped acceptance — 2026-10-30

NukeGeng đã chấp thuận: **“Duyệt đúng phạm vi ADR-0020 và T12.1a–f”**, áp dụng đồng thời ADR-0020, Durable Messaging v1 và bản thiết kế tiếng Việt đã đối chiếu trùng SHA-256 với snapshot trình duyệt. Trạng thái accepted chỉ cho runtime kernel MSG, caller-owned commit/public API compatibility gate, schema/ACL design, envelope/recovery limits và guarded AUD isolated-test seam nêu trong bộ thiết kế. Các đoạn đề nghị/proposed trước annotation là nội dung proposal được giữ để đối chiếu lịch sử; approval này xác lập phạm vi thiết kế được duyệt, không phải kết quả triển khai.

Không bao gồm activation API/business worker, T12.2–T12.4, thay đổi authorization hiện tại hoặc quyền thực thi migration/role/HBA/credential/hạ tầng thật. Fixed-case operational approvals T12.0 không tự chuyển sang fixture mới. Developer bắt đầu bằng T12.1a compatibility gate; không đạt thì báo evidence và giữ các lát phụ thuộc blocked, không fallback sang Internals/native SQL hoặc tự đổi coordinator. Frozen proposal và approval receipt giữ tại `.amr-local/approvals/adr-0020-t12-1/`. Chưa có implementation/test evidence T12.1.
