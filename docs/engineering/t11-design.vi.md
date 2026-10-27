---
id: ENG-T11-DESIGN-VI
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
---

# T11 — Thiết kế Release và phê duyệt: bản tiếng Việt để chủ dự án xem xét

**Cập nhật 2026-10-27: bạn đã duyệt thiết kế; T11 đang được triển khai từng phần.** Bản dịch bên dưới giữ nguyên ngữ cảnh khi đề xuất và không chứng minh tính năng đã hoàn tất.

Phần A dịch đầy đủ nội dung quyết định [ADR-0005](../decisions/0005-immutable-release.md). Phần B diễn giải các quy tắc chính trong [Release & Approval v1](../contracts/release-approval-v1.md) để bạn hiểu hành vi và giới hạn trước khi quyết định. Phần B không phải bản dịch từng dòng của toàn bộ đặc tả HTTP/JSON; tên trường, quyền và mã trạng thái được giữ nguyên để lập trình viên đối chiếu chính xác. Bản này không bổ sung quyết định kiến trúc, không tự chấp thuận ADR và không thay thế đặc tả kỹ thuật gốc.

Nguồn được đối chiếu theo nội dung đã khóa ngày 2026-10-27:

| Tài liệu gốc | SHA-256 |
|---|---|
| ADR-0005 | `b10884383a99d71e0449a04f4c36966ce5768f64cfe4539b7011f2497279b95f` |
| Release & Approval v1 | `76efea31a146bd64cf9d97d71cd1d3fc32dac02ba2e6017446f8012043d9d04a` |

Nếu tài liệu gốc thay đổi, phải đối chiếu lại bản dịch; không dùng bản dịch cũ để suy ra đã duyệt nội dung mới. Bộ đề xuất gốc vẫn giữ nguyên nội dung/hash.

## Đọc trước: T11 mang lại điều gì?

T11 xây dựng phần **chuẩn bị bản phát hành và ghi nhận quyết định phê duyệt**:

1. Chọn ứng dụng, môi trường, đích đã đăng ký và image/artifact bất biến của T10.
2. Xem lại chính xác những gì máy chủ sẽ đưa vào bản phát hành.
3. Đóng băng nội dung đó, tạo mã băm để phát hiện thay đổi.
4. Đưa vào hàng chờ xin duyệt.
5. Người có quyền duyệt, từ chối hoặc thu hồi; hệ thống giữ lịch sử và tính hiệu lực hiện tại.

**Duyệt tại T11 chưa làm ứng dụng được deploy.** Kế hoạch deploy còn cần driver, cấu hình chạy, tham chiếu secret, kiểm tra sức khỏe và quyền thực thi. Các phần đó phải được thiết kế, khóa nội dung và phê duyệt riêng tại các mốc sau. T11 luôn công bố `executionAuthorized=false`, nghĩa là “chưa được cấp quyền thực thi”.

## Phần A — Bản dịch quyết định ADR-0005

### 1. Trạng thái và mục đích

Đây là đề xuất quyết định T11 cụ thể, đang chờ maintainer phê duyệt. Nó mở rộng bản phác thảo đóng băng đặc tả ngày 2026-10-15 vốn chưa được chấp thuận, giữ mục tiêu ngăn tráo artifact hoặc nội dung đã duyệt. Không thay thế ADR đã được chấp thuận hay hành vi đã triển khai. Quy tắc kỹ thuật nằm trong Release & Approval v1; tài liệu release/execution v1 vẫn là đề xuất cho worker và triển khai ở giai đoạn sau.

### 2. Hiện trạng và vấn đề cần giải quyết

