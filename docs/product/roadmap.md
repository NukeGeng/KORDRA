---
id: PROD-ROADMAP
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
---

# Delivery roadmap & architecture maturity

## Active delivery direction and design handoff — 2026-10-20

The maintainer approved the comprehensive product direction: management foundation → useful OP-P0 → frozen release/approval → independently hosted sandbox deployment/verification/rollback → release qualification. The [Control Plane delivery design](../architecture/control-plane-delivery-design.md) maps that direction to this existing roadmap, and the [Luna implementation handoff](../engineering/luna-implementation-handoff.md) supplies tasks T00–T14 with dependencies, feature acceptance and exact adoption gates. This is not a replacement GOAL/checkpoint system.

[FEAT-IAM-002](../features/identity-access/features/local-account-session.md) specifies local authentication separately from authorization. The maintainer accepted ADR-0012, local-authentication-v1, authority-admission-v1 and the Foundation slices T01–T07 on 2026-10-20. T01 persistence source/tests and T02 AUD source/tests are implemented and locally verified; their original failed native checkpoints remain unchanged and separately labeled evidence-recovery successors retain their own evidence. T03 local enrollment/login/session/recovery is implemented and locally verified: the bounded read-only `kordra_app` privilege probe passed against local `kordra`, and all 224 product tests passed with zero skips. The maintainer-authorized shared-owner document reconciliation addresses its initial source-mapping failure. T03 checkpoint completion remains separate because an outside-write protected-content mismatch was already present before this reconciliation; the original checkpoint baseline and execution evidence are preserved. T04–T07 still need their own source/test evidence and deterministic gates. Client API handoff, release/execution-v1 and later provider/distributed-worker choices remain proposed or separately gated. No protected inventory ingress or production operation is claimed.

Existing implementation inventory and historical receipts below remain unchanged. All I2.7 completed evidence is preserved. T01/T02 merged through PR #9 at `74c4ad82afa6feaa4107d797bc0568e149170996`; T03 is prepared as a separate feature PR whose hosted CI/review and human merge remain pending. Product clients/Website are independently owned; the backend contract handoff does not change their repository/submodule or claim UI acceptance.

| Stage | Deliverable | Exit criteria |
|---|---|---|
| 0 Foundation (current) | Scoped ADR-0008 and independent domain increment, source/security proposals | Baseline decisions and evidence reviewed, no runtime claim |
| 1 OSS Beta vertical slice | Target + artifact → release → approval → deploy → verify → authorized rollback | Isolated E2E tests, recovery/tenant-negative gates, independent onboarding |
| 2 Post-Beta integrations | Additional driver, CI/registry/GitOps, metrics/logs, bounded agent/reconciliation | Early-adopter evidence and signed/contract/failure tests |
| 3 Production readiness & Enterprise | Backup/restore, SSO, HA/DR, governance and operational SLO | Security/incident/migration evidence verified |
| 4 Commercial / Cloud exploration | Support/Enterprise offer or managed service if demand exists | Validated customers, license and sustainable operations |

Không ước lượng ngày công khi chưa biết team/capacity; mỗi stage có thể chia task riêng. Chỉ được ghi "Done" khi Definition of Done thỏa.

## v1.5 private Operator Pilot (proposal)

Before the public OSS Beta, validate a **Mac-first operations workstation** through approved private experiments: OP-P0 read-only environment/target inventory + version discovery, OP-P1 release queue/approval + sandbox deploy/verify/rollback/audit; OP-P2 backup/restore rehearsal and OP-P3 HPA/HA writes only after independent security/operations review. Shared LAB-01 remains observe-only unless separately approved. This does not mark the operator pilot or Control Plane implemented and does **not** broaden the mandatory OSS Beta. See [operator requirements](operator-workstation.md) and [pilot plan](operator-pilot-plan.md).

## OSS Beta stage mapping (proposal, carried forward from v1.4)

The current 20-module architecture registry remains intact, but OSS Beta requires a focused vertical slice rather than full enterprise scope. See [product positioning](positioning.md), [OSS Beta acceptance](oss-beta-acceptance.md), [full A–F technology landscape (43 groups)](technology-landscape.md), [commercialization](commercialization.md) and [early adopter validation](early-adopter-validation.md). Design production safety invariants now; only claim production-ready after separate evidence. The current technical Stage 0–4 table is **not** evidence of released features.

