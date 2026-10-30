---
id: PROD-ROADMAP
status: proposed
owner: unassigned
last_reviewed: 2026-10-27
---

# Delivery roadmap & architecture maturity

## Current T11 design boundary — 2026-10-27

T10 PR #19 was human-merged at main bc91c2a66abcdb2156363f5fdbd3dc7f89046e67. Reviewed head 5969786f910081fa4df2c0ce808f5e8c551b7b88 passed docs-integrity/artifact-domain/clients-pin and automatic Codex review with no findings. Local evidence is 218 tooling and 1,159 Product passes, with one separately scoped live Nomad skip; this does not qualify a live registry or the whole Operator Pilot.

T11 is now a bounded proposed package: [ADR-0005](../decisions/0005-immutable-release.md) and [Release and approval v1](../contracts/release-approval-v1.md), with APR policy → REL immutable release intent → APR queue/decisions. Source remains unimplemented pending exact human approval. Qualification is Development/Test pilot only and executionAuthorized=false; approved intent is useful review history, not deploy authority. T12/T13 still need full execution-plan, durable worker, provider and operational gates. No infrastructure setup, HA/HPA/placement expansion is adopted by T11. Earlier dated status paragraphs below are historical.

## Historical T10 decision boundary — 2026-10-25

T09 Nomad and Kubernetes source is now present on main b70def19f69d460d4d9dcac099fa46a56978718d after #18; Kubernetes live T09.1e, Helm, OBS and complete OP-P0 remain separately qualified. The next T10 deliverable is a digest-first project artifact catalog for the selected private OCI/Docker Distribution registry. [ADR-0018](../decisions/0018-project-artifact-registration.md) / [Artifact registration v1](../contracts/artifact-registration-v1.md) specify proposed T10.1a–f and ART10 acceptance, including required IAM/SEC extensions. Exact design approval precedes implementation; none is implemented by this document. T11 release/approval and later deployment/worker decisions remain their own gates. Existing earlier dated progress below is historical.

## T08 credential design ready for owner decision — 2026-10-23

