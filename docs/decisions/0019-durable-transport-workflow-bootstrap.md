---
id: ADR-0019
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-30
---

# ADR-0019 — T12: kiểm chứng durability trước khi nhận distributed runtime

## Trạng thái và phạm vi quyết định

**Accepted ngày 2026-10-30, chỉ cho T12.0 PoC theo quyết định của NukeGeng.** [Thiết kế T12 tiếng Việt](../engineering/t12-design.vi.md) mô tả toàn mốc; quyết định được đề nghị duyệt ngay chỉ là **T12.0: PoC có giới hạn**, owner đề xuất NukeGeng, thuộc FEAT-MSG-001. Phê duyệt PoC không tự phê duyệt T12.1–T12.4, ADR-0001/0002 toàn bộ, distributed authorization hoặc deployment.

Baseline đã đối chiếu: main `69c5d86f3bc85a23636a475a8785543f1641df84`, PR #21 đã merge; T11 head `384449c8f07998add18f32078ceaf34e59e8cafc` có ba CI checks đạt và Codex follow-up không còn finding. T11 approval vẫn `executionAuthorized=false`. ControlWorker, MSG và WFL hiện chỉ có README; chưa có durable runtime.

## Phương án

| Phương án | Đánh giá |
|---|---|
| Wolverine + PostgreSQL + EF Core hiện có | Chọn để kiểm chứng: phù hợp hướng ADR-0002, giữ ownership/persistence đã accepted; cần chứng minh enlistment transaction thật |
| Đồng thời đưa Marten vào và chuyển owner storage | Chưa chọn: thay đổi persistence của nhiều owner, không cần cho bài toán transport hiện tại |
| Tự viết queue/retry/recovery engine | Chưa chọn: phát sinh một infrastructure framework phải bảo trì và chứng minh lại |
| RabbitMQ ngay từ đầu | Chưa chọn: thêm broker và vận hành khi chưa có evidence cần isolation/throughput đó |

## Candidate pins và giới hạn

PoC đề xuất `WolverineFx`, `WolverineFx.EntityFrameworkCore`, `WolverineFx.Postgresql` **6.59.0**, đã đọc version index/nuspec chính thức ngày 2026-10-30. Đây là candidate pins cho spike, chưa phải dependencies được cài hoặc runtime compatibility đã chứng minh. Metadata EF integration khai báo EF Core 10.0.4 cho net10; repository dùng EF/Identity 10.0.11, Npgsql EF 10.0.3 và SDK 10.0.400. Spike phải giữ các pin đã accepted, khóa toàn bộ graph, kiểm tra transitive constraints/advisories. Không tự downgrade EF hoặc thử vòng lặp nhiều phiên bản; không tương thích thì báo kết quả để quyết định tiếp.

Được đề nghị cho phép sau duyệt: tạo test-only project `tests/Messaging.Durability.Tests/`, fixture support riêng trong project đó, cập nhật solution/lock/owner mapping tương ứng; chạy child processes do chính fixture tạo. Chỉ dùng đúng loopback `kordra_test`/`kordra_test`, schema UUID mới của case, dữ liệu tổng hợp và credential locator riêng tư hiện hữu. Không đụng schema/case có sẵn; cleanup chỉ schema/process mới do case sở hữu và được đánh dấu disposable. Không tạo role, sửa `kordra`, sửa API/ControlWorker production composition, đọc Keychain, kết nối provider, khởi động VM hoặc đăng ký broker.

## Điều kiện chấp nhận PoC

1. Cùng Npgsql connection/transaction: owner fixture state + outgoing envelope + audit fixture + persisted floor cùng commit/rollback. Không chứng minh bằng hai commits liên tiếp hoặc chỉ bằng mock return value.
2. Tối thiểu producer/consumer là hai OS processes độc lập, có immutable case/installation IDs và connection scope. Kill chỉ PID fixture; không dùng process name để dừng dịch vụ khác.
3. Crash trước commit, sau commit trước send, sau durable receive trước ACK, sau handler commit trước ACK đều recover được. Duplicate delivery tạo một logical transition/output; message ID khác nhưng cùng logical key vẫn bị dedup.
4. Thử mất COMMIT reply thật ở client-stream boundary; lookup evidence qua connection mới. Không suy unknown thành no-commit rồi replay.
5. Startup không tự DDL; thử chạy với DDL denied sau khi fixture migrator tạo schema. Runtime rights tối thiểu và owner tables nằm ngoài scope phải bị từ chối. Không sử dụng superuser để chứng minh runtime ACL.
6. Schema/type không hỗ trợ, tenant/case không khớp và payload secret bị reject/quarantine hữu hạn. Không có endpoint/handler production nhận business command từ test.
7. Retry/deadline, inbox retention, DLQ và scheduling được đo/ghi với exact config, package graph, migrations và fault outcomes. Envelope hết deadline không được chạy handler effect.
8. Report mọi giới hạn khi multi-DbContext/outbox/ACK recovery không đáp ứng. Test fixture tích cực không chứng minh service identity, deployment authorization, provider fencing hoặc Mac-close.

