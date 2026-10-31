---
id: ENG-T12-CONFORMANCE-PLAN
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-31
---

> Accepted scoped design ngày 2026-10-31; xem Exact scoped acceptance ở cuối file. Chưa có runtime/operation qualification.

# T12.2–T12.4 — acceptance và bằng chứng bắt buộc

Companion của [thiết kế](t12-remaining-design.vi.md), [WFL contract](../contracts/durable-workflow-v1.md), [worker contract](../contracts/worker-authority-v1.md) và ADR-0021/0022/0023. Tất cả case dưới đây là planned, chưa chạy. Unit mocks không chứng minh database atomicity, distributed ordering hoặc host độc lập.

## Fixture và oracle

Mỗi fixture có caseId/installation/tenant IDs tổng hợp, actor/action/scope, exact SHA/toolchain/lock graph, source/SQL/ACL hashes, resource allowlist, lifetime/budgets, operation approval và cleanup receipt. Dùng fresh UUID schemas trong kordra_test sau approval, fresh fixture service logins/certs nếu cần và hai OS processes thật. Không tái sử dụng account/trust/role/HBA approval T12.1 cho fixture mới. Không lấy target công ty làm shortcut. Những case không cần IO chạy trước, case IO chỉ sau SQL/operation review.

Oracle connection độc lập đọc owner-safe views/fixture tables để đếm state/history/AUD/MSG/publication/logical receipts, version/epoch/hash và absence of unauthorized output. Không purge để làm test pass; compare before/after, verify exact cleanup resource IDs và hash HBA/trust nếu có approved temporary change. Service identity SQL tests phải dùng actual least-privilege login, không owner/superuser giả runtime.

| Case | Thao tác/negative case | Oracle và exit condition |
|---|---|---|
| WF01 | Start same request/hash; changed hash; wrong installation/tenant/env; unknown template/version; Production | 1 immutable instance/receipt; conflict không state change; wrong scope deny và no leaked result |
| WF02 | Hai real transactions cùng expectedVersion; reordered step; wrong target/release/template hash | Một CAS/transition; loser inspect current receipt, không effect/result thứ hai |
| WF03 | WFL+actual AUD+floor+MSG commit/rollback, failure từng staging boundary | All-or-nothing qua reader độc lập; no early flush/Handled; shared server-root ten-envelope/160KiB limit |
| WF04 | Kill producer trước commit, sau commit trước flush; kill consumer trước effect, sau commit trước ACK | Pending transport recovered; mỗi logical transition/history/result/AUD chỉ1; không mất committed intent |
| WF05 | Wire proxy drop actual PostgreSQL COMMIT reply; stop/restart successor trước old backend terminal | Matching/Conflict/StillUnknown đúng; không callback/reclaim khi chưa terminal; original failure giữ nguyên |
| WF06 | Timer dài hơn120s, deadline24h, due/restart, duplicate timer generations, overdue message | Không chạy sớm; old original deadline không refresh; successor schedule có custody/receipt proof, counter≤10 |
| WF07 | Cancel trước start, lúc timer fires/CAS, khi Running/Unknown, sau terminal | Intent không thành undo; Unknown không chuyển Failed/Cancelled khi thiếu evidence |
| WF08 | Five attempts1/5/15/60s, oversize/depth/invalid wire, DLQ, clock rollback/floor mismatch, retention | Bounded attempts; safe diagnostics; active/Unknown/90day receipt không mất; expire không renew quyền |
| AU01 | Missing/wrong mTLS CA/SAN/EKU/pin, expired/revoked cert, old service generation, spoof body/GUC/tenant | Internal RPC/SQL deny trước owner transition; no cookie forwarding hoặc grant by role label |
| AU02 | Permit cùng ID nhưng khác service/instance/hash/version/tenant; expired/consumed permit; same permit new DB root | Chỉ exact service/root/operation được stage một transition; SQL direct bypass bị deny |
| AU03 | Every invalidating writer before permit, giữa issue/consume, trước/đang/after worker fence commit | Epoch ordering đúng; revoke committed trước thì reject; worker trước thì writer chờ bounded; no stale permit after change-back |
| AU04 | Writer matrix gồm logout/security boot, roles/policy, memberships/grants, owner binding/lifecycle, REL/APR/SEC/service revoke | Mỗi writer có real entry-point race test + source path; registry/static grep không đủ coverage |
| AU05 | API/DB outage; RPC response lost/duplicated; transaction budget/expiry; issuer restarts | Fail-closed no cached grace; duplicate same key returns original; unknown root giữ custody, no auto retry business |
| DB01 | Runtime create/alter/drop, foreign schema read/write, AUD update/delete, raw WFL writes, arbitrary function args/search_path | Actual login deny; grant list/function source/hash đúng; SECURITY DEFINER không nhận identifier/tenant authority tùy ý |
| DB02 | Upgrade/downgrade/schema/template/lock mismatch; backup restore old authority floor/epoch | Readiness deny incompatible/stale authority; expand/contract rehearsal không mất history; no startup DDL |
| HO01 | Two workers cùng installation, lock connection loss, suspend old process tới stale heartbeat, restart competitor | Chỉ1active; time expiry khôngtakeover; previous backend terminal + receipt reconciliation trước epoch mới |
| HO02 | SIGTERM10s drain, SIGKILL tại tất cả root boundaries, native transport reassignment | Committed state survives, Unknown preserved; host not ready khi custody chưa resolve |
| HO03 | Full unchanged T01–T11 / MSG regression, DTO fixtures/golden vectors, source dependencies | Không public fake/type activation, no breaking consumer change, no Agent/private-owner references |
| RH01 | API+worker+DB off-Mac, start synthetic admitted delayed workflow, close client then sleep Mac≥120s | Independent host records transitions/timer/AUD during closure, compare service-side times/versions after reconnect |
| RH02 | Repeat RH01 but stop issuer API, then restore it; no hidden Mac process allowed | Worker custody/metadata remains; business transition blocked while issuer absent; reevaluated permit after restore; no promise offline authority |
| RH03 | Restart actual worker independent host; controlled service/DB outage within approved plan | Recovery outcomes and independent reads verified, no data/history loss beyond recorded unknown; no provider calls |

