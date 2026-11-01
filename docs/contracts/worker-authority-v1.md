---
id: CONTRACT-WORKER-AUTHORITY-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# Worker identity, authority và host protocol v1 — T12.3

Thiết kế chờ duyệt [ADR-0022](../decisions/0022-controlworker-authority.md). Owner IAM + WFL + AUD; custodian đề xuất NukeGeng cho các owner/architecture/security. Không mở rộng accepted single-process protocol trước approval và conformance. Không dùng cookie, role label, claimed tenant hoặc message delivery làm quyền.

## Lựa chọn và invariant

API tiếp tục là sole writer IAM/TEN/PRJ/INV/REL/APR/SEC authority qua owner handlers. Worker là WFL coordinator riêng, có DB transaction WFL/AUD/MSG thật. Chọn persisted installation-wide authority fence + short-lived exact permits, thay vì cached JWT/offline capability hoặc gọi RPC rồi tin một boolean permit đã cũ. Không giữ transaction qua RPC/network I/O. API unavailable chặn admission business transition mới; timer inspection, custody heartbeat và exact reconciliation metadata vẫn có thể hoạt động, không được mint authority.

Global fence làm giảm concurrency nhưng cho một ordering rõ ràng ở pilot. Sharding thành tenant/resource fence là quyết định khác sau khi có evidence, không tối ưu âm thầm. API in-process writer preference và U/D handoff hiện có giữ nguyên; không đổi nó thành distributed lock.

## Service identity và internal listener

Mỗi installation/worker có serviceId, generation, active status, allowed tenant/environment/template/action bindings, cert SPKI/SHA256 pins, validity và revoke revision trong IAM-owned schema mới `iam_workflow`. Database login ánh xạ một service generation, không phải generic superuser. Bootstrap/rotate/revoke là offline owner manifest hash + AUD; không online enrollment, không nhận serviceId do body tự khai. Không có quyền tự mở rộng binding hoặc rotate key từ worker.

API thêm internal HTTPS listener địa chỉ cụ thể trong operation manifest, không wildcard mặc định; chỉ mTLS, exact installation identity + private CA chain/SAN/EKU/pin/validity và IAM current generation. Worker xác thực server tương ứng, không skip TLS, ambient proxy hay online AIA/OCSP retrieval. Fail-closed nếu không có explicit issuer/revocation custody; API rechecks service registry ở mỗi request, không cache grace. Rotation có hai pins tối đa10 phút, retirement/revoke tăng epoch và chặn generation cũ. PostgreSQL dùng verify-full TLS khi remote, cùng expected server identity/CA trong manifest. Local loopback fixtures có profile riêng đã khóa; không tự áp dụng insecure transport lên remote.

Runtime key/DB locator qua explicit private files ngoài repo, OS service account riêng, directory0700/file0600, không command-line args/environment dump/log. Không dùng macOS Keychain của Mac cho Linux host, không copy installation/cert/credentials hiện hữu tự động. Remote qualification dùng installation mới riêng được provision theo operation approval. T12 worker chưa có provider credential resolver; không đọc Keychain hay secret K8s/Nomad/registry.

## Delegation, permits và wire

Human start dưới trusted session + workflow.start/current resource admission tạo IAM workflow delegation: original actor, exact installation/tenant/resource/release/template/specHash, allowed transition class, validUntil≤workflowDeadline và earliest grant/approval expiry. Delegation là persisted server reference, không transferable bearer secret. Browser close không tự revoke; explicit logout/revoke, principal/session security invalidation hoặc grant/owner changes đều là invalidating writers. Nếu logout policy không chứng minh cập nhật epoch/invalidates delegation thì activation bị chặn. Không sống qua boot/session invalidation bằng cách đổi reference. Offline fixture delegation chỉ cho synthetic identities đã được approve, không seed quyền thật.

Internal RPC schemaVersion1: POST `/internal/v1/workflow-admissions` với service-authenticated instanceId/operationId/expectedVersion/specHash/transitionKind/requestId. API đọc owner facts qua Contracts và complete policy, current delegation/approval/resource state. Request≤16KiB, depth8, request deadline2s, response≤8KiB, no redirects/retry fallback; max16 in flight, bounded queue16. Result là permit reference hoặc reasonCode deny/unavailable. Không trả cookie/token/secret; caller không được tự supply actor/grant/epoch. Readiness endpoint chỉ local/service-authenticated minimal status.

