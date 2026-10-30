---
id: ENG-T12-DESIGN-VI
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-30
---

# T12 — Durable transport / workflow: thiết kế và lộ trình áp dụng

## Kết quả cần đạt

Một logical workflow giữ được trạng thái, timer và lịch sử qua lỗi process/ACK; không mất công việc đã commit, không nhân đôi logical transition khi redelivery. API và ControlWorker là hai process với ownership rõ ràng. Workstation đóng không được là nơi duy nhất giữ workflow state.

T12 không phải toàn bộ deployment. T11 đã có release intent và approval; các bản ghi đó chưa bao gồm execution plan đầy đủ. T13 cần quyết định driver, immutable execution plan, quyền effect hiện hành, secret revision, health verification và provider-safe fencing trước deploy/rollback thật. Không tạo executable defaults cho những field chưa được duyệt.

## Baseline và phạm vi

- Main `69c5d86f3bc85a23636a475a8785543f1641df84` đã chứa T11 qua [PR #21](https://github.com/NukeGeng/KORDRA/pull/21); source tree của merge tương đương head đã kiểm chứng `384449c`.
- EF/Identity 10.0.11, Npgsql EF 10.0.3; API dùng khóa installation và admission trong một process. MSG/WFL/ControlWorker chưa có runtime project.
- [ADR-0019](../decisions/0019-durable-transport-workflow-bootstrap.md) đề nghị duyệt **PoC T12.0 trước**; các phần còn lại dưới đây là thiết kế đề xuất, chưa đủ điều kiện chạy production hoặc triển khai business worker.
- Owner đề xuất NukeGeng cho spike; MSG/WFL cần nhận custodian rõ ràng ở adoption gate. Không tự chuyển module status thành implemented/verified.

## Ranh giới thành phần đề xuất

| Thành phần | Sở hữu | Không được suy diễn |
|---|---|---|
| MSG | Envelope validation, routing, durable inbox/outbox, delivery attempts, quarantine/DLQ | ACK hoặc inbox claim không cấp quyền business/external effect |
| WFL | Instance/step/timer, CAS transition, workflow version, recovery evidence | Workflow Completed không đồng nghĩa DEP Verified |
| API | Current user/session + trusted scoped admission khi nhận yêu cầu | UI session không được gửi cho worker, admission gate không trở thành distributed lock |
| ControlWorker | Chạy đúng consumer/workflow đã đăng ký với identity và database rights riêng | Không đọc/sửa trực tiếp IAM/TEN/PRJ/REL/APR owner state để tự authorize |
| DEP/DRV/OBS, giai đoạn sau | Immutable operation, provider effect và independent verification | T11 grant, broker delivery và lease epoch không thay thế execution approval |

Dependency proposal: WFL phụ thuộc neutral MSG Contracts; MSG infrastructure không reference WFL runtime. Host kết nối consumer/owner adapters; không chấp nhận vòng MSG ↔ WFL runtime từ dependency lists cũ đang proposed. AUD/floor tham gia qua port/enlistment, không callback để authorize từ worker.

Mục tiêu giữ EF owner stores; Wolverine/PostgreSQL là transport candidate, không đưa Marten vào để hợp thức hóa messaging. Không tạo module phụ điều phối AMR hoặc queue engine thứ hai.

## Transaction và recovery

Commit boundary của producer gồm business transition + outgoing message + AUD/floor khi use case yêu cầu. Consumer commit inbox dedup + logical transition + outgoing result cùng transaction trước ACK. MSG không sở hữu business tables. Adapter enlistment phải hoạt động với transaction mà owner/Host đã mở, hoặc adoption gate phải trình bày thay đổi thiết kế cần duyệt; không dùng ambient assumption để tuyên bố atomicity.

Dedup gồm cả delivery ID và logical key (installation, tenant, operation, step/version, message kind). Cùng key/khác business hash là conflict/quarantine. Worker restart hoặc đổi delivery ID không mở logical effect mới. Sau ACK loss, đọc durable receipt; không chạy lại từ UI click.

Envelope dự kiến có schemaVersion/messageId/type/installationId/tenantRef/operationId/stepId/version/correlationId/causationId/occurredAtUtc/deadlineUtc/payloadHash. Không có password/token/session reference/kubeconfig, serialized CLR type hoặc arbitrary shell. Tenant/actor trong message là claimed metadata cho đến khi trusted producer/authority boundary được chứng minh. Type registry và payload bounds phải đóng theo consumer, không deserialize tùy ý.

PoC chỉ dùng message type kỹ thuật trong test, tối đa 16 KiB, depth 8; không đăng ký `deployment.dispatch` như command đang được nhận. Config spike đề xuất concurrency 1, tổng 5 delivery attempts, backoff 1/5/15/60 giây, deadline 120 giây; publish số đo thật thay vì gọi các giá trị này là SLO production. Scheduling test dùng clock fixtures và kiểm tra deadline lại khi chạy thật. Quarantine không tự redrive. Giữ inbox/receipts xuyên toàn test; không purge để khiến duplicate case đạt. Retention production phải bao phủ operation/recovery và producer retry horizon, được chốt trước adoption.

## Cross-process authority là gate độc lập

API hiện có fair in-process gate và persisted floor; worker không được tái sử dụng cookie hoặc giả có gate chung. Trước business workflow phải duyệt service identity, bootstrap/rotation/revoke, issuer/consumer allowlist, current tenant/resource facts và linearization giữa authority change với effect admission. Không có một field `authorized=true` trong envelope làm căn cứ.

Đề xuất ban đầu giữ toàn bộ IAM/TEN/PRJ/REL/APR authority writers trong API được khóa độc quyền. ControlWorker chỉ ghi owner WFL/MSG theo scoped command đã admit. Quyền đọc/ghi dùng role riêng và không tin quyền tổng quát của `kordra_app` là worker authority. Đây vẫn là proposal: protocol revalidation qua API hoặc transactional authority store phải được chốt bằng ADR/contract trước business dispatch. API unavailable thì business effect không được admit; worker có thể tiếp tục timer/checkpoint/reconciliation không có external effect. Không hứa deploy vẫn chạy khi API là authority issuer đang tắt.

## Lease, Unknown và cancellation

Lease/epoch là coordination evidence, không tự fence được Kubernetes/Nomad/Helm. Hết lease không đủ cho writer thứ hai nếu writer cũ có thể còn effect. Khi dispatch có thể đã xảy ra nhưng thiếu kết quả: persist Unknown, reconcile journal/provider evidence; không blind retry hoặc tự rollback. Known no-effect và confirmed effect phải được phân biệt bằng evidence hiện hành. Cancellation sau possible effect chỉ ghi intent, không tuyên bố đã undo.

PoC synthetic chỉ chứng minh journal/Unknown control flow; nó không chứng minh provider fencing. Workflow template ở adoption đầu là versioned typed steps; chưa có arbitrary script hoặc visual editor.

## Các lát và điều kiện chuyển tiếp

| Lát | Deliverable | Gate ra |
|---|---|---|
| T12.0 — MSG compatibility/fault PoC | Candidate pins, locked graph; hai OS processes; real PostgreSQL atomic state/outbox/inbox/AUD/floor fixture và failure report | Duyệt ADR-0019 + fixture scope trước code; không tương thích thì báo một lần, không tự đổi stack |
| T12.1 — MSG runtime adoption | Exact schema/migrations/ACL, closed envelope/consumer catalog, retry/retention/DLQ, offline recovery/runbook | Owner duyệt adoption sau evidence T12.0; runtime không DDL và không có permissive consumer |
| T12.2 — WFL persistence | Versioned instance/step/timer/receipt, CAS, Unknown/reconciliation, paired atomic MSG writes | Accepted state/transaction/authority contracts và source/test mapping trước implementation |
| T12.3 — independent ControlWorker | Worker identity/roles/readiness/shutdown, registered consumers, bounded retries và restart | Cross-process authorization/authority writer protocol accepted; không sửa single-process assumptions âm thầm |
| T12.4 — conformance | Crash/ACK/reorder/revoke/epoch/schema/retention/cancellation matrix và actual independent-host evidence | Host riêng được chủ sở hữu cấp quyền; VM cùng Mac không chứng minh Mac-close; hosted CI/Codex review và human merge riêng |

Toàn mốc T12 chỉ hoàn tất khi đạt các lát này. T12.0 đạt không được đổi nhãn toàn mốc thành completed. Nếu chưa có host độc lập, ghi rõ T12.4 pending; không dựng infra hoặc coi simulator là bằng chứng deploy thật.

## Kịch bản chấp nhận bắt buộc

- R12-01: owner/outbox/AUD/floor all-or-nothing bằng transaction thật, reader connection độc lập; thêm hai business contexts vào fixture để lộ integration gap.
- R12-02: kill tại trước commit / sau commit-trước send / receive-trước ACK / handler commit-trước ACK; restart không mất committed work.
- R12-03: duplicate/reorder/message ID khác cùng logical key; một transition, cùng key khác hash bị chặn.
- R12-04: actual COMMIT reply loss; durable lookup, unknown không bị giả no-commit.
- R12-05: wrong tenant/installation/origin/type/version, expired deadline/revoked service identity; quarantine/deny, không bypass bởi retry.
- R12-06: node chết/lease hết/epoch cũ; không cạnh tranh writer đang có possible effect; Unknown cần evidence reconciliation.
- R12-07: runtime DDL denied, không truy cập schema owner khác; migration/rollback policy không xóa immutable history.
- R12-08: delayed timer/cancellation/DLQ, retention boundary, bounded attempts/capacity và no secret output.
- R12-09: unchanged T01–T11, schema compatibility và no execution authority from T11; provider paths không được kích hoạt bởi test types.
- R12-10: worker/DB thực sự nằm ngoài Mac, client/API đóng theo topology đã duyệt; phân biệt timer progress với effect authorization availability.

R12-01–04/07/08 là scope PoC phù hợp; R12-05/06/09/10 cần runtime/protocol/host adoption tương ứng. Chỉ fixture tenant tampering không chứng minh distributed authentication.

## Handoff cho Developer

Sau duyệt T12.0, dùng chat Developer theo model đã được owner cho phép; không mở subagent/phiên AI chỉ để lấy receipt. Tạo task trong AMR hiện có, owner FEAT-MSG-001, test-only project và planned mappings trước thay đổi. Chạy targeted fault cases trước; chỉ chạy Product gates ở validation/PR boundary. Không chạy lại T11 native trust/bootstrap hoặc migration `kordra`. Source/test/fixture receipts phải gắn đúng SHA/epoch; không reset lịch sử T11. PoC report phải chứa pins thật, transaction evidence, failures/limitations và recommendation để duyệt adoption. Sol High kết thúc tại bản thiết kế, không gọi thêm AI review, PR trigger hoặc thực thi GOAL liên tục.


## T12.0 scoped approval recorded — 2026-10-30

NukeGeng duyệt ADR-0019 đúng phạm vi compatibility/fault PoC, candidate pins Wolverine 6.59.0, EF/Npgsql hiện có và test-only UUID fixtures trong kordra_test. Exact proposal hashes/approval receipt được lưu riêng; ADR-0019 được accepted cho scope đó, chưa triển khai và chưa có kết quả compatibility. Toàn bộ T12.1–T12.4, distributed authority, runtime ControlWorker và real-host/provider operations vẫn gated. Case runtime least-privilege chưa thể coi đạt bằng database-owner role; không có quyền tạo role bổ sung. Phiên Sol High hiện tại chỉ ghi nhận approval/handoff, không thực thi Developer task.
