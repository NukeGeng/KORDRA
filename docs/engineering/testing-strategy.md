---
id: ENG-TEST
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Test pyramid, failure injection & security regression

## Levels
1. **Domain unit tests:** invariants, deterministic transition, policies, role grant ceilings, tenant ID.
2. **Contract tests:** OpenAPI, message schema compatibility, agent/driver version negotiation.
3. **Integration tests:** PostgreSQL/Marten/Wolverine inbox/outbox, broker (if used), registry/secret adapters with isolated test env.
4. **End-to-end tests:** approve → frozen release → dispatch → observe → status; Helm K8s test cluster & Compose target in sandbox.
5. **Chaos/recovery tests:** kill process between commit/outbox dispatch, replay duplicate, stale epoch, unknown agent outcome, lost ACK.
6. **Security tests:** horizontal/vertical access escalation, support TTL, cross-tenant list/query/websocket/export, secret exposure, webhook spoofing.

## Required cases per feature
| Case | Minimum assertion |
|---|---|
| Success | observable business outcome |
| Wrong tenant | deny without existence leak |
| Missing/revoked grant | denied at execution boundary where critical |
| Duplicate message/retry | one logical mutation, idempotent receipt |
| Worker/agent restart | persisted status, safe recovery |
| Timeout | Unknown or safe failed state, not blind rollback |
| Contract evolution | producer/consumer compatibility |
| Audit | correlation, actor/scope, safe logs |

## Evidence
Store test identifier/report link, target build SHA, observed timestamp; no green status from unexecuted tests. Feature cannot be `verified` without real test evidence.

## Suggested CI stages
`docs-check → format/lint → unit → contract → integration → security isolation → driver conformance → build/package → staging smoke`. Slow chaos tests may run nightly/pre-release but must be recorded and cannot be silently skipped for relevant risk.

The detailed project-reference, assembly-boundary and host-composition test proposal is in [architecture-test-rules.md](architecture-test-rules.md). It is not an active CI gate until the rules are accepted, real project edges exist, and an executable test plus CI result is recorded.

## T04 definition and tenant owner validation — 2026-10-22

FEAT-IAM-001 RoleDefinitionTests/DefinitionRuleTests validates exact revision/retirement, manifest replay/conflict, released offline lease and exact Foundation permission/scope/effect interpretation. FEAT-TEN-001 TenantMembershipTests uses the existing private KORDRA_IAM_TEST_CONNECTION_STRING only after exact target/role guard (127.0.0.1:5432/kordra_test as kordra_test). The test fixture migrates only isolated owner contexts, seeds synthetic principals/definitions and cleans only its tracked tenant/member/assignment/receipt/definition/principal IDs; immutable audit evidence is retained.

TEN acceptance mapping: TEN-F1 subject-bound minimal selector and same-principal multi-tenant isolation; TEN-F2 replay/conflict, stale revision, composite cross-tenant FK, grantor capability/expiry ceiling, explicit deny/current definition and ambiguous assignments; TEN-F3 shared reader/writer cancellation and committed lifecycle/ancestor invalidation; TEN-F4 real AUD transaction rollback, setup timeout without gate poisoning and committed owner mutation/lost ACK with receipt replay after a fresh gate. Fault adapters are test-only; unknown outcomes never become success. Actual HTTP U/D race tests and production boot/restart reconciliation belong to T06/T07; creating a fresh test gate does not prove a real production restart.

ArchitectureTests validates the actual restored project graph and compiled PE references for IAM/TEN Contracts/runtime; Contracts remain BCL-only and cross-owner runtime references are rejected. Existing compiled negative fixtures, denied inventory HTTP tests and unrelated Product suites remain required. No frontend/submodule tests are included.

## T04 shared PostgreSQL fixture scheduling — 2026-10-22

The canonical Product CI and existing amr.py deterministic gate now pass MSBuild -m:1 to the solution test command, so separate Product test projects run sequentially. AUD saturation tests deliberately acquire ACCESS EXCLUSIVE on the shared isolated aud_intents table; running new TEN fixture provisioning concurrently produced legitimate bounded unavailable results. Project serialization preserves every assertion, timeout, failure fixture and test count, and changes no product runtime or AMR routing/review/checkpoint policy. No Website process, service or test is involved.

## T05 PRJ/INV owner and transaction evidence — 2026-10-22