Permit lưu IAM-owned: permitId, serviceId/generation, installation/tenant/resource chain, delegationId, operation/logical key, instance/step expectedVersion, spec/template/release hashes, authorityEpoch, issuedAt, expiresAt, disposition. TTL≤2s và earliest underlying expiry; một logical operation chỉ một successful consumed result, changed hash conflict. Expired/revoked permit không được renew như quyền cũ: issuer re-evaluates toàn bộ cho attempt mới, old permit audit retained. Wire chỉ trả permitId/hash/expiry; đây là locator, không là proof tự đủ.

## Linearization: revoke không đua với worker commit

IAM owns one `authority_fence` row/installation (epoch BIGINT checked overflow, utcFloor, manifest revision). All invalidating writers, including offline CLI, phải acquire API writer gate khi online rồi lock fence FOR UPDATE trên root connection, **trước đọc authorization facts**; mutate owner state + epoch++ + AUD/floor cùng root. Offline mutation yêu cầu API/worker quiesced custody, vẫn lock/increment. Direct SQL/out-of-band authority edits không thuộc supported path; detection/hash/restore drift đóng readiness, không hứa detect tức thì mọi DBA edit.

Coverage bắt buộc: login/session/boot/security/lockout/logout, role definition/policy head, tenant/membership/assignment/grantor, lifecycle/ownership/classification/target binding, REL/APR decisions/expiry/withdrawal, SEC revision/revoke, service bootstrap/rotation/revoke và installation restoration. Denial audit thuần append không đổi authority thì không increment. Thiếu một writer entry + race test là blocker activation, không coi registry generic là proof.

Permit issuance giữ existing API gate + fence, resolve current facts, insert permit/AUD rồi commit. Worker nhận response ngoài DB transaction; sau đó mở own root, lock fence cùng installation, đọc permit/service/delegation qua IAM-owned port; exact epoch/service/operation/hash/expiry phải match. API revoke đã commit trước → epoch khác, reject. Worker đang giữ fence trước → writer chờ; worker rechecks DB trusted time/expiry ở final commit handoff, consumes permit + WFL CAS + AUD/floor + MSG cùng root. Lock giữ tới confirmed root outcome, timeout không được tự giải phóng custody khi commit Unknown. Grant expiry trong thời gian transaction chỉ được admit tại final checked handoff; residual commit acknowledgement delay là Unknown/reconcile, không permission grace.

DB lock acquire≤1s, each transition root budget≤2s bằng monotonic timer, SQL/lock timeouts trong budget còn lại; statement timeout riêng không đủ thay transaction deadline. Release API gate theo existing unknown-commit protocol. Worker không cầm DB fence trong khi gọi API. Required ordering gate→fence→owner rows; worker fence→WFL rows, tuyệt đối không quay lại gọi API khi đang giữ fence. API-only positive protected reads giữ U/D gate; worker không mutate authority facts để bypass read ordering. Issuer/worker share a nondecreasing persisted authority UTC floor; server time behind floor>1s closes readiness, không tự lấy max để che clock fault.

## Database rights và owner ports

Migration tạo exact closed functions/schema/ACL manifest, không runtime provisioning. `kordra_worker_runtime` đề xuất một login/service generation, không owner/superuser/CREATEROLE/CREATEDB/BYPASSRLS/INHERIT; no PUBLIC-derived cross-database access, no wildcard future-table grants. Giữ `kordra_msg_runtime` transport-only profile; không dùng nó cho owner commit.

Worker chỉ DML exact native MSG allowlist đã chứng minh; owner writes qua parameterized owner-owned functions/ports trong cùng root. IAM functions acquire/check fence, validate/consume exact permit and service mapping from authenticated session_user; không tin SET tenant/custom GUC. WFL functions stage CAS/history/timer/custody theo validated permit, binds immutable tenant/installation/operation/hash/version, reject unsupported transitions. AUD append function nhận exact immutable intent/permit binding, append-only, không UPDATE/DELETE; floor owner functions tăng đơn điệu. Không cấp worker raw DML WFL/IAM/AUD hoặc SELECT user/secret/business tables. Read functions trả scoped safe metadata theo fixed service bindings. Tenant protection phải enforce cả SQL surface, không chỉ application callback.

