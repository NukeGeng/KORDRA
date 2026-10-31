---
id: ADR-0020
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-30
---

# ADR-0020 — T12.1: nhận MSG runtime kernel có giới hạn

> Accepted ngày 2026-10-30 theo exact scoped acceptance ở cuối file. Chỉ phạm vi T12.1a–f; chưa triển khai hoặc cấp quyền vận hành thật.


## Quyết định đang đề nghị

Chỉ nhận **thư viện runtime MSG**, schema/migration artifacts và conformance tests theo [contract v1](../contracts/durable-messaging-v1.md). Owner/custodian đề xuất: NukeGeng, FEAT-MSG-001 / MOD-MSG. Chưa được duyệt; không áp dụng quyền PoC của ADR-0019 sang database thật.

Baseline thiết kế: PR #22 head `954c9e94825ee074a54582c0eed88752e38910f3`, ba CI checks thành công, Codex Auto Review tự chạy một lượt và không có finding. Main baseline của PR là `69c5d86f3bc85a23636a475a8785543f1641df84`; đây không phải khẳng định PR đã merge. [Kết quả T12.0](../engineering/t12-poc-results.vi.md) chứng minh fixture, không chứng minh authority của business worker.

## Các lựa chọn và lựa chọn đề xuất

| Phương án | Quyết định đề xuất |
|---|---|
| Copy nguyên handler PoC và EfCoreEnvelopeTransaction.CommitAsync | Không chọn: lớp nằm trong namespace Internals và tự commit; xung đột với quyền commit hiện có của coordinator/AUD |
| Adapter MSG dùng API công khai của persistence trên transaction caller sở hữu | Chọn, với compatibility gate bắt buộc trước phần còn lại; không mặc định public đồng nghĩa ổn định giữa các phiên bản |
| Đưa Marten/broker mới vào hoặc thay owner persistence | Không chọn: không cần cho kernel, mở rộng adoption ngoài evidence |
| Tự viết queue/retry scheduler | Không chọn: tiếp tục dùng Wolverine; chỉ bổ sung receipt/hash và validation do Kordra sở hữu |

## Package và project boundary

- Runtime Infrastructure nhận `WolverineFx` và `WolverineFx.Postgresql` **6.59.0**. `WolverineFx.EntityFrameworkCore` 6.59.0 giữ ở PoC; không cần đưa vào runtime khi adapter dùng transaction API công khai. Giữ EF/Identity 10.0.11, Npgsql EF 10.0.3 và SDK 10.0.400 hiện có. Không tự đổi transitive pins ngoài graph đã đối chiếu; nếu cần package/version khác thì báo delta để quyết định.
- `src/Modules/Messaging/Contracts/Kordra.Modules.Messaging.Contracts.csproj`: BCL-only, không chứa EF/Npgsql/Wolverine, connection string hoặc dữ liệu authority có thể chuyển nhượng.
- `src/Modules/Messaging/Infrastructure/Kordra.Modules.Messaging.Infrastructure.csproj`: references MSG Contracts và package transport; không references IAM/TEN/PRJ/INV/REL/APR/AUD/WFL runtime hay Kordra.Api.
- Host/coordinator ở mốc tích hợp sau kết nối owner ports và infrastructure factory; owner Domain/Contracts không phụ thuộc MSG Infrastructure. Không thêm một project Shared/BuildingBlocks chung để gom business logic.
- `tests/Messaging.Tests/` kiểm tra kernel/adapter. PoC hiện có được giữ như compatibility evidence. Solution/architecture tests được cập nhật cho đúng graph này; không chấp thuận toàn bộ ADR-0009 hoặc refactor source đang chạy.

## Transaction và quyền commit

Adapter cung cấp stage-only enlistment trên **cùng DbConnection và DbTransaction** caller đã mở. API công khai được chọn ở pin hiện tại: `IMessageDatabase.StoreOutgoingAsync`, `StoreIncomingAsync`, `MarkIncomingEnvelopeAsHandledInTransactionAsync` và `MessageBus.EnlistInOutboxAsync`. Stage không commit, không flush/send, không đổi envelope thành Handled trong bộ nhớ trước durable commit. Không dùng SQL tự viết vào bảng native Wolverine, reflection hoặc lớp EF Internals làm fallback.

Coordinator có sẵn giữ duy nhất một commit-capable path: nếu AUD tham gia thì dùng transaction context của AUD hiện có; owner state/floor, AUD và MSG writes đã enlist trước đó cùng commit. MSG không gọi AUD AppendAsync riêng để giả atomicity. Sau xác nhận Durable mới đánh dấu Handled trong memory và cho phép flush; flush lỗi sau commit chỉ là delivery pending. Unknown commit phải reconcile bằng logical key/hash qua connection mới; không chạy lại owner mutation. Fail trước commit-capable handoff là known setup failure; không tự đầu độc admission gate hiện có.

**T12.1a là compatibility gate**: phải chứng minh cả producer và consumer bằng adapter mới, cùng transaction với actual AUD context trong fixture được phép, xác nhận inbox identity đầy đủ và không early send. PoC cũ dùng EF Internals chưa chứng minh điều này. Nếu API không đáp ứng thì giữ kết quả failed, dừng adoption; không tự đổi coordinator, copy SQL native hoặc mở thêm AI review.