ProjectEnvironment.Tests runs both owner slices in the same scoped integration project because the critical assertion is atomic PRJ association + INV target + AUD, with separate owner contexts. It uses only the private exact kordra_test locator, synthetic IAM principals and real TEN exact-scope grants; policy approvals are explicit fixture data with no production registration. Cleanup is limited to tracked synthetic tenant/principal/definition IDs and hashed command keys; audit rows remain immutable.

PRJ-F1/F2: exact replay/conflict, stable references, application/environment owner FKs, wrong subject/tenant/project, no implicit project/target permission, current policy and explicit classification. PRJ-F3/INV-F1: a GUID-named row-specific test trigger fails INV save after PRJ save; assertions verify both owners, receipt and audit rolled back and same-key retry creates one result. Trigger/function are removed in finally. Unknown commits are exercised both with durable data and without commit; fresh test admission/receipt replay produces one binding, never claimed as a real production restart proof. PRJ-F4: lifecycle, revision/change-back, stale classification/policy/association, active parent and deny on unknown/malformed environment absence. T06/T07 still own actual U/D HTTP race/disclosure proof.

Targeted execution: 28 T05 PostgreSQL cases passed, zero skips. Four architecture cases passed against actual XML/restored/PE graph, including compiled forbidden-edge fixtures. The PE helper now qualifies nested types by declaring type, preventing compiler-generated same-name collisions without excluding generated types or weakening Contracts isolation. Both new owner model/migration comparisons reported no drift. Required final full Product gate remains separate from targeted results.


## Disposable regression target preparation — 2026-11-08

The owner confirmed that existing kordra_test.app may contain data to retain and selected preparation of a separate disposable database. [ADR-0025](../decisions/0025-disposable-regression-database-profile.md) is proposed for an explicit manifest-bound test-only target, with no ambient runtime override or fallback. A test-only profile parser/negative suite exists; owner factory/transaction guard integration and operation approval remain pending. Profile parsing/export cannot create/migrate/drop a database or establish approval. Current source/default/runtime guards and existing app data have not been changed for this profile.


### Exact profile design acceptance — 2026-11-08

NukeGeng accepted the exact ADR-0025 proposal for explicit test/isolated-transaction integration. Owner factories, AUD bridges and WFL/MSG context/root/provenance/custody test entry points are being extended with explicit immutable profile projection; default/runtime paths remain fixed. Offline construction/negative evidence is separate from actual DB qualification. Test-host/owner coordinator threading and a final frozen operation manifest are still required before any database creation/migration/drop. Existing kordra_test.app remains protected.

ADR-0025 test-runner descriptor preparation uses `KORDRA_REGRESSION_PROFILE_PATH`, `KORDRA_REGRESSION_RUN_ID`, `KORDRA_REGRESSION_SOURCE_SHA256` and `KORDRA_REGRESSION_MANIFEST_SHA256` only in shared test code. All absent retains legacy selection; partial/invalid input fails with no fallback. Fixture composition must explicitly capture/pass the immutable profile, not change Product environment routing. The approved future runner must verify current source/SQL/config/operation approval before any DB operation; a self-supplied descriptor does not authenticate these rights. Full suite wiring is still pending.

The captured `ProductPostgreSqlFixtureSelection` now threads that runner selection into actual owner factories, audited mutations and fixture HTTP DI. It binds only owner transactions already registered by the fixture and preserves explicit custom audit fault factories; it never registers unrequested modules. Runtime/default entries remain independently tested. Native WFL/MSG and remaining isolated fixture descriptors still need explicit wiring before the disposable-DB operation manifest can be frozen. During this in-progress wiring, use targeted/default-guard tests and compile the changed graph; do not repeatedly rerun full DB tests with unchanged missing configuration. Required final gates remain mandatory and cannot be replaced by these targeted results.

Native/isolated selection validation uses two offline configurations: descriptor absent (legacy) and a synthetic closed disposable descriptor (no credentials/operation rights). The matrix constructs models/inspectors and exports SQL without starting native hosts; it also checks default guards and the exact derived restricted-role boundary. Mixed live/export SQL helpers receive explicit offline metadata selection so a profiled run cannot break or silently retarget exports. Owner connections remain unpooled under the disposable profile; legacy crash fixture pool settings are retained only on the legacy path. Actual native execution and resource/cancellation/crash budgets still require a separately approved immutable operation manifest and observed DB/process cleanup.