Functions dùng SECURITY DEFINER chỉ trong owner schema với non-login non-superuser function owner, fixed pg_catalog + explicit qualified objects/search_path, REVOKE PUBLIC EXECUTE, exact signature/grants, không dynamic SQL/session-controlled identifiers, immutable manifest hash. Owner-to-owner chỉ gọi public function contract/port, không private table SQL. Enlistment test phải chứng minh AUD function preserves existing canonical hash/idempotency/floor behavior. Đây là **technology/security adoption đề nghị mới**, compatibility/ACL PoC là gate T12.3a; fail thì dừng, không mở quyền raw tables làm fallback. AuditDbContextFactory hiện có không được nới target guard tùy ý. API profile được cấp exact owner function rights cần thiết, vẫn giữ existing owner handlers; worker login không được tự issue permit.

## Host lifecycle và compatibility

ControlWorker executable .NET hiện có reservation; không add service chỉ từ docs. Startup validate installation/schema/lock graph/template/registry/ACL hashes, service identity, TLS, clock floor, exclusive worker lock, stopped unresolved custody. Static handlers/AutoCreate.None, no dynamic codegen/DDL, no default business consumer. Registration chỉ exact pilot checkpoint/timer/cancel consumers sau các gate. API listener off by default tới authorized activation; user API loopback vẫn giữ nguyên profile.

Liveness không đủ readiness. DB/issuer outage: stop new admitted transitions, retain committed envelopes/timers, no drop; recovery inspect metadata/custody bounded, không authorize. SIGTERM: stop polling/new receives, drain≤10s, capture exact operations; Unknown stays stopped. SIGKILL/two-worker partition tests phải pass trước qualification. Restart cần schema/epoch/receipt checks, không clear journal. No auto redrive DLQ hoặc resume terminal work.

No registry/package upgrade; derive exact consolidated worker lock from current EF/Npgsql and Wolverine graph, prove public transaction compatibility before adoption. WFL Domain/Contracts BCL-only; WFL Infrastructure references own Domain/Contracts + MSG Contracts/Infrastructure adapters as needed, not other owners' private stores. Host references owner Contracts/Infrastructure only for composition; no owner Domain→Host/transport dependencies, no MSG→WFL runtime cycle, no Agent→Control Plane persistence.

## Chưa nằm trong quyền T12

Permits chỉ admission cho internal WFL database transition; không authorize Kubernetes/Nomad/Helm effect, deployment.execute/rollback, provider secrets hoặc fencing external API. T13 phải duyệt immutable execution plan và effect-specific current authority, provider idempotency/fencing/observed outcome. Process/lease/epoch alone không đủ. Public browser remote hosting/SSO, distributed active-active authority issuer và new secret provider là decisions riêng.

## Owner function contract details for implementation

Permit guard/consume binds workerBootId/custodyEpoch and actual PostgreSQL full root transaction ID. WFL/AUD functions require that same consumed permit in that same still-live root and exact operation/hash, not merely a historically consumed ID. Repeated calls for the same operation return the original staged/durable receipt; a second transition or another root conflicts. Function definitions, signatures and grants are reviewed artifacts; implementation must prove bypass denial with direct SQL and hostile search_path before activation. Successor custody claim verifies old backend terminal under the shared fence, CAS-increments epoch; every later transition checks epoch. A stale process with an otherwise valid certificate cannot use its older permit/custody epoch.

## Transaction isolation và admission capacity

All authority/transition roots use READ COMMITTED with fresh owner reads after acquiring the fence; no stale tracked EF entity/snapshot cache may serve current facts. A retained reader/previous issuer response cannot supply new permission. Recheck expiry/current service status at final handoff; no actor-source changes during reauthorization.

Pilot starts at max100 active instances/installation, consumer concurrency1 and at most16 queued receives. MSG owns an additive admission-reservation ledger: max1,000 unsettled logical publications and32MiB summed full serialized Data, reserved under the same global fence/root as publication. API/worker new-publication paths all participate; duplicate same-key/hash does not reserve again, changed hash conflicts. Mark reservations settled only after exact terminal transport/logical evidence; Unknown retains budget, no time-based free. No other production message types/publishers are activated outside this guarded catalog. This is an admission ledger, not a queue/retry engine; native Wolverine continues transport recovery. MSG schema/ACL manifest additive change requires its own owner tests at12.3a/f.

This logical hard quota does not claim a physical byte bound over native retry/handled history or PostgreSQL WAL. Before activation the target-specific operation plan must set/enforce actual storage volume quota and measured stop-admission/free-space thresholds, DB monitoring freshness (max5s; missing health denies new work), backup headroom and retention capacity. Test exhausted reservation and real fixture storage-pressure/outage handling without filling a shared volume. Missing operational sizing/monitoring leaves activation gated, not a passing capacity claim.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.