## Quyền và activation

Schema riêng `msg` và `msg_transport`; cùng database với owner transaction, không cross-database pseudo-transaction. Offline migrator sinh manifest DDL exact-version/hash và grants; runtime AutoCreate.None, Static handlers, kiểm tra schema/version và fail readiness khi lệch. Artifact SQL được review trước mọi thao tác thật. Không sửa HBA hay tự tạo tài khoản khi startup.

T12.1 chỉ thêm library và offline migration artifacts, **không đăng ký vào API đang chạy, không tạo ControlWorker executable/service, không có HTTP enqueue/DLQ/replay API**. Consumer kỹ thuật dùng hai fixture processes, không được đăng ký trong production catalog. Business consumer catalog ban đầu rỗng và từ chối type chưa được duyệt. Đây là kernel có thể tích hợp sau, chưa phải messaging service đã kích hoạt.

T12.2 chốt state WFL; T12.3 chốt service identity, distributed authority và worker composition trước activation. Không đổi accepted single-process admission/U-C-M-D, không chuyển T11 intent approval thành execution authority. MSG không authorize bằng tenant/actor field hoặc tên database role.

## Điều kiện phê duyệt và điều kiện thực thi

Đề nghị owner duyệt **ADR-0020 + contract v1 + T12.1a–f** trong [bản thiết kế tiếng Việt](../engineering/t12-1-design.vi.md). Thiết kế cho phép code, build và fixture tổng hợp trong phạm vi cụ thể được xác nhận; không cấp quyền migrate `kordra`, tạo login/HBA, đọc credential/Keychain hoặc kết nối hạ tầng thật. Fixture dùng locator đã được cấp riêng; thiếu quyền thì làm phần độc lập và báo đúng gate, không bỏ test hoặc dùng superuser làm bằng chứng runtime.

Tiêu chí hoàn tất T12.1: kernel/adapter và migration artifacts theo contract; failure matrix có evidence hiện hành; regression T01–T11 đạt; hosted CI/Codex PR Review và human merge tách biệt. Không đổi nhãn toàn T12 thành completed. Chưa có implementation evidence cho ADR này.

## API source đã đối chiếu

Đã đọc nguồn đúng commit package `0313f3f49ed34d203f077f65210efe3adfa36079`: [IMessageDatabase](https://github.com/JasperFx/wolverine/blob/0313f3f49ed34d203f077f65210efe3adfa36079/src/Persistence/Wolverine.RDBMS/IMessageDatabase.cs), [IEnvelopeTransaction](https://github.com/JasperFx/wolverine/blob/0313f3f49ed34d203f077f65210efe3adfa36079/src/Wolverine/Persistence/Durability/IEnvelopeTransaction.cs). Public API presence là bằng chứng thiết kế, không thay executable compatibility gate.


## AUD fixture seam cần được duyệt cùng quyết định

AUD hiện hard-code schema app và factory exact-target. Vì vậy chứng minh actual AUD bằng UUID schema cần một seam test-target rõ ràng, không giả đã hỗ trợ: cho phép bổ sung guarded isolated-test factory/model-schema parameter trong `AuditDbContext.cs`, `AuditDbContextFactory.cs`, `AuditTransactionContext.cs` thuộc AUD Infrastructure. Runtime/offline production paths giữ mặc định app và toàn bộ target guards; seam chỉ chấp nhận database kordra_test, identity fixture đã được cấp riêng và schema UUID thuộc manifest case, model cache key phải chứa schema. Không đọc/ghi app.aud tables để thử và không nới ValidateRuntimeTarget. MSG Infrastructure không reference AUD; test project được reference cả hai Infrastructure để kiểm chứng coordinator. Owner AUD đề xuất NukeGeng đồng phê duyệt đúng test seam này. Nếu không duyệt seam thì T12.1a cần phương án fixture khác được chốt, không thay bằng synthetic audit rồi gọi actual-AUD proof.


## Exact scoped acceptance — 2026-10-30

NukeGeng đã chấp thuận: **“Duyệt đúng phạm vi ADR-0020 và T12.1a–f”**, áp dụng đồng thời ADR-0020, Durable Messaging v1 và bản thiết kế tiếng Việt đã đối chiếu trùng SHA-256 với snapshot trình duyệt. Trạng thái accepted chỉ cho runtime kernel MSG, caller-owned commit/public API compatibility gate, schema/ACL design, envelope/recovery limits và guarded AUD isolated-test seam nêu trong bộ thiết kế. Các đoạn đề nghị/proposed trước annotation là nội dung proposal được giữ để đối chiếu lịch sử; approval này xác lập phạm vi thiết kế được duyệt, không phải kết quả triển khai.

Không bao gồm activation API/business worker, T12.2–T12.4, thay đổi authorization hiện tại hoặc quyền thực thi migration/role/HBA/credential/hạ tầng thật. Fixed-case operational approvals T12.0 không tự chuyển sang fixture mới. Developer bắt đầu bằng T12.1a compatibility gate; không đạt thì báo evidence và giữ các lát phụ thuộc blocked, không fallback sang Internals/native SQL hoặc tự đổi coordinator. Frozen proposal và approval receipt giữ tại `.amr-local/approvals/adr-0020-t12-1/`. Chưa có implementation/test evidence T12.1.