## Gate tiếp theo và compatibility

Sau PoC, bổ sung exact transaction adapter, schema migration/ACL, worker role/identity, message envelope/retention và cross-process security contract để chủ repo duyệt adoption T12.1–T12.4. Nếu cần thay đổi invariants single-process đã accepted, phải có quyết định supersede rõ phạm vi trước khi sửa owner writers. Giữ nguyên T01–T11, local locks/U/C/M/D và native recovery. Không đổi T11 grant thành quyền execution.

Đây là lần thiết kế bounded; không tự gọi thêm Sol review. Developer báo evidence PoC rồi dừng ở gate adoption, không biến từng bài test thành một vòng Architect review.

## Nguồn kiểm chứng

- [Wolverine EF transactional middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware): một transactional DbContext cho một handler chain; không bảo đảm mặc nhiên shared owner transaction của Kordra.
- [Wolverine PostgreSQL integration](https://wolverinefx.net/guide/durability/postgresql.html): persistence/transport candidate, không phải authorization/side-effect proof.
- [NuGet EF integration candidate](https://www.nuget.org/packages/WolverineFx.EntityFrameworkCore/6.59.0), [PostgreSQL candidate](https://www.nuget.org/packages/WolverineFx.Postgresql/6.59.0). Metadata có thể tra từ NuGet v3 flat-container; không chạy mã package trong phiên thiết kế.


## Exact scoped acceptance — 2026-10-30

NukeGeng quyết định: “Duyệt T12.0 đúng phạm vi ADR-0019”. Approval áp dụng candidate Wolverine 6.59.0, giữ pin EF/Npgsql hiện hữu, test-only project và hai process fixture do case sở hữu, schema UUID mới disposable trong đúng kordra_test; các giới hạn ở trên giữ nguyên. Frozen pre-annotation ADR SHA-256 `be00842bf7baa54eea61fd9ae00b110aebdd0aa4a2be6c442191438e09eed7b5`; thiết kế tiếng Việt SHA-256 `f8cbed6dadd81b70253bcf0af200012ca4a28a577385f8ef7cf88c6431f672de`. Original bytes và approval receipt giữ riêng trong `.amr-local/approvals/adr-0019-t12-poc/`.

Đây là duyệt phạm vi thử nghiệm, không phải kết quả PoC, compatibility, runtime adoption, worker authority hoặc production readiness. ADR-0001/0002 tổng thể và T12.1–T12.4 vẫn chưa được duyệt.

### Acceptance constraint cần giữ nguyên

Case runtime DDL/owner-isolation chưa được chứng minh. `kordra_test` là owner database thử nghiệm; chạy cả migrator và worker bằng role đó không chứng minh least privilege hay DDL denial. Approval này không cho tạo role hoặc mở quyền sang database khác. Developer có thể làm package/transaction/recovery cases trong scope được duyệt, nhưng phải ghi case ACL là chưa đạt nếu chưa có boundary thật; không giả chứng minh bằng mock/read-only transaction, không hạ tiêu chí và không tự cấp quyền mới. Cần quyết định môi trường/quyền riêng nếu muốn thêm fixture runtime identity để khép case này. Không gọi toàn PoC đạt chỉ vì những case khác xanh.


## Separate operational exceptions — 2026-10-30

After accepting the original PoC, NukeGeng explicitly authorized one fixed disposable runtime role/schema and a four-rule HBA block scoped solely to that role, with reload and exact cleanup. These separate operational approvals do not expand runtime adoption or change the original approval record. The isolated PostgreSQL ACL probe passed and its role/schema/HBA block were removed; Wolverine restricted-runtime startup is still outstanding. The original no-role restriction above describes the initial acceptance scope, superseded only for this separately authorized fixture operation.