Nhánh main tại `bc91c2a66abcdb2156363f5fdbd3dc7f89046e67` đã có [PR #19](https://github.com/NukeGeng/KORDRA/pull/19) do chủ dự án merge. Phiên bản được review là `5969786f910081fa4df2c0ce808f5e8c551b7b88`, đã đạt ba CI checks docs-integrity/artifact-domain/clients-pin và một lượt Codex Auto Review không có finding. Đó là bằng chứng cho source T10 đã kiểm tra, không phải bằng chứng T11 hay khả năng hoạt động với registry thật. Thư mục REL/APR hiện chỉ có README giữ chỗ. Danh mục quyền đang chạy chưa có các thao tác release; Resource Policy v1 chỉ áp dụng cho target.read.

Đề xuất liên module cũ trộn việc duyệt release với driver, cấu hình, kiểm tra sức khỏe và quyền thực thi tương lai chưa được chấp thuận. Nếu đóng băng các trường còn thiếu bằng giá trị mặc định hợp lệ, về sau có thể thêm hoặc thay đổi chúng mà quyết định ban đầu không bao quát. ART hiện chứng minh được hash manifest/config trong phạm vi giới hạn; chưa xác minh layer, chữ ký và khai báo nguồn gốc thủ công. T11 phải tạo được hàng chờ release/phê duyệt hữu ích, đồng thời phản ánh trung thực cả hai giới hạn này.

### 3. Các phương án đã cân nhắc

| Phương án | Đánh giá |
|---|---|
| Duyệt tag hoặc cấu hình còn sửa được, sau đó mới xác định nội dung thật | Không chọn: nội dung có thể thay đổi sau khi duyệt. |
| Gọi quyết định T11 là phê duyệt deploy nhưng để trống các trường thực thi tương lai | Không chọn: các trường được thêm sau có thể nằm ngoài quyết định đã duyệt. |
| Chấp thuận ngay toàn bộ worker/provider/health contract | Để T12/T13: chưa cần cho hàng chờ duyệt bền vững và sẽ mở rộng kiến trúc chưa được chấp thuận. |
| Hoãn toàn bộ release/phê duyệt đến khi có registry/driver thật | Không chọn cho pilot: cản trở việc kiểm thử phần ý định phát hành và phê duyệt theo đúng chủ sở hữu. |
| Khóa một đối tượng release-intent-v1 có trường dữ liệu xác định, tách khỏi quyền thực thi và duyệt thực thi riêng về sau | Phương án đề xuất được chọn; đây là giới hạn sản phẩm được công bố rõ, không tuyên bố duyệt là deploy. |
| Thêm event store, dịch vụ workflow hoặc policy engine mới | Chưa áp dụng: tái sử dụng nền tảng local đã kiểm chứng và đúng các dependency hiện có. |

### 4. Mười một quyết định đang xin bạn duyệt

1. **Chỉ chấp thuận T11.0–T11.3:** FEAT-APR-002 xây nền policy duyệt bằng công cụ offline; FEAT-REL-001 tạo nháp/đóng băng/ngừng sử dụng release; FEAT-APR-001 xây hàng chờ và quyết định phê duyệt. Đề xuất NukeGeng phụ trách các lát công việc này và các điểm kết nối liên quan; không thay đổi quyền sở hữu module ngoài phạm vi đó.

2. **Khóa nội dung do máy chủ xác định:** tenant, project, ứng dụng, môi trường, target đã đăng ký, dữ liệu ART bất biến, đúng phiên bản policy/liên kết APR và mức đủ điều kiện pilot. Lưu chính xác các byte UTF-8 được chuẩn hóa theo quy tắc sắp xếp khóa, kèm SHA-256 có dấu phân biệt loại nội dung. Không dùng tag còn thay đổi, quyền/hash do client tự khai, bằng chứng nguồn gốc mượn từ bản đăng ký khác hoặc một túi cấu hình chưa được xem xét.

3. **Đối tượng được duyệt là release-intent-v1:** dùng các quyền release.* cụ thể trong contract. Quyết định T11 có executionAuthorized=false và không đáp ứng deployment.approve. Driver thực thi, định danh hệ thống từ xa, cấu hình, secret và health phải thuộc kế hoạch có phiên bản riêng, được duyệt mới theo contract được chấp thuận sau này. T11 không thêm worker, driver, message hay dispatch.

4. **Điều kiện ban đầu chỉ dành cho pilot Development/Test:** sử dụng bằng chứng hash manifest/config của T10 và nguồn gốc chưa được chứng thực, chỉ khi policy cho phép rõ ràng. Có thể tạo nháp Staging/Production để lập kế hoạch; việc đóng băng/phê duyệt phải chờ mức xác minh được chấp thuận sau. Không gọi đó là xác minh chữ ký, xác minh khả năng pull hay quyền vận hành production.

5. **APR sở hữu policy nghiệp vụ có phiên bản riêng:** mỗi môi trường được liên kết chính xác với policy. Policy chỉ hạn chế quyền IAM/TEN đã cấp, không tự cấp quyền. Giữ nguyên Resource Policy v1 target.read và các liên kết đọc PRJ hiện tại. Công bố policy khi API đã dừng, có duyệt hash, audit và chống xử lý trùng; không tạo policy cho phép mặc định hay tự cấp bổ sung.

6. **Khuyến nghị tách người yêu cầu và người duyệt:** thời gian chờ tối đa đề xuất là 86.400 giây (24 giờ), thời hạn quyết định duyệt là 1.800 giây (30 phút), nhưng phải khai báo rõ tất cả giá trị. self_allowed là lựa chọn riêng chỉ cho Development/Test; cùng một người vẫn phải có đủ quyền yêu cầu và quyền duyệt độc lập. Nó không vượt quy trình phê duyệt production của tổ chức. Hiệu lực duyệt bị giới hạn bởi thời hạn chờ gốc, quyền được cấp, policy và thời điểm bắt đầu khung giờ cấm kế tiếp. V1 chưa có duyệt theo số đông, lịch lặp hoặc cơ chế bỏ qua khẩn cấp.

7. **Vòng đời release là Draft → Frozen → Retired:** kiểm tra đúng phiên bản trước khi sửa để chống ghi đè đồng thời. Byte của bản Frozen không bao giờ được viết lại. Mỗi phiên bản release cụ thể chỉ có tối đa một yêu cầu phê duyệt trong suốt lịch sử; sau từ chối, thu hồi, hết hạn hoặc mất hiệu lực phải tạo phiên bản đã xem xét mới để yêu cầu tiếp. Phiên bản mới không tự chuyển hướng hay kế thừa quyết định cũ. Muốn loại bỏ bản lỗi thời phải retire hoặc thu hồi rõ ràng.

8. **Quyết định duyệt là sự kiện chỉ ghi thêm, không sửa/xóa:** trạng thái đã ghi và hiệu lực hiện tại được tách biệt. Hết hạn hoặc thay đổi chủ sở hữu, role, membership, quyền, policy hay retire có thể làm mất hiệu lực; không giả vờ đã có scheduler ghi quyết định mới. Session của người yêu cầu/người duyệt chỉ cần cho thao tác HTTP thực tế, không phải token bền vững cho worker. Đăng xuất/khởi động lại không xóa quyết định; vẫn phải kiểm tra quyền lưu trong hệ thống hiện tại và thời hạn gốc.

9. **Tái sử dụng cơ chế nhất quán một process:** admission gate, giới hạn tài nguyên dùng chung, cổng đọc của module sở hữu, đồng hồ tin cậy/mốc thời gian tối thiểu, AUD cùng transaction và bước trả kết quả gắn đúng người gọi. Thay đổi dữ liệu, biên nhận, quyết định, mốc thời gian và audit phải commit một lần trên cùng kết nối. Lỗi trước commit không được làm host mất khả năng phục vụ; chỉ kết quả commit thực sự chưa rõ mới chặn admission, sau đó đối chiếu đúng biên nhận khi API dừng. Recovery không tự chạy lại, phục hồi head cũ hay tạo quyết định duyệt mới.

10. **Chỉ bổ sung lưu trữ cho REL/APR bằng công nghệ đã khóa:** EF Core 10.0.11 và Npgsql EF 10.0.3; Domain thuần, Contracts chỉ dùng thư viện nền .NET. Migration thêm bảng app.rel_* / app.apr_*, kiểm tra đúng locator runtime/admin/test và giới hạn quyền SQL tối thiểu. Không tự tạo schema lúc startup, không thêm package/phiên bản, Marten, Wolverine, dịch vụ mới, frontend hoặc refactor cây source đã chốt. Host kết nối các Contracts trung lập của module sở hữu; REL/APR không tham chiếu runtime vòng nhau và không mượn quyền target.read.

11. **Triển khai feature hoàn chỉnh theo thứ tự:** policy APR → Release REL → quyết định APR. Mỗi feature có source/tests/docs/gates, commit/PR theo feature, Codex Auto PR Review mặc định và con người quyết định merge. Không lặp Sol review GOAL/source. Duyệt thiết kế không tự chạy schema, cấp policy hay thao tác target, secret, production.

### 5. Migration, tương thích và khôi phục phiên bản

Contract quy định bảng dữ liệu, locator, HTTP/ETag/lỗi, lưu giữ lịch sử và thứ tự triển khai. Kiểm thử migration chỉ bổ sung, ràng buộc SQL/tenant, dữ liệu module cũ không rỗng, lỗi một phần và rollback không phá lịch sử trên database kordra_test cách ly. API T01–T10, đường dẫn source, hash ART và hành vi credential hiện tại phải tương thích.

Khi cần lùi tính năng, tắt các route mới; giữ nguyên nội dung bất biến, quyết định/policy/audit, head và mốc đồng hồ chỉ tiến. Duyệt thiết kế không cấp phép chạy DDL hay thay đổi quyền trên bản cài đặt thật.

Các tham chiếu module và bản phác thảo release/execution cũ đã được cập nhật để trỏ đến đề xuất cụ thể này, không được tự coi là đã chấp thuận. Những quyết định deployment/request/override/message/worker tương lai vẫn là proposed. Không lấp chỗ thiếu của thực thi bằng adapter luôn cho phép hoặc quyền kế thừa từ T11.

### 6. Điều kiện chấp thuận và giới hạn còn lại

Trước khi code, maintainer cần chấp thuận ADR-0005, Release & Approval v1 và ba feature đúng T11.0–T11.3, bao gồm: tách ý định release khỏi thực thi, mức pilot, quyền có phạm vi cụ thể, tách nhiệm vụ/thời hạn/khung giờ và lựa chọn lưu trữ/admission. Trạng thái vẫn proposed cho đến khi quyết định được ghi nhận theo hash tài liệu đã khóa.

Để hoàn thành implementation phải đạt R11-01–16: vector byte/hash độc lập; chủ sở hữu/tenant/quyền hiện tại; preview/kiểm tra phiên bản; tách nhiệm vụ/hết hạn/thu hồi; cạnh tranh quyết định/retire; mất xác nhận commit thật; recovery khi API dừng; HTTPS thật; migration/quyền SQL cách ly; tương thích T01–T10; kiểm tra xác định và review trên PR. T11 chưa chạy các kiểm thử này.

Giới hạn còn lại: tin cậy maintainer local/DBA/hệ điều hành; một API process; dữ liệu đã bắt đầu trả không thể thu hồi hoàn toàn; bằng chứng registry là lịch sử; chưa xác minh nguồn gốc/layer/chữ ký; chưa có kế hoạch thực thi/dispatch; không đảm bảo thu hồi quyền phân tán. Đổi mô hình chạy hoặc dùng hạ tầng thật cần thiết kế và quyền vận hành riêng.

## Phần B — Diễn giải hành vi để bạn dễ quyết định

### 1. Một ví dụ xuyên suốt

Bạn muốn chuẩn bị phát hành frontend phiên bản A vào môi trường Test:

1. T10 đã lưu image theo digest cụ thể, ví dụ sha256 của đúng nội dung; tên tag latest không được xem là nội dung bất biến.
2. Bạn chọn ứng dụng frontend, project, môi trường Test, target đã đăng ký và artifact đó.
3. Máy chủ đọc thông tin hiện tại từ các module sở hữu, rồi trả preview. Bạn xem preview này trước khi freeze.
4. Nếu target, artifact hoặc policy đổi giữa lúc xem và freeze, máy chủ từ chối đóng băng bằng preview cũ. Bạn phải xem selection mới.
5. Khi freeze thành công, server lưu chính xác nội dung và hash. Bạn không sửa thẳng bản đó được nữa.
6. Bạn yêu cầu duyệt bản Frozen. Người có đúng quyền xem và duyệt xem đầy đủ nội dung cố định rồi quyết định.
7. Sau khi duyệt, hệ thống vẫn kiểm tra thời hạn, quyền và các ràng buộc hiện tại khi hiển thị hiệu lực. Bị thu hồi quyền hoặc policy đổi thì quyết định không còn đủ điều kiện sử dụng.
8. Chưa có lệnh deploy gửi đến Kubernetes/Nomad. Phần thực thi ở mốc sau phải đưa ra kế hoạch cụ thể và được duyệt riêng.

Đây là kịch bản thiết kế, chưa phải thao tác có thể chạy trên T11 hiện tại; frontend cũng chưa được triển khai bởi phạm vi Product này.

### 2. Ai được thao tác gì?

| Quyền kỹ thuật | Phạm vi | Nghĩa sử dụng |
|---|---|---|
| release.create | Project | Tạo nháp; chỉ người tạo nháp có quyền hiện tại mới được sửa/đóng băng nháp đó. |
| release.read | Project | Xem lịch sử và chi tiết release trong project được cấp. |
| release.retire | Project | Ngừng sử dụng bản Frozen, có lý do; không phục hồi lại bản Retired. |
| release.request-approval | Environment | Yêu cầu duyệt bản Frozen đúng môi trường; đồng thời cần release.read của project. |
| release.approval.read | Environment | Xem hàng chờ và đầy đủ nội dung Frozen cần duyệt cho môi trường đó; không có quyền đọc mọi artifact/project. |
| release.approve | Environment | Duyệt hoặc từ chối; cần thêm quyền xem approval; khi duyệt còn kiểm tra policy, người yêu cầu, thời hạn và mức đủ điều kiện. |
| release.approval.revoke | Environment | Thu hồi yêu cầu đang chờ hoặc đã được duyệt; vẫn cần quyền xem approval. |

Tên role như Admin không tự tạo các quyền này. Client không được tự khai tenant hay actor để trở thành người có quyền. Việc thêm tên quyền vào danh mục cũng không tự cấp quyền cho bất kỳ tài khoản nào.

### 3. Policy khác permission như thế nào?

Permission trả lời “người này có quyền làm thao tác này trên tài nguyên này không?”. Policy bổ sung điều kiện nghiệp vụ: môi trường nào được phép, có cần hai người khác nhau không, thời hạn bao lâu và giờ nào không được duyệt.

Có permission mà policy từ chối thì vẫn không được duyệt. Có policy cho phép mà thiếu permission cũng không được duyệt. Thiếu policy không tự rơi về cấu hình cho phép.

APR có policy riêng; không sửa policy target.read của T06. Mỗi phiên bản policy là bất biến, mỗi môi trường gắn với đúng phiên bản. Khi đổi policy, các approval phụ thuộc có thể mất hiệu lực; không tự nâng chúng sang policy mới.

Quản trị policy ở T11 dùng công cụ khi API đã dừng, kiểm tra đúng bản cài đặt/database và duyệt hash manifest cụ thể. Không có endpoint HTTP chỉnh policy, không tự tạo policy lúc bật ứng dụng. Tên lệnh đề xuất approval-policy inspect/apply chưa phải lệnh đã có implementation.

### 4. Tự duyệt có được không? Thời hạn là bao lâu?

| Quy tắc | Nội dung đề xuất |
|---|---|
| independent | Người yêu cầu và người duyệt phải có principal ID khác nhau. Đây là khuyến nghị, không phải giá trị tự điền khi thiếu cấu hình. |
| self_allowed | Cùng một người được yêu cầu và duyệt chỉ khi policy khai báo rõ, môi trường Development/Test và người đó có đầy đủ các quyền riêng. |
| Thời gian chờ | Cho phép cấu hình 60–86.400 giây; đề xuất 24 giờ. |
| Thời hạn sau khi duyệt | Cho phép cấu hình 60–86.400 giây; đề xuất 30 phút, nhưng không vượt hạn chờ gốc và các hạn quyền/policy khác. |
| blockedWindowsUtc | Tối đa 16 khoảng UTC cố định mỗi rule; trong khoảng đó không được grant. Có thể gửi yêu cầu vào hàng chờ. |

Ví dụ minh họa: policy đặt thời hạn duyệt 30 phút nhưng quyền của approver chỉ còn 10 phút, thì quyết định không thể có hiệu lực quá 10 phút đó. Nếu trước đó đã đến hạn chờ gốc hoặc khung giờ cấm bắt đầu, hiệu lực còn kết thúc sớm hơn. Đăng nhập lại/gia hạn quyền không kéo dài thời hạn gốc. Qua hết khung giờ cấm không làm quyết định đã hết hạn sống lại.

Pilot hiện chỉ đủ điều kiện freeze/request/grant tại Development/Test và phải cho phép nguồn gốc chưa chứng thực một cách rõ ràng. Staging/Production chỉ lập nháp; chỉ đặt rule allow không đủ mở khóa việc duyệt ở hai môi trường đó.

### 5. Lịch sử quyết định và hiệu lực hiện tại khác nhau

| Trạng thái | Ý nghĩa |
|---|---|
| Draft | Release còn nháp, được sửa theo kiểm tra đúng phiên bản. |
| Frozen | Nội dung release đã khóa; không được sửa byte/hash tại chỗ. |
| Retired | Bản release đã ngừng sử dụng, không được hồi sinh. |
| Pending | Yêu cầu duyệt đang chờ. |
| Granted | Hệ thống đã ghi nhận quyết định duyệt; cần xem hiệu lực hiện tại. |
| Rejected | Đã từ chối. |
| Revoked | Đã thu hồi. |
| Expired | Hiệu lực đã hết thời hạn gốc. |
| Invalidated | Không còn đủ điều kiện vì quyền/chủ thể/liên kết/policy/nội dung sở hữu liên quan đã thay đổi. |

Ví dụ: lịch sử vẫn ghi “A đã duyệt lúc 10:00”, nhưng lúc 10:05 quyền cần thiết của A bị thu hồi thì không được hiển thị quyết định đó như một quyền hiện hành vẫn hợp lệ. Hệ thống không sửa lịch sử thành “A chưa từng duyệt”. Nếu không đọc được dữ liệu để xác minh hiệu lực, trả lỗi tạm không khả dụng, không đoán Granted.

Mỗi revision chỉ có một yêu cầu duyệt trong toàn bộ lịch sử. Sau từ chối/thu hồi/hết hạn/mất hiệu lực, muốn yêu cầu lại phải tạo revision mới và xem xét lại; có thể giữ cùng nội dung nhưng không mượn quyết định cũ. Mỗi dòng release chỉ có tối đa một Draft. Tạo revision mới không tự hủy revision Frozen cũ: muốn loại bỏ bản cũ phải retire/thu hồi rõ ràng.

### 6. Đóng băng những gì, xử lý lỗi thế nào?

Nội dung Frozen giữ đúng tenant; ID/phiên bản project, ứng dụng, môi trường; phân loại môi trường; target và phiên bản liên kết; dữ liệu artifact T10 cố định; policy/liên kết và phiên bản; mức pilot/nguồn gốc chưa chứng thực/chưa có quyền thực thi. Không chứa mật khẩu, token, cấu hình tùy ý, lệnh shell hay trường driver/health chưa được thiết kế.

Server tính hash từ byte chuẩn hóa, không tin hash client làm sự thật. selectionHash do client gửi lại chỉ để so với preview server đã tính. Nhãn hiển thị không nằm trong hash nội dung Frozen; đổi thông tin có ý nghĩa trong nội dung phải làm hash thay đổi. Dữ liệu lưu bị sai hash là lỗi toàn vẹn phải chặn và điều tra, không tự ghi lại cho khớp.

Gửi trùng đúng khóa và nội dung phải đối chiếu kết quả đã lưu, không tạo quyết định lần hai. Hai người cùng grant/reject chỉ một người thắng trên đúng phiên bản Pending. Duyệt thành công rồi một thao tác revoke hợp lệ đến sau vẫn có thể thu hồi quyết định đó.

Thay đổi owner, biên nhận, audit và mốc thời gian phải commit chung. Mất kết nối trước khi thử commit khác với mất xác nhận sau khi đã thử commit: trường hợp sau phải giữ trạng thái chưa rõ và đối chiếu khi API dừng, không đoán thất bại rồi tự chạy lại. Nếu thay đổi đã commit nhưng người gọi mất quyền trước khi nhận response, thay đổi vẫn tồn tại; hệ thống giữ lại response nhạy cảm và cho phép đối chiếu bằng chứng theo đúng quyền.

### 7. Thứ tự Developer triển khai và tiêu chí hoàn thành

| Lát công việc | Kết quả cần giao |
|---|---|
| T11.0 — APR-002 | Policy có phiên bản, lưu trữ, công cụ offline, liên kết môi trường và kiểm thử; hoàn thành feature PR policy trước. |
| T11.1a–b — REL-001 | Domain/Contracts/store/migration release, hash/cạnh tranh phiên bản, HTTPS tạo/xem/freeze/retire và recovery; hoàn thành feature PR Release. |
| T11.2a–b — APR-001 | Yêu cầu/quyết định/hiệu lực, HTTPS hàng chờ/chi tiết/duyệt/từ chối/thu hồi và recovery; hoàn thành feature PR Approval. |
| T11.3 | Đối chiếu đủ kiểm thử liên module, tương thích, restart/recovery, dữ liệu HTTP bàn giao client, gates/CI/Codex PR Review; con người quyết định merge. |

R11-01–16 bao gồm: hash và thay nội dung; owner/tenant; permission/deny; Draft/CAS/retire; preview đổi; policy; một request/revision; tách nhiệm vụ/tự duyệt đúng giới hạn; hết hạn/khung giờ/đồng hồ; đổi rồi khôi phục quyền không hồi sinh approval; cạnh tranh/SQL; atomic audit/mất xác nhận commit; HTTPS và giới hạn trả dữ liệu; recovery dừng API; migration/dependency/tương thích; hiển thị hiệu lực trung thực và bằng chứng kiểm tra/review/merge tách biệt.

Các bảng này là kế hoạch, không phải bằng chứng đã có code hay test đạt. Developer chạy targeted tests trong lúc code, chạy gates bắt buộc ở ranh giới feature/PR; Codex Auto PR Review là reviewer mặc định. Không tự gọi thêm Sol review mốc hoặc review GOAL.

### 8. Những phần chưa được gói thiết kế này cấp phép

Triển khai thật vào Kubernetes/Nomad; tạo VM/worker/cluster; chuyển pod/image giữa worker; HPA/HA/VPA; cấu hình runtime/secret/health để chạy ứng dụng; worker bền vững, broker/outbox hay quyền phân tán; truy cập credential/registry/cluster thật; chạy migration hoặc đổi quyền database thật; frontend/Website và tự động merge PR. Những phần đó cần mốc thiết kế hoặc phạm vi vận hành riêng.

### 9. Bạn đang duyệt điều gì?

Bạn duyệt phạm vi thiết kế và cho Developer triển khai ba feature theo T11.0–T11.3, bao gồm giới hạn release-intent, pilot Development/Test, policy/SoD/thời hạn, quyền có phạm vi cụ thể và transaction/lưu trữ một process. Bạn chưa duyệt deploy hoặc thao tác hạ tầng thật, và chưa xác nhận code/test T11 đã hoàn thành.

Nếu đồng ý đúng nội dung này, có thể xác nhận:

> Duyệt ADR-0005 và Release & Approval v1, cùng APR-002 / REL-001 / APR-001 đúng phạm vi T11.0–T11.3, với các giới hạn đã giải thích trong bản tiếng Việt.

Việc cần chủ dự án chấp thuận trước code xuất phát từ AGENTS.md: không thay đổi state machine, permission model hay technology/boundary khi chưa có ADR được duyệt. Bản dịch này chỉ giúp bạn đọc và quyết định; không tự ghi nhận câu trên thành sự chấp thuận.

## Ghi nhận phê duyệt sau khi dịch — 2026-10-27

NukeGeng đã trả lời “ok duyệt, thực hiện T11 với terra xhigh”, chấp thuận đúng thiết kế và phạm vi đã nêu ở trên. Hash bản gốc trước khi ghi trạng thái accepted vẫn như bảng đầu tài liệu; quyết định được ghi tại ADR-0005. Các đoạn “chờ duyệt” trong bản dịch phản ánh thời điểm trước quyết định. T11 đang được triển khai, chưa hoàn thành.
