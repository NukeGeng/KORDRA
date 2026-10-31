---
id: FEAT-MSG-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
module_id: MOD-MSG
feature_id: FEAT-MSG-001
phase: MVP
source_paths: [".github/workflows/product-ci.yml", "Kordra.slnx", "src/Modules/Messaging/Infrastructure/", "src/Modules/Messaging/Contracts/"]
test_paths: ["tests/Messaging.Durability.Tests/", "tests/Messaging.Tests/"]
planned_test_paths: ["tests/Messaging.Durability.Tests/", "tests/Messaging.Tests/"]
planned_source_paths: ["src/Modules/Messaging/Contracts/", "src/Modules/Messaging/Infrastructure/", "Kordra.slnx"]
planned_doc_paths: ["docs/decisions/0020-messaging-runtime-adoption.md", "docs/contracts/durable-messaging-v1.md", "docs/engineering/t12-1-design.vi.md"]
---

# FEAT-MSG-001 — Durable command delivery and recovery


## Current implementation scope

The bounded T12.1 kernel is implemented and locally verified; the final section records its evidence, integration limits and publication gates. The broader worker-delivery feature below remains proposed and is not activated by this increment.

## Independent worker delivery slice — proposed 2026-10-20

Luna T12 MSG task follows accepted release/approval behavior. Adopt ADR-0002's exact package/store/envelope/retention/retry configuration and worker identity before runtime changes; the current proposed Wolverine direction is not installed by this design. [Release-execution v1](../../../contracts/release-execution-v1.md) requires atomic business intent/outbox, inbox dedup, commit-before-ACK and sanitized DLQ. MSG is internal operations delivery, not customer/business queue administration. No event candidates become live topics until owner/consumers and schema are registered.

Acceptance MSG-D1: kill before/after state+outbox commit and ACK; no tested committed command is lost and duplicate results do not duplicate logical effects. MSG-D2: wrong tenant/origin/expired authorization/schema rejects or quarantines without infinite retry. MSG-D3: bounded transient retry/deadline, DLQ metrics and explicit replay guards are verified; unknown provider outcome never causes blind execution redrive. MSG-D4: rolling compatible envelope fixtures and isolated database migration/restart tests pass. Message dedup is not proof of exactly-once provider effects. Worker hosting/authority protocol is a separate accepted gate; no broker/service is started here.


## User story / outcome
**Actor:** System / Platform Operator. **Goal:** Durable command delivery and recovery. **Primary owner:** `MOD-MSG`. **Phase:** MVP.

## Pre-conditions
Envelope validated and target consumer registered.

## Main flow
Persist business write + outbox → relay → consumer inbox → handler → store result → ack → metrics.

## Alternatives / exceptional flow
Transient retry bounded; terminal failure DLQ with redrive authorization and observation before re-execution.

