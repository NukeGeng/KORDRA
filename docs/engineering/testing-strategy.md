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