The existing F0/M1/M2/M3/M4 task identifiers below are retained technical dependency labels, not aliases for the revised product-stage numbers. The Beta slice still needs the durable execution, duplicate-delivery, crash and unknown-outcome safeguards in M2-A/M2-B before its exit gate; moving integrations to a later product stage does not defer those safeguards. OP-P0 can be decomposed into bounded read-only feature proposals first, with trusted target/credential decisions and accepted ADRs before introducing a UI stack or host.

## Current verified inventory and continuation
Baseline inspected: `d0eff75951d1d587b1d9689c189a8a62bae18d58`, clean `main` before branch `feat/artifact-identity-foundation`. At that baseline, AMR Python tooling existed with 43 passing tests and a successful initial GitHub docs CI run. There was no product source, host, persistence or application test project. ADR-0001–0007 and product feature specs were proposed; ADR-0008 is now accepted only for the scoped .NET/test bootstrap. The installed SDK is .NET 10.0.400; this observation is not an architectural approval.

Current milestone: **Stage 0 Foundation**. **FEAT-ART-001/I1** is a merged, narrowly scoped domain-validation increment: PR [#1](https://github.com/NukeGeng/KORDRA/pull/1) merged on 2026-10-17 (merge SHA `2aa156f4e6e3afe3cc648186ac6a3242781dc71b`); [documentation CI](https://github.com/NukeGeng/KORDRA/actions/runs/39000192554) and [product CI](https://github.com/NukeGeng/KORDRA/actions/runs/39000192528) succeeded for this merge. The repository records a COMMENTED review, not verified independent approval; provider automatic review is unverified. Registry-backed artifact registration and all Control Plane runtime capabilities remain unimplemented. ADR-0008 is accepted only for the scoped bootstrap, while ADR-0009 and other decisions remain proposed. This checkpoint is dated and must be re-verified on subsequent revisions.

## Source & solution architecture v1.2 assessment
The ten supplied documents are now organized under `docs/architecture/`, `docs/decisions/` and `docs/engineering/`. They remain `proposed`. The bundle's ADR-0008 number conflicted with the existing accepted bootstrap ADR-0008, so the source architecture proposal is tracked as ADR-0009; no decision was accepted by importing the files. The verified current source inventory and impact matrix are in [source architecture adoption](../engineering/source-architecture-adoption.md).

The independent native GPT-6.1 Sol High review of ADR-0009 and its six specs completed on a stable read-only snapshot. Its impact findings and dispositions are recorded in the adoption assessment. ADR-0009 remains proposed; preserve the existing root solution and source/test paths. The narrow permission/trusted-scope baseline in ADR-0003 remains accepted. ADR-0012 later adopted only the local identity/session/audit Foundation slice; the broader source architecture, messaging and provider decisions remain open.

| Module | Feature | Actual product implementation |
|---|---|---|
| MOD-IAM | FEAT-IAM-001 / FEAT-IAM-002 | Synthetic I1 policy proof; FEAT-IAM-002 T01 persistence, T02 audit and T03 local authentication/product tests are locally verified; T03 shared-owner documentation is reconciled; AMR completion still needs the pre-existing protected-content discrepancy resolved; authorization enforcement remains unimplemented |
| MOD-TEN | FEAT-TEN-001 | Not implemented; depends on identity/authorization/audit |
| MOD-PAD | FEAT-PAD-001 | Not implemented; scoped support/elevation policy open |
| MOD-PRJ | FEAT-PRJ-001 | Not implemented; trusted tenant ownership prerequisite |
| MOD-INV | FEAT-INV-001 | I1 local loopback API and inventory contracts implemented/verified; target registration and real Kubernetes/Helm reads remain unimplemented |
| MOD-SEC | FEAT-SEC-001 | Not implemented; secret provider binding and access policy open |
| MOD-ART | FEAT-ART-001 | I1 merged via PR #1 at `2aa156f4`; merge-SHA docs/product CI passed; scoped ADR-0008 accepted; registry-backed registration not implemented |
| MOD-REL | FEAT-REL-001 | Not implemented; artifact/catalog/approval dependencies unmet |
| MOD-APR | FEAT-APR-001 | Not implemented; policy/SoD/expiry/spec binding prerequisites unmet |
| MOD-MSG | FEAT-MSG-001 | Not implemented; durable transport/storage ADR open |
| MOD-WFL | FEAT-WFL-001 | Not implemented; message/lease/recovery contracts not validated |
| MOD-DEP | FEAT-DEP-001 | Not implemented; workflow/approval/driver dependencies unmet |
| MOD-AGT | FEAT-AGT-001 | Not implemented; enrollment trust/target scope decisions open |
| MOD-DRV | FEAT-DRV-001 | Not implemented; approved-operation/capability contract validation pending |
| MOD-OBS | FEAT-OBS-001 | Not implemented; observation trust/freshness contracts pending |
| MOD-REC | FEAT-REC-001 | Not implemented; inventory/observations/single-writer mode required |
| MOD-AUD | FEAT-AUD-001 | T02 append/lookup source, isolated PostgreSQL tests and deterministic gates passed; native checkpoint lacks a receipt and remains failed; audit query/export remains unimplemented |
| MOD-CI | FEAT-CI-001 | Not implemented; provider identity/signature/dedup contracts pending |
| MOD-NOT | FEAT-NOT-001 | Not implemented; authorized event-driven notification prerequisites unmet |
| MOD-SCH | FEAT-SCH-001 | Not implemented; durable timer/timezone/change-window policy pending |

## Dependency graph and implementation order
The module summaries contain cycles (IAM ↔ TEN; ART ↔ CI; REL ↔ APR). They describe runtime collaboration, not permission to introduce cyclic project references. Resolve task-level contract seams with owners/accepted ADRs before implementing cross-module wiring. This is tracked in DRIFT-BOOT-001; no dependency direction was silently changed.

### Active GOAL continuation — 2026-10-18

At initial inspection the native thread GOAL reported active; its live status remains authoritative in the native runtime. The original `KORDRA-FOUNDATION` checkpoint cannot load under the current schema because its immutable plan lacks the active objective; both it and `KORDRA-FOUNDATION-AMR-002` also pin the older GOAL content. Their recorded tasks remain `validation_failed` (stale review / missing terminal review), and read-only native `thread/read` returned `thread not loaded` for the old execution handles. Preserve both checkpoint files unchanged; do not replay or promote their work. A reconciled continuation uses the existing AMR checkpoint mechanism as `KORDRA-OP-P0-FOUNDATION-001`, with the full active user objective and current GOAL hash; no accepted tasks are carried forward.

At the initial specification checkpoint, actual source consisted of the artifact digest library/tests. The following documentation tasks prepare the read-only pilot's existing owner features; none authorizes runtime, new hosts/projects, authentication choices or company access. Product implementation waits for the relevant explicit decisions.

| Task | Primary feature / module | Dependency / quality floor | Current state / exit |
|---|---|---|---|
| F0-OPP0-INV-SPEC | [FEAT-INV-001](../features/infrastructure-inventory/features/register-target.md) / MOD-INV | Reconciled current context; Sol Medium | Documentation task completed: native Medium execution changed only the owner spec; full gates (114 tooling / 48 product tests) passed; native Sol High review exited on stable content with no actionable findings; contributor triage accepted. I0 remains proposed, source/tests unmapped; no inventory runtime implemented |
| F0-OPP0-PRJ-SPEC | [FEAT-PRJ-001](../features/project-environment/features/environment-catalog.md) / MOD-PRJ | INV spec task accepted; Sol Medium | Documentation task completed: verified Medium execution; one coordinator ownership ambiguity fixed within the declared file, then full gates and stable native High re-review passed and finding triage accepted. ConnectionProfile stays MOD-SEC-owned; no profile/environment runtime implemented |
| F0-OPP0-SEC-SEAM | [FEAT-SEC-001](../features/connection-secrets/features/secret-binding.md) / MOD-SEC | Both preceding tasks accepted; Sol High because credential/authorization boundary is involved | Documentation task completed after explicit user resume: native coordinator and task observed Sol High; full gates and stable independent native High review passed, then exact-content contributor triage accepted. Local/hosted credential references remain proposed; no credential runtime or access implemented |

The initial coordinator turn observed `gpt-6.1-sol/medium`. After the requested safe checkpoint and native GOAL pause, the user explicitly resumed; the subsequent native session event identifies `gpt-6.1-sol/high`, and the credential task independently observed that selection. Discovery advertises Medium/High and disabled collaboration sub-agents. These observations are turn-bound, not a permanent default or an automatic desktop switch. The local CLI still labels caller-supplied coordinator selection unverified; native task receipts are separate evidence.

A follow-on `F0-OPP0-ARCH-SPIKE` task under the existing AMR continuation `KORDRA-OP-P0-ARCH-EVIDENCE-001` is completed as a bounded experiment/evidence task: native High execution, retained sandbox restore failure, separately reconciled local fixture pass, full gates, stable independent High re-review and exact-content triage. One disposable Host-reference omission was reproduced, repaired and rechecked. Fourteen loopback HTTP assertions and two forbidden built-reference cases passed; these are not product CI, a real Agent or IAM/provider implementation. See [the owning evidence](../features/infrastructure-inventory/features/register-target.md#proposed-feasibility-evidence--opp0-arch-spike-001).

The three specification tasks above completed their documentation scope. Owner preferences select **Kubernetes/Helm read-only**, **API local on Mac first** and **Kordra-managed local accounts**. These preferences grant no cluster access or storage/permission approval.

The maintainer explicitly accepted [ADR-0010](../decisions/0010-local-readonly-bootstrap.md) and requested bootstrap → folder framework → next eligible work. The initial `KORDRA-OP-P0-BOOTSTRAP-001` source execution completed, but first gates exposed an xUnit analyzer error, a shared-owner AMR fixture assumption and then a macOS loopback-alias timeout. The failed checkpoint remains unchanged. Corrections preserve the guard and test invariants; primary listener shutdown still requires refusal and same-port rebinding.

Continuation `KORDRA-OP-P0-BOOTSTRAP-CONT-001` is now completed: shared-owner fixture repair and bootstrap finalization both have native Sol 6.1 High execution, passing full gates, stable independent High Code Review and exact-content contributor triage. Actual solution: six projects, no runtime PackageReference, local loopback API with constant liveness and uniform inventory denial. **114 tooling + 67 product tests pass**; actual Program smoke also passed. See [I1 evidence](../features/infrastructure-inventory/features/register-target.md#coordinator-final-acceptance--2026-10-18). GitHub CI/provider automatic review of this unpublished content remains unverified.

After that acceptance, the requested [folder framework](../engineering/repository-folder-framework.md) reserved 45 locations without new projects or source moves. ADR-0009 remains proposed. The later FEAT-IAM-001 proposal and synthetic-policy increment, plus narrow ADR-0003 acceptance, are recorded below. Runtime identity/session/storage and authority contracts remain prerequisites; inventory stays denied. Full GOAL and OP-P0 remain incomplete.

Native desktop GOAL was previously blocked awaiting human ADR approval. That decision is resolved and the current human turn authorizes this work; local checkpoint completion does not prove native GOAL resume. The available control API has no resume operation. Do not infer desktop state from these local receipts.

| Order / milestone task | Prerequisites | Implementation / validation exit |
|---|---|---|
| F0-A — repository/source/docs audit | Existing Git revision and governance | Inventory above, drift/debt and scope identified |
| F0-ART-I1 — immutable digest domain | Existing OCI invariant; isolated validation scope | Merged bounded increment; build/content/immutability/concurrency/boundary tests and dated native review evidence; scoped ADR-0008 accepted; no registration/runtime completion claim |
| F0-B — accept development baseline | Human ADR-0008 decision; owner assignment | Scoped ADR-0008 accepted; product owner assignment pending; no implied acceptance of all other ADRs |
| F0-ARCH-01 — source/solution architecture review | Current source audit; independent critical review; architecture/security owners | Review ADR-0009, migration impact and test strategy; explicit owner decision before changing source layout |
| F0-C — identity and audit contract seams | ADR-0003, identity provider, scope/deny/expiry and audit decisions | Tenant/platform isolation, revocation and scoped authorization contracts and tests agreed |
| M1-A — IAM/TEN then project/environment/target/secret references | F0-C, persistence/credential decisions | Trusted ownership and negative access tests before registration or API exposure |
| M1-B — artifact registration and signed CI intake | M1-A, registry adapter/SSRF/credential policy, signed webhook contract | Registry metadata/content verified; immutable tenant-owned record; dedup/audit; no mutable-tag substitution |
| M1-C — release and approval | M1-B, approved frozen-spec/SoD/policy contract | Exact artifact/spec/target binding; expiry/revocation/self-approval tests |
| M2-A — durable messaging and workflow | ADR-0002, execution/lease/state contracts | Inbox/outbox, restart, duplicate, order, fencing and unknown-outcome tests |
| M2-B — agents, driver and deployment | M1-C + M2-A, ADR-0006, approved enrollment/target ownership | Scoped fake-target end-to-end rollout; verify observed state; no production operation |
| M3 — observation/reconciliation/CI/notifications/scheduler | Trusted scoped state and durable workflow; ADR-0004 | Freshness, single-writer, signed provider, recovery, timezone and permission regressions |
| M4 — operational/enterprise hardening | Measured product behavior, explicit authorized test environment | HA/DR/backup restore, support elevation and on-call evidence; separate human operation approvals |

Continuation state lives here and in each owning feature/traceability entry, not a second task-management system. Before the next session, check Git revision, dirty paths, exact feature evidence and current review/CI status. At this 2026-10-18 checkpoint, the next action after merged I1 was to decide ADR-0009 and identity/tenant authorization prerequisites; later sections record the subsequent IAM design, proof and ADR-0003 decision. Provider-review/owner-evidence gaps remain separate. Do not move to production.

### IAM decision readiness after folder framework — 2026-10-18

`F0-OPP0-IAM-DESIGN` / FEAT-IAM-001 / MOD-IAM completed its **proposal-only** task in existing AMR continuation `KORDRA-OP-P0-IAM-DESIGN-001`: actual native Sol 6.1 High execution, current full gates (114 tooling / 67 product tests), stable independent native High review and contributor triage. [Canonical I0 design](../features/identity-access/features/authorize-resource.md#i0--op-p0-iam-design-readiness-proposed) records local-account preference, ownership/trust seams, alternatives, unresolved decisions and planned negative matrix. Review content `d3793bdefe3b9bb2beb849f653ce71b1b5c90ea3a38ef0df57d449e9b9137a88`; no actionable findings. No authentication/policy test implementation or ADR acceptance is inferred from existing suites.

At that 2026-10-18 I0 checkpoint, the proposed next task was to assign/review identity/session, membership/resource-owner and policy/audit contracts, then authorize a bounded synthetic pure-policy PoC. The I1 proof and narrow ADR-0003 acceptance below complete only the test-only policy increment and baseline decision. Runtime IAM, positive inventory access and remaining authentication/storage decisions are still open. Full GOAL remains incomplete. No production/cluster/credential operations or publication occurred.

### Local PostgreSQL development environment — 2026-10-18

Per explicit owner request, local Homebrew PostgreSQL 15.18 now has `kordra` and isolated `kordra_test`, with the requested superuser/admin/app/test role split. [ADR-0011](../decisions/0011-local-postgresql-roles.md) accepts only this Mac-local environment; [runbook](../operations/local-postgresql-setup.md) records grants and Keychain locators. This does not connect Kordra product source, accept ADR-0002, or choose product persistence/messaging. Credentials remain in macOS Keychain.

### Synthetic IAM policy proof — 2026-10-18

The owner-authorized FEAT-IAM-001/I1 pure-policy PoC is locally completed and accepted through `KORDRA-OP-P0-IAM-POLICY-CONT-002`: 66 passing synthetic policy cases, 133 product/141 tooling tests, formatting, exact native High execution/design/post-review and contributor triage. [Canonical evidence](../features/identity-access/features/authorize-resource.md#coordinator-acceptance--2026-10-18). Earlier I0 empty test mappings are historical; only the test mapping is populated for FEAT-IAM-001. FEAT-IAM-002 T01 persistence is separately implemented and locally verified; authentication, authorization enforcement and protected inventory access remain incomplete.

The live exercise also repaired parent-lock re-entry in the existing AMR runner, with regression coverage and stable independent High review. Earlier incomplete attempts remain historical; no automatic replay or fabricated completion occurred. Native terminal-failure diagnostics have a residual limitation recorded as DEBT-AMR-004.

At this proof checkpoint the narrow ADR-0003 decision was pending; the human acceptance below supersedes that decision status only. Full GOAL, OP-P0, real login, provider reads and remote CI remain incomplete. No publication or infrastructure operation occurred.

### Narrow ADR-0003 accepted by maintainer — 2026-10-18

The human explicitly approved the reviewed [ADR-0003 baseline](../decisions/0003-tenant-authorization.md): permission/trusted resource scope, deny-by-default, expiry/revocation and platform/tenant separation only. The earlier waiting-for-ADR-0003 boundary is resolved. The 66-case synthetic proof remains test-only; broader IAM module scope and ADR-0009 remain proposed. ADR-0012 later accepts the specific local identity/session and authority Foundation design for implementation; positive inventory/provider access remains separately gated.

The [FEAT-IAM-001/I2.7 register](../features/identity-access/features/authorize-resource.md#i27--owner-reconciliation-and-decision-register-proposed) records the earlier behavioral and ownership proposal across TEN, PRJ, INV and AUD. The maintainer later accepted [ADR-0012](../decisions/0012-operator-pilot-runtime-foundation.md), local-authentication-v1 and authority/admission-v1 for the exact Foundation T01–T07 implementation scope. Luna may begin those slices in dependency order, with source/test evidence and deterministic gates still required. T08–T14, real provider access, production operations and ADR-0009 adoption remain separately gated; the prior I2.7 approval does not complete F0-C, M1-A, OP-P0 or the full feature.