## Messages and integration contracts
`deployment.dispatch.v1; message.dead-lettered.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Receive duplicated message; worker crash before ACK; message from wrong tenant; replay with invalid policy.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
No lost committed command in tested crashes; duplicate transport delivery does not duplicate external mutation.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## Source & test traceability
- Source files for the scoped T12.0 PoC: `Kordra.slnx` integration; no production MSG runtime.
- Test files: `tests/Messaging.Durability.Tests/` (approved test-only spike).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.


## T12 bounded design — 2026-10-30

[Thiết kế T12 tiếng Việt](../../../engineering/t12-design.vi.md) và [ADR-0019](../../../decisions/0019-durable-transport-workflow-bootstrap.md) đề xuất compatibility/fault PoC trước runtime adoption. T12.0 chỉ kiểm chứng MSG bằng test-only PostgreSQL fixture; không hoàn tất WFL, distributed authorization hoặc Mac-close. Source/test mappings vẫn chưa implemented. Chưa có package, process, permission hoặc technology adoption nào được cấp quyền bởi ghi chú này.


## T12.0 scoped owner approval — 2026-10-30

NukeGeng đã duyệt [ADR-0019](../../../decisions/0019-durable-transport-workflow-bootstrap.md) chỉ cho PoC T12.0; NukeGeng là custodian của spike. Planned test root là tests/Messaging.Durability.Tests/, chưa có executable/source mapping hoặc kết quả test. Phần runtime MSG/WFL và các quyết định T12 tiếp theo vẫn proposed. Developer phải giữ case ACL chưa đạt nếu chỉ có database-owner role; không tự tạo role hoặc tuyên bố compatibility đã kiểm chứng. Không có dependency/runtime/schema nào đã được tạo bởi approval annotation.


## T12.0 first Developer increment — 2026-10-30

The approved test-only Messaging.Durability.Tests project restores Wolverine 6.59.0 together with unchanged EF 10.0.11/Npgsql EF 10.0.3 and records a package lock. Eight target-guard tests pass without opening a database connection: exact loopback test target, case schema naming, redacted diagnostics and rejection of wrong host/port/database/role/search-path/session options. NuGet's transitive vulnerability query reports no vulnerable package entries at this observation; this is not a full security assessment. No Wolverine message/transaction/recovery behavior is established by these guards. Runtime, producer/consumer fault cases and ACL acceptance remain outstanding; no database role/schema or provider operation was performed.


## T12.0 transaction probe — 2026-10-30

Added real PostgreSQL commit/rollback probes using two EF DbContexts on the same connection/transaction: synthetic owner state, audit and floor plus Wolverine durable envelope are invisible to a separate reader before commit and all commit/rollback together. The candidate's default Dynamic startup fails without a runtime compiler package; no fourth dependency was added. The current no-consumer publisher probe uses supported Static mode. This does not prove generated consumer loading, two-process delivery, inbox/ACK recovery or runtime DDL denial. Fixture setup may create tables only in each newly allocated UUID schema; cleanup is confined to that schema after stopping its own host, including error teardown.

The separately approved fixed role/schema has not been created: preflight found PUBLIC CONNECT on other databases, so per-role GRANT/REVOKE alone cannot enforce the approved database-only boundary. A concrete exact-role-only temporary HBA proposal is pending separate owner permission; no authentication configuration, pre-existing role/schema or Product database was changed.


## T12.0 isolated ACL probe — 2026-10-30

The owner separately authorized the exact disposable role/schema and four role-specific temporary HBA rules. The PostgreSQL probe allowed DML on its synthetic fixture table and denied schema/database DDL, foreign owner reads/writes and connections to other databases. The role had no privileged flags. Cleanup removed only that fixture role/schema/block, reloaded without restart and restored the original HBA SHA-256; all cleanup checks passed. This supersedes the earlier pending-HBA observation. Private evidence: `restricted-runtime-acl-receipt.json` under `.amr-local/t12-design/`.

This proves the database ACL boundary only. Wolverine startup with migrations disabled and actual restricted consumer execution remain unverified; the full PoC is still in progress. No production runtime, company infrastructure or existing data was changed.


## T12.0 two-process Developer evidence — 2026-10-30

The test executable now pre-generates Wolverine handlers in Static mode using only the approved package graph. Regenerate from `tests/Messaging.Durability.Tests` with `dotnet run -p:T12CodegenBootstrap=true -- codegen write`, then build normally; the bootstrap property excludes stale generated source only during regeneration. It does not bypass runtime tests. Native queue and envelope schemas are both explicitly UUID-scoped (see DRIFT-T12-001).

Real process probes verified normal delivery, producer crash before commit (no state/envelope), producer crash after commit before flush (outbox recovery), consumer crash before effect and after durable effect before ACK, restart, and different-envelope/same-logical-key dedup. Short fixture node-health/reassignment periods (1 s polling, 3 s stale timeout) are explicit; they are not production defaults. A loopback PostgreSQL wire proxy drops the server's actual COMMIT response: the client fails, while a new direct connection finds the committed logical key without replay.

Five rejection/retry cases passed before the later isolation/serializer changes: wrong case, tenant, schema version and expired deadline reach DLQ after one attempt with no logical effect; transient failure reaches DLQ after five attempts with measured 1/5/15/60 s delays. Current-content rerun remains required. Default inbox retention is explicitly 10 minutes; a separate shortened retention fixture must verify cleanup without manually deleting inbox rows. Closed fixture serialization rejects unknown fields (including synthetic secret fields), duplicate keys, wrong type, bodies over 16 KiB and depth over eight. These tests do not establish a trusted production service identity.

The corrected restricted-role probe starts Wolverine with AutoCreate.None, handles one synthetic message, denies DDL and leaves relation inventory unchanged. Exact role/schema/HBA cleanup passed. Private receipts/logs remain under `.amr-local/t12-design/`; the initial default-schema failure and residual-object debt are retained rather than erased.

Remaining acceptance: current-content fault/retry/serializer/retention results, consumer inbox/state/output transaction analysis, unknown-type quarantine behavior, final deterministic gates, residual-object disposition and complete-feature publication. Producer multi-context atomicity and synthetic effect dedup do not prove atomic production consumer state/inbox/output or distributed authorization. No runtime MSG/WFL/ControlWorker adoption or T12 completion is claimed.


## Current PoC report — 2026-10-30

[Tiếng Việt: kết quả T12.0 và gate adoption](../../../engineering/t12-poc-results.vi.md) records the full acceptance matrix, actual candidate configuration, corrected consumer inbox/state/result transaction, retention, unknown serialized type and queued expiry. Earlier notes above are dated progress observations. DRIFT-T12-001 residual cleanup was explicitly authorized and verified; no default-schema relation remains. Full gates and publication are still pending until exact-content evidence is recorded.


The shared architecture suite now explicitly validates this accepted test-only solution addition and its pinned, Product-isolated package graph. Canonical gate testing needs an explicit bounded 600-second serial budget for the expanded suite; the 300-second timeout failure remains recorded, never promoted to success. The native macOS artifact test additionally requires the pre-approved synthetic `KORDRA_CREDENTIAL_KEYCHAIN_FIXTURE_ROOT`, not access to existing Keychain items.


## Final local validation — 2026-10-30

Canonical `gates --base 69c5d86f3bc85a23636a475a8785543f1641df84 --product-test-timeout 600 --validation-context <private-nonsecret-epoch>` passed with stable source: docs, scope, 220 tooling tests, Clients pin metadata only, locked restore, Release build, serial Product tests and source mapping. Product result: 1,594 passed, zero failed, one existing T09 live Nomad test skipped; all 34 T12.0 tests passed with zero skips. No existing Keychain item, VM or real provider was accessed to replace the skipped test.

The full PoC implementation is locally validated. Hosted CI, exact PR review evidence and human merge remain separate. T12.1–T12.4/runtime adoption is still unapproved. Earlier partial/failing receipts remain retained, including default-schema escape, initial unknown-type fixture error, missing graph entry, missing synthetic fixture-root environment and the 300-second serial-suite timeout. The corrected final run used the approved isolated synthetic Keychain root, explicit bounded timeout and a rotated fixture epoch after interruption. Existing historical GOAL checkpoints were not rewritten or replayed. A pending bounded CLI plan is not represented as completed native execution for the user-authorized Medium session; actual coordinator/model/source/test receipts are retained separately.


## T12.1 bounded Architect proposal — 2026-10-30

[ADR-0020](../../../decisions/0020-messaging-runtime-adoption.md), [Durable Messaging v1](../../../contracts/durable-messaging-v1.md) và [thiết kế tiếng Việt / T12.1a–f](../../../engineering/t12-1-design.vi.md) là **proposed**, chưa cấp quyền runtime adoption. Owner/custodian đề xuất NukeGeng; phạm vi là Contracts/Infrastructure library và conformance, chưa activate API/business consumers/ControlWorker. Planned paths là kế hoạch, không implementation mapping. Adapter giữ commit ở caller, dùng pinned public transaction APIs qua compatibility gate; AUD isolated-test seam được trình rõ, không nới production guards. Không suy approval T12.0 sang migration/role/HBA thật.


### T12.1 scoped acceptance recorded — 2026-10-30

NukeGeng accepted [ADR-0020](../../../decisions/0020-messaging-runtime-adoption.md), [Durable Messaging v1](../../../contracts/durable-messaging-v1.md) and T12.1a–f on the exact reviewed proposal, including the guarded AUD UUID-schema test seam. Earlier proposed notes are historical. No Product implementation is established by this annotation; production target guards/authority remain unchanged. Design approval grants no real installation migration, new role/HBA, credential or provider access. T12.1a public-API/actual-AUD compatibility proof precedes dependent slices.


## T12.1 kernel — implemented and locally verified, 2026-10-31

Scope: accepted ADR-0020 and Durable Messaging v1, T12.1a–f only. NukeGeng approved the design, the later bounded logging correction and the separately scoped operational fixtures. The current implementation is a library kernel; the business catalog is empty. No API/ControlWorker activation, distributed authority, T12.2+ adoption, deployment or production database provisioning is implied.

| Area | Implementation and observed evidence |
|---|---|
| Contracts and wire | BCL identity/full logical key, explicit tenant/system scope, typed closed registry, canonical binding/hash, bounded payload/depth/deadline, envelope validation and native ingress rejection. No CLR serializer names or message metadata authorize work. |
| Caller transaction | Public native persistence/enlistment APIs stage on the caller connection/transaction. Actual AUD integration, owner/floor all-or-nothing, commit/rollback/crash and real lost-COMMIT-reply cases passed. Runtime uses no EF Internals/protected SQL/reflection fallback. |
| Receipts and reconciliation | Full-key/hash immutable publication/logical receipts, original outcome/ID preservation, terminal deny, unique contention and CAS rollback. Independent read-only inspection returns observed matching/conflict/unknown/unavailable; absence never authorizes replay. |
| Bounded recovery | Five actual attempts and native1/5/15/60-second timers, sequential restart, one successful effect and separately one sanitized exhausted-retry DLQ passed. UnknownCommit remains inspect/fenced, never generic automatic retry. |
| Lifetime and storage | Native expired/25hour-old delivery rejection, confirmed rollback and postcommit cancellation, seven-day handled retention and bounded native cleanup protecting active/scheduled work and91day receipts passed. No owned automatic purge. |
| Capacity | Actual10,001 pending rejects new publication and rolls back its staged receipt;10,000 accepts; unavailable storage denies without dropping committed rows. This is an availability watermark, not a concurrent hard quota. |
| Schema and privilege | Exact package-generated native/owned DDL, ACL inventory and review manifest; current incoming/DLQ PK(id,received_at). Dedicated disposable PostgreSQL15.18 instance passed no-DDL restricted native startup, destination identity, DML rollback,7permission denials, cross-DB rejection and missing/mismatched readiness. It was stopped and deleted. |
| Diagnostics | Explicit MessageDiagnosticLoggingProvider and MessageTrackerDiagnosticExport project only closed metadata before output. Native sentinel/provider, tracker-export and sampled-span tests plus protected actual-AUD/crash/UnknownCommit DB regression passed. Raw tracked sessions, arbitrary exporters and global host logging are not implicitly safe. |
| CI and gates | Existing Product workflow prepares synthetic ephemeral-service manifests; MSG classes run serially to honor fixture budgets/process-global tracing. Canonical local gates passed:220tooling tests,1,770product tests,0failures; one optional live personal-Nomad case skipped because its separate operational scope was not activated. No tests were removed or weakened. |

### Source and verification ownership

MSG owns Contracts, Infrastructure/Persistence, Serialization, Transactions, Recovery and Diagnostics, with tests in Messaging.Tests. AUD changes are limited to the accepted guarded UUID test seam/model-cache binding in its three Infrastructure files. Solution/architecture checks preserve module isolation; API, Agent and ControlWorker composition remain unchanged. Other owner feature links record future integration boundaries, not authority adoption.

Private exact approvals, original accepted proposal snapshots, failed evidence, per-case source hashes, cleanup receipts and coordinator checkpoints remain under `.amr-local/t12-1-design` / `.amr-local/approvals`. Original failed checkpoints are not replayed or relabeled. The latest configured canonical receipt is `full-gates-20261031-v1/gates.json`, with verified base main8f07308 (merged PR22; same tree as initial954c9e9). Documentation-only reconciliation reruns docs/scope/mapping/tooling and may reuse successful unchanged product gates only through explicit gates --reuse with the same fixture epoch and original freshness window.

### Limits and publication

Native retry booking is a separate action after a confirmed terminal root transaction. Native ClearAllAsync catches rollback failures and AUD RollbackAsync does not expose its internal confirmation result; successful method return alone is not no-commit/custody evidence. UnknownCommit never enters generic retry. No protected scheduling SQL or custom scheduler is substituted for native persistence.

Local evidence is not hosted CI/native PR review or production readiness. Optional live provider/company access, automatic host/logger registration, distributed takeover, worker identity, replay, migration apply, disk sizing/hard quota and arbitrary telemetry extensions remain outside this kernel acceptance. Keep native failure diagnostics closed and raw exception/session data private; preserving caller exceptions does not permit exporting them. A future host must explicitly invoke schema readiness and select the protected output boundary. Feature PR/CI/Codex review and human merge remain separate gates.