## Evidence bundle và gate semantics

Bundle riêng mỗi case: environment identity (public redacted), protected authorization/operation manifest, exact source SHA and hashes, clock/expiry boundaries, invoked commands, process/backend IDs privately, before/after counts, original failures, terminal results, cleanup proof. Logs only fixed codes/IDs/hash, no credential/payload/connection string. Test report phải list skipped/pending với lý do; không suy rằng native test passed khi chỉ chạy guard.

T12.2 exit WF01–WF08 + applicable HO03, thực tế AUD/MSG khi transition integration có; distributed positive HTTP/worker activation chờ T12.3. T12.3 exit AU01–AU05/DB01–DB02/HO01–HO03 với exact writer matrix và owner functions; mỗi slice commit theo feature/owner, required local gates + exact-head hosted CI + Codex BIR. Không thêm AI reviewer tại validate/accept. T12.4 local exit toàn matrix trừ RH; remote qualification cần RH01–RH03 và riêng operation approval. Bất kỳ unresolved blocking finding hoặc pending mandatory remote case giữ broader milestone chưa hoàn tất.

Mỗi failed execution phải ghi cause, code/test fix và targeted rerun trước full gate; giữ failed checkpoint/receipt. Reuse chỉ qua deterministic one-hour fingerprints/fixture epoch. Không nâng timeout hoặc bỏ assertion để pass mà không đối chiếu requirement/operation limit.

Reauthorization cases AU05/WF01 bổ sung: same original actor/current grants can replace an expired delegation only in BlockedAuthority; original spec/deadline/retry counters unchanged, old delegation rejected after epoch change. Wrong actor, new tenant/release/template, Unknown/terminal state or stale If-Match deny without state change. Current release owner must affirm any new approval applies to the exact original release revision/hash.

Capacity gate CAP01 (T12.3/T12.4): exactly100 active instances,1,000 unsettled publications/32MiB boundary and competing API/worker writers under the fence. Rejected admission creates no provisional orphan; transaction rollback frees only uncommitted reservation, Unknown preserves committed reservation; duplicate does not allocate again. Terminal reconciliation settles once. Expired/missing storage health denies new work. Independent physical storage/WAL sizing is an operation qualification input, not inferred from a logical counter.

## Exact scoped acceptance — 2026-10-31

NukeGeng đã trả lời **“tôi duyệt”** cho bộ thiết kế T12.2–T12.4 vừa trình: ADR-0021/0022/0023, Durable Workflow v1, Worker Authority v1, thiết kế tiếng Việt, conformance plan và operation runbook. Cả18 file proposal liên quan đã được đối chiếu trùng SHA-256 với frozen snapshot trước khi ghi nhận. Approval evidence và pre-annotation hashes được giữ riêng tại `.amr-local/t12-remaining-design/design-acceptance.json` cùng `proposal-snapshot/`.

Accepted chỉ cho exact design: workflow states/CAS/timer/recovery/reauthorization và permission pairs, closed message registrations, service identity/mTLS, current permits/global fence, owner-owned restricted SQL functions, additive MSG admission ledger và independent-host qualification. Những đoạn proposed/chưa duyệt trong nội dung proposal phía trên được giữ để đối chiếu lịch sử; annotation này xác lập acceptance thiết kế, không biến chúng thành implementation evidence.

Developer được triển khai theo dependency12.2a–e →12.3a–f →12.4a–d, với gate compatibility/ACL/writer-coverage không được bỏ qua. **Không cấp quyền thao tác DB/role/HBA/credential/CA/trust/host/network/backup/Mac sleep hoặc hệ thống công ty/provider; không cấp quyền merge.** Mỗi fixture/target operation vẫn cần immutable manifest và approval riêng. Không tự approve T13, thay accepted single-process baseline ngoài phần bổ sung được chốt, hoặc chạy thêm AI review.
