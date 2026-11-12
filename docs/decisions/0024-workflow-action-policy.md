---
id: ADR-0024
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-06
---

# Resource policy cho các thao tác workflow của T12.2e

## Vấn đề cần quyết định

[ADR-0021](0021-durable-workflow-persistence.md) đã chốt bốn quyền ở environment scope và yêu cầu kiểm tra policy hiện hành. [ADR-0014](0014-resource-policy-v1.md) và [Resource Policy v1](../contracts/resource-policy-v1.md) giới hạn policy ở `target.read`. IAM hiện kiểm tra grant workflow và Host có chỗ nối policy cho luồng đọc. Còn thiếu quy tắc xác định policy nào áp dụng cho từng thao tác. Không có policy hợp lệ thì giữ deny.

## Phương án đề xuất

Mở rộng Resource Policy bằng phiên bản payload 2 cho bốn action đã duyệt: `workflow.start`, `workflow.read`, `workflow.cancel` và `workflow.reconcile`. Mỗi policy chỉ chứa một action và chỉ áp dụng ở environment scope. Giữ các trường validity, status và case/effect của v1. Chỉ nhận hai case Development và Test. Một case cần allow rõ ràng, deny cùng case thắng allow. Thiếu case, revision hết hạn, classification ngoài pilot hoặc dữ liệu không nhất quán đều deny. Không thêm cú pháp biểu thức hoặc kế thừa.

Dùng đúng ownership hiện có. IAM giữ immutable revision, head, payload/hash và evaluator. PRJ giữ binding và history theo khóa tenant/project/environment/action. Host lấy tenant từ owner chain và actor từ phiên hiện hành. Host yêu cầu cả grant hiện hành lẫn policy allow cho cùng action/scope. Policy dùng exact current head đã được binding pin. Revision cũ, sai action và đổi classification đều invalidate kết quả trước đó. Identity của policy v2 giữ action cố định suốt vòng đời. Tách bằng chứng thời gian quan sát khỏi revision/hash ổn định để lần đọc mới có thể rút ngắn hạn hiệu lực mà không gia hạn invocation cũ.

Giữ policy payload v1, canonical bytes và các binding `target.read` hiện có. Thêm constraint/validation cho v2 theo hướng additive và chỉ publish qua manifest offline đã duyệt. Không backfill, tự cấp grant hay tạo policy mặc định. Chưa đăng ký HTTP workflow hoặc worker. Trong T12.3, publication, disable và rebind phải nằm trong inventory invalidating writers và tham gia fence theo [ADR-0022](0022-controlworker-authority.md) trước activation.

### Kiểm tra theo thao tác

| Action | Điều kiện bổ sung ngoài policy và grant |
| --- | --- |
| `workflow.start` | Cùng phiên có `release.read`, exact frozen release ID/revision/hash và approval còn hiệu lực. Delegation có hạn không vượt workflow deadline hoặc hạn sớm nhất của các quyền và approval liên quan. |
| `workflow.read` | Mỗi page đọc theo owner scope và ghi audit U/D trước khi trả đúng payload. |
| `workflow.cancel` | Ghi sticky intent và audit dưới version CAS. Kiểm tra lại authority trước commit và disclosure. |
| `workflow.reconcile` | Custody đã quiesce, bằng chứng owner bất biến được xác minh, version hiện hành được so trong transaction. Hai loại quyết định đã duyệt giữ nguyên. |

Snapshot cho U, mutation và D chứa action, tenant, project, environment, classification, grant revision, policy ID/revision/hash, binding ID/revision và mốc hết hạn. Mỗi lần kiểm tra lại dùng owner facts mới. Rebind hay publish phải tăng revision để change-and-back không hợp thức hóa bằng chứng cũ. Có nghi vấn COMMIT thì giữ Unknown và đi theo reconciliation hiện có.

## Phương án đã cân nhắc

Dùng policy `target.read` cho workflow làm mất phân biệt giữa đọc target và thực hiện thao tác workflow, đồng thời cho phép một binding tác động sang action khác. Một policy workflow gom bốn action giảm số binding nhưng thêm precedence và làm tăng phạm vi thay đổi mỗi lần publish. Phương án trên dùng lại một binding cho mỗi action, đổi lại owner cần khai báo từng action có chủ đích.

## Điều kiện nghiệm thu

- Golden v1 giữ nguyên. Golden v2 và kiểm thử parsing từ chối schema/action/scope/case lạ.
- Fixture có tenant khác, sai action, thiếu grant hoặc binding, deny thắng allow, head/binding stale, rebind, expiry và classification đổi giữa hai lần đọc.
- DB fixture chứng minh binding unique theo action, publication/binding/AUD atomic, upgrade đọc được dữ liệu v1 và chưa có default grants.
- Kiểm thử owner API chứng minh không lộ payload khi U/D, CAS hoặc audit chưa đủ. Gate fence/worker và phê duyệt thao tác hạ tầng tiếp tục theo ADR hiện có.

## Phạm vi duyệt

Chỉ xin quyết định về policy action cho T12.2e. Approval thiết kế không cấp quyền áp dụng migration, dùng credential, chạy fixture, bật endpoint hoặc merge. Lịch WaitUntil mặc định, start idempotency và persistence của quyết định reconcile tiếp tục thuộc lát triển khai tương ứng.

## Phê duyệt đúng phạm vi — 2026-11-07

NukeGeng đã duyệt bằng câu “tôi duyệt tiếp tục làm đi”. Nội dung trước annotation đã được đối chiếu đúng hash bản đề xuất đã trình. Phê duyệt áp dụng cho thiết kế policy của ADR này. Thao tác DB và activation tiếp tục theo các gate vận hành hiện có.