The owner selected macOS Keychain for the local pilot. [ADR-0015](../decisions/0015-credential-boundary-preparation.md) and [Credential boundary v1](../contracts/credential-boundary-v1.md) supply the proposed exact T08 architecture/acceptance package. Status is proposed, not implemented or accepted: offline binding lifecycle, current SEC evidence at U/C/D and bounded native child access must be approved and then proven. No real Keychain item, provider/cluster connection or T09 adoption is authorized. See [handoff](../engineering/luna-implementation-handoff.md#t08-exact-design-handoff--2026-10-23).

## T07 bounded-read increment — 2026-10-23

The local T07 slice adds an observation-only INV reader port and controlled-adapter/actual HTTPS proof over real test IAM/TEN/PRJ/INV/policy/AUD state. It preserves the existing metadata v1 API and keeps fake wiring in tests. See [T07 traceability](../engineering/traceability.md#t07-bounded-protected-read--2026-10-23) for tested scope and final gate evidence. This closes the bounded fake-adapter proof only; T08 credential custody and T09 real Kubernetes/Helm resources/provider/freshness remain separately gated. Whole-workspace management and full Foundation/Operator Pilot acceptance remain pending. Earlier progress statements below are historical.

## T06 local policy/admission increment — 2026-10-23

The current local T06 branch implements accepted ADR-0014 resource policy plus existing admission v1: exact current owner/grant/policy evidence, unique explicit bindings/history, persisted clock floor, durable U/D with bounded actual HTTP handoff, offline policy inspect/apply/reconciliation and two scoped HTTPS routes (metadata GET and environment-create v2). See [traceability](../engineering/traceability.md#t06-scoped-runtime-implementation--2026-10-23) and [runbook](../operations/resource-policy-v1.md) for exact scope. Local deterministic evidence is separate from hosted CI/PR review and real installation configuration; this note does not assert current remote PR/merge state. The historical delivery statements below remain historical. Wider workspace provisioning, list/history/other catalog routes and T07–T09 provider/credential capability are not completed by this slice, and the full Foundation/native GOAL is not marked complete.

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
| MOD-IAM | FEAT-IAM-001 / FEAT-IAM-002 | T01–T03 persistence/audit/local authentication merged at main 25bd7e7; T04 role-definition owner implementation is local with targeted tests; T06 protected authorization enforcement remains pending |
| MOD-TEN | FEAT-TEN-001 | T04 local owner persistence/handlers and targeted PostgreSQL tests; no HTTP routes or real provisioning; T05/T06 wiring pending |
| MOD-PAD | FEAT-PAD-001 | Not implemented; scoped support/elevation policy open |
| MOD-PRJ | FEAT-PRJ-001 | T05 local catalog/lifecycle/classification/policy/association owner implementation and PostgreSQL tests; T06 production adapters/HTTP pending |
| MOD-INV | FEAT-INV-001 | I1 local loopback API and inventory contracts implemented/verified; T05 target metadata/atomic association is locally implemented; real Kubernetes/Helm reads remain unimplemented |
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

### T03 PR #10 corrective work — 2026-10-22

The six findings on reviewed commit `79a6ee72af8f0240c941074e183f0ca1a284309b` have scoped local corrections and regression coverage in FEAT-IAM-002. Initial IAM verification passed 72 cases; final full gates bind the final local source independently. Fixes are not yet published or independently confirmed on a new PR revision. T03 remains pending that boundary and maintainer acceptance; do not advance T04–T07 from a green local test result or the AMR bookkeeping repair alone.

## T04 local owner implementation — 2026-10-22

IAM definition and TEN membership/exact-scope assignment owner slices are implemented locally on codex/foundation-t04-definitions-tenancy from the human-merged T03 baseline 25bd7e7. See [traceability](../engineering/traceability.md#t04--iam-definitions-and-ten-ownership--2026-10-22) for tests and remaining integration/publication status. No new AI review or PR action was initiated. T05 catalog/target ownership comes next; T06 policy/enforcement remains mandatory before positive HTTP ingress. This is Foundation progress, not deployable Operator Pilot or Beta acceptance.

### AMR coordination evidence — 2026-10-22

The stopped T03 checkpoint is reconciled as failed with its prior snapshot and findings preserved; it no longer holds an active coordinator claim. T01/T02 remain merged through PR #9. PR #10 contains the six corrective paths at `fcad37f28c468c02805916252f785eec1d559f92`, with exact-tree 246 Product/168 tooling test evidence. This supersedes the earlier pre-publication note above, but not independent PR follow-up, human merge or future feature gates. Publishing AMR tooling neither replays Product work nor completes T03.

## T05 local owner progress — 2026-10-22

PRJ catalog and INV metadata/two-owner association slices are locally implemented under accepted ADR-0012, with 28 targeted PostgreSQL cases and four architecture tests passed. See [T05 traceability](../engineering/traceability.md#t05--prj-catalog-and-inv-target-association--2026-10-22) for exact full-gate/publication evidence and limits. T06 current authorization/policy/workspace composition and U/D HTTP enforcement remain the next dependency. No runtime migration, real provisioning, provider read or Operator Pilot/Beta readiness claim.


## T08 design accepted; implementation pending — 2026-10-23

NukeGeng accepted the exact ADR-0015/Credential boundary v1 package and separately permitted its new isolated synthetic-Keychain fixture. The accepted Developer handoff covers T08.1–T08.5. This supersedes pending-design decision wording above only for T08; SEC source/native fixture proof still does not exist. No existing credential or real provider/cluster operation is authorized, and T09 remains separately gated.

### T08 local implementation scope — 2026-10-24

The approved local credential boundary now has SEC metadata/offline lifecycle, current U/C/D admission and a bounded native Mac Keychain child, with synthetic owner/HTTPS/process/native tests mapped in FEAT-SEC-001. Exact final source-bound gate evidence is private under .amr-local/t08/implementation/. The native fixture is newly created, isolated and deleted; existing secrets and real targets are excluded. Product metadata routes remain credential-independent and no credential network consumer is registered. This delivers the narrow T08 implementation when its applicable local gates pass; it does not complete OP-P0, the broader credential MVP or T09. Installation apphost/ACL/locator provisioning and approved Kubernetes/Helm provider design remain separate work.

### T09.1 workload-first design gate — preparation before acceptance, 2026-10-24

T07/T08 PR15 head ef42551 passed its three hosted checks and was human-merged as main 40acd58; separate main-branch CI is not claimed. The owner selected Kubernetes workloads by namespace before Helm release inventory. [ADR-0016](../decisions/0016-kubernetes-workload-observation.md) and its [contract](../contracts/kubernetes-workloads-v1.md) are proposed, awaiting exact approval of provider/trust/transport/freshness changes. No T09 adapter, actual observation, health result or live access is delivered by this design; wider OP-P0 is not complete.


Accepted scope update — 2026-10-24: NukeGeng approved exact ADR-0016 and Kubernetes workloads v1 for T09.1a–d, including the post-C managed-header exception, offline private-PKI residual, exact Namespace identity GET and additive config/scope/freshness caps. This supersedes pending-design approval wording in this section only. Runtime implementation and K09-01–12 verification remain pending. T09.1e, existing secrets, real clusters, Helm and OBS runtime remain excluded; historical checkpoints are preserved without replay.

### T09.2 Nomad personal lab and accepted provider design — 2026-10-24

After the owner reported a company move to Nomad and no company credential, the requested personal 1-vCPU/2-GiB/15-GiB Lima VM was created with Ubuntu ARM64 and Nomad 2.0.7 TLS/ACL. Direct local API probes and a completed synthetic batch allocation provide lab-only evidence; no Kordra provider is implemented. [ADR-0017](../decisions/0017-nomad-workload-observation.md)/[Nomad workloads v1](../contracts/nomad-workloads-v1.md) is the exact T09.2a–d design accepted by NukeGeng on 2026-10-24; Product implementation remains pending. Kubernetes ADR-0016 stays accepted and unimplemented, with accepted sequencing behind Nomad. T09/OP-P0 completion, company access and broad deployment remain unclaimed.

### T09.2a implementation — 2026-10-24

Local code now implements Nomad trust/configuration and compatible IAM config/scope/freshness primitives under accepted ADR-0017. The targeted 40 trust/58 admission tests pass without skips. T09.2b transport/projection, T09.2c composition and T09.2d complete conformance remain pending. Full gate evidence is separate; no T09/OP-P0 completion or live Kordra observation is claimed.

### Active T09 Nomad implementation and own-lab conformance — 2026-10-24

T09.2a–d code, synthetic transport/projection and actual-owner U/C/D HTTPS tests are implemented locally under accepted ADR-0017. T09.2e own-lab conformance passed with the authorized new lab token/new UUID isolated Keychain/native helper and real Nomad; temporary store and test-owner bindings were cleaned up. Required final gates/PR/CI are tracked separately; no merge or deployed permanent installation is implied. Kubernetes T09.1 remains accepted/deferred and Helm/OBS health/wider OP-P0 are not complete. This completes the active Nomad source/conformance scope, not the entire product vision.

## T09 provider implementation status — 2026-10-24

FEAT-INV-001 with IAM/SEC/AUD now implements accepted ADR-0016 T09.1a–d in existing projects: immutable private namespace trust, pinned bounded six-GET transport, Pod/Deployment/StatefulSet/DaemonSet projections, configured workloads route and current U/C/D with freshness caps. Nomad T09.2 remains a separate adapter; metadata and legacy authority serialization are preserved. Exact source/test mappings are in the INV owner feature, wire artifacts in Kubernetes workloads v1 and operator custody/rollback in the runbook. Current-session evidence is private under .amr-local/t09/kubernetes/; final gates and PR/CI/review identities remain distinct. No checkpoint replay, local AI review, new package/project, Clients change, production operation or ADR approval is performed. Kubernetes live T09.1e, Helm/OBS health and complete OP-P0 remain outside implementation acceptance; do not report the entire broader T09 vision as qualified.

## T10.1a–f approved for implementation — 2026-10-25

NukeGeng accepted the exact ADR-0018 / Artifact registration v1 proposal. This resolves the current T10 design gate above, with ART/registry slice custody and IAM/PRJ/SEC/AUD dependencies. Next is T10.1a, followed by covered slices b–f; implementation/ART10 execution/PR/merge remain pending. T10.1g and T11–T14 keep their own target/design gates. No repeat Sol review or design approval is required for the already-covered scope.

## T10.1a local code progress — 2026-10-25

The approved ART identity/store/migration slice now has local source and targeted evidence (111 Domain, 31 owner persistence/boundary, 6 architecture cases; zero-warning build). Final applicable gates remain separate current-source evidence. This is not completed artifact registration: project authorization, registry credential scope, bounded provider verification, API/disclosure/recovery and conformance T10.1b–f remain next. No live registry or real installation migration is enabled.

## T10.1a–f local implementation — 2026-10-27

The approved project artifact catalog, registry verification, current authorization, local HTTPS surface and stopped recovery are implemented in the feature worktree. The local gate passed 218 tooling and 1,158 Product tests; one live Nomad test was skipped. The [contract evidence map](../contracts/artifact-registration-v1.md#t101f-local-conformance-evidence-map--2026-10-27) describes test coverage. Feature PR CI, Codex Auto PR Review and the maintainer's merge decision remain pending; this is not a statement that the feature is on `main`. Live registry qualification T10.1g and later release/deployment features require their own approved scope.

## T11 exact owner acceptance — 2026-10-27

NukeGeng accepted ADR-0005, Release and approval v1 and FEAT-APR-002/REL-001/APR-001 for T11.0–T11.3. This supersedes pending-approval wording in the earlier T11 proposal, not historical evidence. Exact pre-annotation hashes and private acceptance receipt are recorded in ADR-0005. T11 runtime implementation and validation remain in progress; no deployment, real database/secret/target operation or merge is authorized by this design acceptance. The user explicitly selected gpt-6.1-terra/xhigh for T11; actual turn metadata and native model catalog confirm this profile.

## T11.0 local policy implementation — 2026-10-27

FEAT-APR-002 is implemented locally under accepted ADR-0005: exact approval policy/manifest, neutral owner seams, least-privilege migration and guarded offline policy transaction/reconciliation. The current-source full gate passed 218 tooling and 1,220 Product cases, one separate live Nomad skip; hosted review/CI and human merge are pending. The prior intermittent synthetic Keychain failure passed on the subsequent full run but has no confirmed root cause. T11.1 Release, T11.2 Approval decisions and T11.3 milestone conformance remain outstanding. Real installation operations are separately authorized.


## T12 design entry — 2026-10-30

T11 PR #21 đã merge thành main `69c5d86f3bc85a23636a475a8785543f1641df84`; reviewed/tested PR head `384449c8f07998add18f32078ceaf34e59e8cafc` có ba CI checks đạt và native Codex follow-up không có finding. Đây không phải chứng nhận deployment hoặc production readiness. [Thiết kế T12 tiếng Việt](../engineering/t12-design.vi.md) và [ADR-0019](../decisions/0019-durable-transport-workflow-bootstrap.md) hiện proposed: PoC MSG/EF/PostgreSQL/Wolverine đi trước adoption MSG/WFL/ControlWorker và distributed authority. Chưa triển khai, cài dependency, duyệt ADR hay thay đổi hạ tầng.


## T12.0 scoped approval recorded — 2026-10-30

NukeGeng duyệt ADR-0019 đúng phạm vi compatibility/fault PoC, candidate pins Wolverine 6.59.0, EF/Npgsql hiện có và test-only UUID fixtures trong kordra_test. Exact proposal hashes/approval receipt được lưu riêng; ADR-0019 được accepted cho scope đó, chưa triển khai và chưa có kết quả compatibility. Toàn bộ T12.1–T12.4, distributed authority, runtime ControlWorker và real-host/provider operations vẫn gated. Case runtime least-privilege chưa thể coi đạt bằng database-owner role; không có quyền tạo role bổ sung. Phiên Sol High hiện tại chỉ ghi nhận approval/handoff, không thực thi Developer task.


## Final local validation — 2026-10-30

Canonical `gates --base 69c5d86f3bc85a23636a475a8785543f1641df84 --product-test-timeout 600 --validation-context <private-nonsecret-epoch>` passed with stable source: docs, scope, 220 tooling tests, Clients pin metadata only, locked restore, Release build, serial Product tests and source mapping. Product result: 1,594 passed, zero failed, one existing T09 live Nomad test skipped; all 34 T12.0 tests passed with zero skips. No existing Keychain item, VM or real provider was accessed to replace the skipped test.

The full PoC implementation is locally validated. Hosted CI, exact PR review evidence and human merge remain separate. T12.1–T12.4/runtime adoption is still unapproved. Earlier partial/failing receipts remain retained, including default-schema escape, initial unknown-type fixture error, missing graph entry, missing synthetic fixture-root environment and the 300-second serial-suite timeout. The corrected final run used the approved isolated synthetic Keychain root, explicit bounded timeout and a rotated fixture epoch after interruption. Existing historical GOAL checkpoints were not rewritten or replayed. A pending bounded CLI plan is not represented as completed native execution for the user-authorized Medium session; actual coordinator/model/source/test receipts are retained separately.
