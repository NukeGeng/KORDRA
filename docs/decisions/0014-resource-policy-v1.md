---
id: ADR-0014
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-22
---

# ADR-0014 — Explicit resource-policy identity and binding for T06

**Status: Accepted for the exact T06 resource-policy scope on 2026-10-22.** The maintainer explicitly approved ADR-0014 and Resource Policy v1 together. This completes the missing resource-policy design input and supplements ADR-0012 without superseding its technology, ownership, single-process admission or U/D protocol. Design acceptance is not implementation completion or operational authority.

## Context / problem / forces

T05 persists environment `policyRevision` but no authoritative resource-policy identity, current revision, payload or applicability source exists. Its policy allowlist is test-only. T06 must not treat that integer, IAM RoleDefinition or a fixture as resource eligibility. Grant authority and resource eligibility are distinct mandatory predicates.

## Decision drivers and invariants

Maintain deny-by-default, explicit deny precedence, exact tenant/project/environment ownership, current immutable policy and binding evidence, writer ordering, durable audit and no protected output before D. Preserve existing code/data/checkpoints and keep the addition bounded to `target.read`.

## Considered alternatives / trade-offs

| Alternative | Assessment |
|---|---|
| Treat positive revision/fixture or RoleDefinition as resource policy | Reject: policy identity, complete applicability and independent resource constraint remain undefined |
| External policy engine, policy inheritance or arbitrary expressions | Defer: unnecessary dependencies, precedence semantics and operational scope for this read slice |
| Mutable policy content with integer version checks | Reject: weak history/change-back detection and ambiguous exact evidence |
| IAM immutable fixed rules plus authoritative head and explicit PRJ binding | Proposed: bounded evaluator, one eligibility source and explicit failure/compatibility semantics; costs additional owner tables and conservative denial for stale bindings |

## Decision

The maintainer accepted the exact [Resource Policy v1 contract](../contracts/resource-policy-v1.md):

1. IAM owns policy identity, immutable revisions/fixed rules, authoritative current head and evaluation. TEN retains grants, PRJ owns current policy bindings/history, INV owns targets/associations, AUD owns append evidence, Host composes owner ports.
2. Only `target.read` is supported. Require both current IAM/TEN grant and resource allow; explicit deny wins. Rules are one canonical case/effect set; no second classification allowlist or arbitrary predicates.
3. Exactly one explicit current binding applies. An environment-bound target uses that environment; an environment-free target needs matching PRJ/INV resolved absence plus explicit `NoEnvironment` support. No inheritance or fallback.
4. Current policy comes from the IAM head, never `MAX(revision)`. Binding must pin that current revision. Content/status/validity changes append `n+1`; binding changes increment their own revision. No revision reuse, future scheduled activation or change-back reset.
5. Publish one policy and an explicitly bounded set of selected bindings plus receipt/history/AUD in one transaction. Unlisted old bindings deny after a head change. Existing owner-created environment bindings are permitted only with explicit current-policy validation under existing catalog authority; online replacement/publication stays excluded.
6. Bootstrap/publication/rebinding use approved installation-bound offline manifests, exclusive lease/API stopped, exact idempotent receipts and unknown-commit reconciliation. No new HTTP policy mutation permission, UI, service or operational authority is implied.
7. U/D binds and compares policy/binding IDs, revisions and policy hash with all existing owner facts. Keep current gate, audit intent semantics, trusted time, bounds and HTTP handoff; no cache or automatic retry.
8. Add owner tables/migrations; preserve legacy T05 integer data as non-authoritative, without Allow backfill. Update actual internal consumers together and define schemaVersion 2 for future affected positive wire requests before enablement. Keep ingress closed until tests pass.

## Consequences (pros/cons, risks)

The missing resource policy becomes explicit without a second authorization service. Historical revisions/bindings remain auditable and change-back invalidates in-flight results. Shared-head publication may intentionally make unlisted scopes unavailable; dry-run impact and explicit manifest approval are required. Additional table/port and migration tests are necessary. Offline-only policy maintenance limits convenience but avoids a premature admin permission/workflow. The existing single-process/trusted-DBA limitations remain.

## Migration/rollout/rollback plan

Follow contract sections 3, 6 and 8: additive IAM/PRJ migrations, explicit owner provisioning with audit, update all consumer DTOs/fixtures, U/D and real HTTP output proof, then enable the adopted path. No real database migration/provider call is authorized by accepting design. Rollback closes positive ingress while retaining records/audit and monotonic revisions. Do not rewind heads, fabricate default policy or replay completed checkpoints.

## How to verify / revisit triggers

Contract section 9 defines negative, PostgreSQL, atomicity/recovery, canonicalization, owner-boundary and HTTP/concurrency acceptance. They are planned tests, not executed results. Native Codex Auto PR Review remains the default implementation PR reviewer. Revisit before online policy publication/rebinding, scheduled activation, broader actions/conditions, inheritance/composition, distributed writers or production policy administration.

## Related modules, contracts, supersedes

Owners FEAT-IAM-001, FEAT-PRJ-001, FEAT-INV-001 and FEAT-AUD-001; [accepted ADR-0012](0012-operator-pilot-runtime-foundation.md), [admission v1](../contracts/authority-admission-v1.md) and [bounded contract](../contracts/resource-policy-v1.md). Supersedes no accepted ADR. Upon acceptance it resolves the resource-policy `[NEEDS-DECISION]` in the PRJ feature; historical notes remain history.

## Approval record

**Accepted by NukeGeng on 2026-10-22:** “Duyệt ADR-0014 và Resource Policy v1 đúng phạm vi T06”. Before this status annotation, ADR-0014 SHA-256 was `2619a9c0fd9a02de062c90bbc915e8b9cf0dcf3bf7ae4976e1d207f2d1581f15` and Resource Policy v1 SHA-256 was `c10f90b4198653a8cc3f14256a59cd14e8d82494dfef8ae8c87103db7eb62fb5`. Both matched the preserved proposal; all 11 context-document hashes were also checked unchanged. The original snapshots/design receipt remain immutable. Private acceptance evidence is recorded at `.amr-local/approvals/ADR-0014-resource-policy-v1-acceptance.json`. This approves the bounded design and synthetic-test implementation scope only; no source result, checkpoint completion, native model profile, PR action, real migration/provider access or merge is claimed.
