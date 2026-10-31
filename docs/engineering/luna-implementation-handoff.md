---
id: ENG-LUNA-IMPLEMENTATION-HANDOFF
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
---

# Luna Max implementation handoff — approved direction, decision-gated execution

## Current T11 design handoff — 2026-10-27

Baseline main bc91c2a66abcdb2156363f5fdbd3dc7f89046e67 contains human-merged PR #19. Reviewed T10 head 5969786f910081fa4df2c0ce808f5e8c551b7b88 passed all three required CI checks and one native automatic Codex review without findings. This supersedes historical starting-base/pending T10 publication language below; it does not rewrite historical evidence.

[ADR-0005](../decisions/0005-immutable-release.md), [Release and approval v1](../contracts/release-approval-v1.md), FEAT-APR-002/REL-001/APR-001 are proposed exact T11 design, not implementation authority yet. Human decision must include release-intent-v1 (executionAuthorized=false), Development/Test-only qualification, explicit independent/self_allowed business policy, lifetime/window caps and new scoped release permissions. No T11 product source exists. Native Architect context for this proposal was gpt-6.1-sol/high, turn 01a17b39-eae6-7341-9da3-bbec9117958f; no Developer model is inferred from it.

After exact acceptance, record the approved content hashes and proceed in the existing single-agent AMR coordinator without another Sol design/review loop:

1. T11.0 / FEAT-APR-002: real APR policy persistence, stopped inspect/apply/reconciliation, neutral PRJ environment port and exact IAM permission catalog. Complete policy tests/migration/ACLs/docs and feature PR first. No fake production positive policy.
2. T11.1a–b / FEAT-REL-001: neutral ART/PRJ/INV owner ports, REL core/migration/exact canonical bytes, then actual guarded HTTPS preview/freeze/retire/read and stopped receipt recovery. Complete REL feature commit/PR.
3. T11.2a–b / FEAT-APR-001: real REL dependency, requests/immutable decisions/current eligibility, then guarded queue/detail/grant/reject/revoke and recovery. Complete APR feature commit/PR.
4. T11.3: source-bound R11-01–16 conformance, compatibility, exact gates/CI and Codex Auto PR Review; human merge separately. No additional AI milestone review.

For each slice declare exact planned owner paths from the feature, current actor/permissions/scope, targeted tests and accepted contracts before code. Use own Domain/BCL Contracts/runtime, existing locked EF/Npgsql only and grouped Host folders. Map actual files only after creation. Preserve private historical checkpoints; use existing reconciliation/continue mechanisms where eligible, never replay old work or manufacture a completed native checkpoint. Reverify actual Developer model/effort and separately authorized fixture identity. No real installation migration/provisioning, provider/secret access, dispatch, Client edits or production action follows from design approval. Publish fixtures/OpenAPI/runbook with each real feature; planned tests are not evidence. Remaining T12/T13 need separately approved execution plan/worker authority.

## Historical Foundation readiness and authority

Overall direction and the exact ADR-0012 Foundation scope were approved by the maintainer on 2026-10-20. ADR-0012, local-authentication-v1, authority-admission-v1 and owner slices T01–T07 are accepted for implementation. The delivery design and roadmap retain the full objective and progress state; this handoff is not a second executor/checkpoint or permission to access infrastructure. T08–T14 remain separately decision-gated.

Current source base `63a7951f48b4441e880defaf315c3755ff79842b` plus uncommitted I2.7 documentation. Preserve existing 8 dirty documentation files and completed private checkpoints. The separate main checkout/clients setup branch is not this worktree. Reconcile fresh Git/PR/CI state at implementation start; no merge state or review result is asserted by this design.

Native catalog discovery advertises `gpt-6-luna/max` and `gpt-6.1-sol/high`. This Architect turn was observed as `gpt-6.1-sol/high`, turn `01a157fa-b2ef-7163-990f-9ff1b5f1776c`, in the current thread's native turn context. Effective CLI config default `gpt-6-luna/max` is not proof of the next desktop turn. After the human selects Luna, verify its own invocation-bound model/effort before code; never silently substitute. Sol ends after design; no additional local AI review or PR side effect is part of this task.

## Task dependency order and acceptance

Statuses here are readiness rules, **not execution evidence**. Each row belongs to an existing owner feature or new FEAT-IAM-002; selected source roots are planned scopes, not frontmatter mappings. Every task's plan must list exact files, actors, catalog permission/resource scope, accepted decisions and targeted tests before dispatch.

| Task / existing milestone | Owner / dependency | Bounded implementation deliverable and acceptance | Design gate |
|---|---|---|---|
| T00 — record approved design handoff | Accepted ADR-0012 and T01–T07 design package | Preserve prior receipts, record approval hashes and reconcile affected docs; no source or infrastructure changes | Completed by this bounded documentation handoff; no PR action implied |
| T01 — persistence and source boundary | IAM-002; ADR-0012 accepted | Pin/restore approved packages; offline additive owner migrations; no runtime DDL; actual graph tests; clean isolated DB roundtrip and runtime DDL denial | Exact ADR-0012 + local auth/admission contracts; explicitly authorized test database identity before DB writes |
| T02 — audit foundation | AUD-001; T01 | AUD append/idempotency/query port; state+audit transaction enlistment; no circular IAM callback; table-level update/delete denial; ACK-loss lookup and conflict tests | ADR-0012; AUD authority-admission v1 section |
| T03 — local account/session | IAM-002; T01,T02 | Offline enrollment/recovery, login/CSRF/me/logout/password, startup session invalidation; introduce the one technical self/authentication admission gate; real HTTP+DB/race tests AUTH-01–09 | Authentication v1 accepted; fixture certs for tests; maintainer provisions real local TLS/private locators separately |
| T04 — definitions then tenant/assignment | IAM-001 definition port, then TEN-001; T03 | Versioned IAM role-definition lookup/interpretation followed by TEN membership/exact scoped assignment; no grant beyond grantor and no membership authority in cookies | ADR-0012 assignment ownership and fixed catalog permission slice; separate owner tasks |
| T05 — catalog and target ownership | PRJ-001, INV-001 separate tasks; T04 | Persist project/application/environment, target identity and explicit two-owner associations; no cross-tenant links, classification downgrade or provider reads | ADR-0012 plus owner Foundation slices and authority-admission v1 command/query DTOs |
| T06 — authorization and admission | IAM-001; T02,T04,T05 | Owner facts, complete policy/deny, writer registry, fair gate, U/D audit; all interleavings including actual protected-response handoff | ADR-0012/admission v1 accepted; unproved protocol keeps inventory denied |
| T07 — bounded protected read | INV-001; T06 | One target.read use case through instrumented fake adapter and real HTTP enforcer; private bounded result; no fake registered in production | Owner target.read DTO/allowlist; this validates enforcement, not real OP-P0 |
| T08 — credential boundary | SEC-001; T06 | Opaque per-target profile/reference/binding and rotation/revoke semantics; resolve only for approved consumer; no browser secret | SEC provider/key-custody/TLS/bootstrap/RBAC decision before actual provider dependency or secret read |
| T09 — OP-P0 Kubernetes/Helm | INV-001, OBS-001; T07,T08 | Adapter with exact resources/fields, trusted provider identity, freshness/timeout; fake conformance then separately authorized live read; client fixtures | Provider SDK/CLI/version and safe operation/field allowlist approved; no real LAB target implicit |
| T10 — artifact registration | ART-001; T05,T06,T08 | Existing digest plus tenant/project provenance, registry checks, immutable record and duplicate handling | Registry/SSRF/credential policy and persistence contract accepted |
| T11 — policy, release, approval | APR-002 → REL-001 → APR-001; merged T10 | Exact T11.0–T11.3 in Release and approval v1; immutable release-intent, explicit queue/decisions and no execution grant | Exact ADR-0005/contract/three-feature acceptance pending; future worker/execution separate |
| T12 — durable transport/workflow | MSG-001 then WFL-001; T11 | Atomic state/outbox, inbox, persisted recovery/Unknown path, separate ControlWorker and Mac-close proof | ADR-0001/0002 scoped adoption, package pins, worker identity/authority, lease/recovery protocol accepted |
| T13 — Helm sandbox deployment | DRV-001, DEP-001, OBS-001 separate tasks; T12 | Typed plan/execute/observe, provider-safe writer exclusion, verified deployment/history, separately authorized rollback | ADR-0004/0006 and exact provider conformance design; approved isolated target, immutable spec, rollback and operational approval |
| T14 — release qualification | Applicable features; T09,T13 and actual client integration | Clean second install, migration/restore and failure drills, supported matrix, license/publication decision and Beta checklist | No Beta/production claim from local tests; no cloud/Enterprise expansion by default |

T01–T07 are approved for Luna implementation in dependency order. Each still needs its own task plan, exact path/actor/permission/test scope and deterministic evidence; approval is not completion. T08–T14 retain their explicit provider/distributed-execution adoption decisions. Authentication implementation need not wait for Helm choices. Backup/capacity mutations and advanced workflow editor require their own owning features; neither is hidden work inside inventory or messaging.

T04/T05 initially deliver owner data/handlers/contracts and isolated tests; their HTTP routes stay unregistered until T06 supplies real trusted policy/enforcement. Never register a permissive placeholder authorizer to make CRUD look complete. T06 wires guarded mutation/query endpoints, using the same technical gate introduced by T03. Split T04/T05/T11/T12/T13 into owner-scoped child tasks in the existing plan; preserve these dependency labels as milestone groups, not one giant multi-owner commit. Shared composition changes declare all affected consumers.

## Planned writable roots by owner

| Owner | Candidate roots, only create on the task that needs them |
|---|---|
| IAM-002 / IAM-001 | `src/Modules/IdentityAccess/`, `tests/IdentityAccess.Tests/`; BCL-only Contracts where needed |
| AUD-001 | `src/Modules/AuditCompliance/`, `tests/AuditCompliance.Tests/` |
| TEN-001 | `src/Modules/TenantManagement/`, `tests/TenantManagement.Tests/` |
| PRJ-001 | `src/Modules/ProjectEnvironment/`, `tests/ProjectEnvironment.Tests/` |
| INV-001 | Existing `src/Modules/InfrastructureInventory/`, owner tests; preserve bootstrap contract and explicit compatibility |
| Composition / migrations / technical admission | `src/Host/Kordra.Api/` remains unchanged; `src/Host/Kordra.DbMigrator/` implements only the explicit IAM T01 migration host; `src/BuildingBlocks/Security/` remains proposed; architecture tests are mapped only where needed |
| Later owners | Their current reserved module/Execution/Host roots; exact project/package list approved before creation |

Root solution/build/CI changes are shared paths, explicitly declare every consumer and owner. Do not move Artifact projects, create all module projects at once, add a second AMR tool or touch clients/submodule/Website to satisfy a Product task.

## Validation, checkpoint and PR boundaries

- Preserve historical checkpoints and execution receipts without replay. Create a fresh reconciled dependency-ordered AMR plan after design approval; mark later decision-gated tasks ineligible. This Markdown table is not a claimed initialized or completed plan.
- During implementation use targeted meaningful tests; at feature/PR boundaries run required deterministic gates, including `python3 docs/tooling/amr.py gates --base <verified-base-SHA>`, actual DB/HTTP/architecture checks as applicable, strict doccheck and source ownership. Do not mutate real development/company state as a test.
- Runtime account/grant/audit tests use synthetic records in an explicitly scoped isolated test database. Verify identity and permission before destructive cleanup; never reset all PostgreSQL databases, stop unrelated services or kill Website containers/processes.
- Store exact commands/results/source identities and populate source_paths/test_paths only after files exist. Planned assertions do not count as passing evidence. Existing 48 Artifact + 85 bootstrap tests include the 66 policy proof cases.
- Luna owns feature commits/push/PR within standing authorization; Codex Auto PR Review handles independent review after CI, with one default/max three BIR rounds and no unchanged revision rerun. Sol is not the default reviewer, does not trigger review comments and does not run continuous GOAL.
- Human maintainer decides detailed architecture adoption, merge and sensitive operations. A blocked finding, failed applicable gate or exhausted review budget does not approve anything.

## Design validation evidence — 2026-10-20

Bounded Architect author checks on the complete documentation package: `python3 docs/tooling/doccheck.py --strict-sources` passed (227 Markdown files, 209 IDs, 21 modules, zero errors/warnings); `git diff --check` passed; `python3 docs/tooling/changeguard.py --base 63a7951f48b4441e880defaf315c3755ff79842b --working-tree` passed with no source changes. The prior I2.7 checkpoint remains completed; it is not replayed or rewritten for this new design. Exact document hashes and native turn provenance are retained privately under `.amr-local/`.

These checks prove documentation structure/mapping only. No account, database migration, HTTP authentication, provider, concurrency protocol or new package was executed/validated by the design session. No routine Product suites, `amr.py gates`, independent AI review or GitHub PR review were run here; full applicable deterministic gates belong to Luna at implementation/PR boundaries under the Architect role separation. Do not reuse historical 133-product-test evidence as proof of the new design.

## Developer resume instruction

### Current T06 local runtime slice — 2026-10-23

The accepted policy design is implemented in the current `codex/foundation-t06-authorization-admission` worktree. Current owner specs and [traceability](traceability.md#t06-scoped-runtime-implementation--2026-10-23) map the policy/head/binding, floor, actor/grant/resource ports, U/D proof, guarded metadata/environment-v2 routes and offline policy CLI. Use the exact final `.amr-local/t06-preparation/` receipt/source identity; do not replay old checkpoints or reuse T03/T05 counts as T06 validation. Native model/effort is unverified; no extra AI/PR review was initiated. Existing unrelated `dotnet-tools.json` remains uncommitted and Clients/Website are untouched.

This delivers the scoped policy/admission increment, not all Foundation management capability. Whole-workspace symbolic resource/grant provisioning, list/audit-history disclosure and other catalog HTTP routes still need their accepted owner composition/proof. T07–T09 provider/credential gates remain separate; policy scope closure does not authorize live migration, actual CLI apply, provider access, merge or marking the entire GOAL completed. Preserve the narrower acceptance instead of claiming or inventing a broader delivered baseline.

### T06 resource-policy addition accepted — 2026-10-22

[ADR-0014](../decisions/0014-resource-policy-v1.md) and [Resource Policy v1](../contracts/resource-policy-v1.md) supply the bounded missing policy design, including the four requested clarifications. The maintainer accepted both exact documents for T06 on 2026-10-22. Follow contract section 10's IAM, PRJ, offline Host and U/D integration slices; preserve completed T01–T05 evidence and keep unresolved policy denied. The existing admission/session/clock protocol is not redesigned. Planned implementation remains in existing IdentityAccess/ProjectEnvironment owner contexts and Contracts, Host API/DbMigrator and corresponding owner tests; update INV/AUD consumers through ports, not new services or cross-owner tables. The contract lists actual current DTO consumers, legacy nullability migration, fixed rules/current head, explicit bindings and future versioned Product fixtures. Planned acceptance is not passing test evidence. No Sol PR action, extra AI review, provider access or operational migration is authorized by this handoff.

Design-only author validation: strict doccheck passed with 228 Markdown files, 210 IDs, 21 modules and zero errors/warnings; scopecheck passed with 39 unique LAB cases; whitespace checks and the JSON example parse passed. These establish documentation integrity only. No Product suite, database migration, AI review or PR operation was run for this bounded design task. Exact candidate hashes/snapshots are retained privately under `.amr-local/designs/t06-resource-policy-v1/`; acceptance is recorded separately in the ADR and private approval receipt.

### Existing Foundation resume baseline

Read GOAL/roadmap and this handoff in the existing Product worktree. Verify native gpt-6-luna/max for this turn. Preserve completed checkpoints, dirty design changes and accepted ADR scopes. ADR-0012, local-authentication-v1, authority-admission-v1 and Foundation T01–T07 were explicitly accepted by the maintainer on 2026-10-20; do not ask for that approval again. Reconcile the existing AMR plan/checkpoints and execute T01–T07 in dependency order, preserving per-owner scopes and all test/database safety boundaries. Do not auto-launch Sol or local AI review, change accepted architecture, operate live infrastructure, edit clients/Website, or merge.

The original T06 document-integrity issue is resolved: before this reconciliation, [FEAT-IAM-001](../features/identity-access/features/authorize-resource.md) matched the preserved pre-approval input and receipt at SHA-256 `88c2a3a0297ee94da793a440029e11435390f74af9c511f387232f85b6e4faaf`. Its I2.7a–I2.7g evidence remains intact. The acceptance receipt's embedded `91c...` recovery annotation describes the first reconstruction and remains historical; the recovery sidecar records the exact restoration. I then reconciled the feature's older open-decision wording against current ADR-0012, `local-authentication-v1` and `authority-admission-v1`, and added a post-acceptance crosswalk without changing their decisions. The current feature hash includes that annotation and is recorded in `.amr-local/recovery/t06-contract-reconciliation.json`; the immutable approval receipt and prior recovery evidence are preserved.

The crosswalk confirms: IAM consumes the current authenticated subject/session and interprets RoleDefinition; TEN owns membership/assignment; PRJ/INV provide authoritative ownership/lifecycle facts; AUD owns durable append; and protected use/disclosure follows the accepted single-process writer-preferring gate, U/D audit protocol, current-fact revalidation and bounded caller-bound HTTP handoff. Exact bounds and outcome semantics remain in the contracts. Historical I2.7 open-decision text is retained but is no longer operative where those accepted documents decide T01–T07. These decisions do not cover provider/credential access, distributed writers, support, streaming or production operations. T06 remains dependent on T02, T04 and T05 and must prove revoke/fairness/expiry/audit/actual HTTP handoff interleavings before positive ingress is enabled. T01–T05 may proceed only within their accepted slices and prerequisites. This reconciliation needs no repeat design approval and verifies no runtime or protected ingress.

The original feature hash is preserved in the approval receipt and recovery evidence. The current post-acceptance FEAT-IAM-001 crosswalk hash is `37cecb30122b785d693f27674d28a157100b799b0404efcf8dbe95bd4b56ec77`. The current accepted ADR/contract files have hashes different from the pre-acceptance receipt values; no raw pre-acceptance snapshots for those three documents are present here. The crosswalk therefore verifies the T01–T07 provisions in their current normative text and records the limitation rather than claiming byte identity. See the [traceability record](traceability.md#feat-iam-001--t01t07-contract-reconciliation--2026-10-20) and local reconciliation evidence.

## T04 continuation evidence — 2026-10-22

T03 was merged by the human maintainer at main 25bd7e71fbeb5370d19baac1878ab1609c7d72a3. T04 continues in the existing Product worktree/branch as two owner slices: IAM definition Contracts/store/migration/tests, then TEN membership/assignment Contracts/store/handlers/tests with host-only audit enlistment. The user explicitly authorized Sol Medium implementation for T04 only; default AMR responsibilities are unchanged. See the dated traceability entry for validation/publication status. No repeated Architect review or PR side effect was initiated. T05 and T06 remain pending, and T04 HTTP routes remain unregistered.

## T05 owner continuation — 2026-10-22

PRJ catalog and INV metadata/association owner slices are implemented locally with separate persistence/Contracts and one Host-composed owner/AUD transaction. 28 targeted cases and actual XML/PE/negative architecture checks passed; final gate results are in the dated traceability/private T05 evidence. T04 inputs are snapshotted for later commit separation; no historical checkpoint is reset or task replayed. Native model/effort for this resumed turn is unverified. No AI review/PR side effect or HTTP/provider/runtime migration was initiated. T06 still owns trusted adapters/current policy/workspace composition, endpoint authorization and real U/D output proof; these must not be replaced by the fixture adapters.


### T07 local implementation handoff — 2026-10-23

The approved bounded read now uses an INV-owned observation port with the same existing metadata v1 route/allowlist. The actual-owner HTTP fixture supplies a controlled test-only reader, normal durable U/D and response-write instrumentation. See [T07 traceability](traceability.md#t07-bounded-protected-read--2026-10-23) and final `.amr-local/t07/` gates for exact evidence; do not reuse T06 counts or replay blocked historical checkpoints. Product defaults to the real local registry reader; there is no production fake switch or provider access. T08/T09 need their separately recorded design/operational gates; no entire Foundation completion is implied.


### T08 exact design handoff — 2026-10-23

The owner selected macOS Keychain for the local Mac pilot and requested design completion. [Proposed ADR-0015](../decisions/0015-credential-boundary-preparation.md) and [Credential boundary v1](../contracts/credential-boundary-v1.md) now provide the exact T08 decision package, including SEC08-01–12 and slices T08.1–T08.5. Status remains proposed until exact human acceptance. Do not ask again which provider was preferred; do not infer design or operational acceptance from that preference.

After acceptance, implement SEC metadata/offline receipts and audit; wire neutral IAM SEC-dependency evidence and separate C before consumer use; implement the bounded native Keychain child in the same API executable; validate synthetic consumer/HTTPS and separately authorized isolated-Keychain fixture. Use the reserved ConnectionManagement module path. Follow the completed API responsibility-folder layout when adding Host files, integrating that separate branch first. Existing T07 observation/wire contract and historical checkpoints stay intact. T08 does not register a real network adapter or approve T09 TLS/provider identity/RBAC/dependencies/cluster access. No automatic Sol review, PR action or GOAL replay belongs to this handoff.


### T08 accepted Developer handoff — 2026-10-23

The maintainer approved the exact ADR-0015/Credential boundary v1 package and explicitly authorized the isolated synthetic-Keychain fixture described in contract section 10. Proceed with T08.1–T08.5 under that bounded design; do not repeat provider/design/fixture approval or request an additional Sol review. Keep metadata-only behavior and T07 receipts intact. Record actual fixture/executable identities before running and retain cleanup evidence. Accepted design does not claim implementation, native profile verification, migration, existing secret access, T09 provider adoption or human merge. The bounded design task ends at this handoff; Developer execution/checkpoints remain a separate scope.

## T08 implementation continuation — 2026-10-24

Accepted ADR-0015/credential-boundary-v1 T08.1–T08.5 is implemented locally on codex/foundation-t08-credentials in the existing foundation-t03 Product worktree. FEAT-SEC-001 and the dated traceability/runbook map actual code, consumers and synthetic tests. Keep the separate approval receipt, completed T07 and historical AMR evidence unchanged. Use the final source-bound .amr-local/t08/implementation/ gate receipt before publication; native model/effort is unverified. No local AI/PR review is part of this implementation.

Do not infer permission for existing Keychain items, real installation DDL or a cluster. No network consumer or credential HTTP management route is registered. Future activation needs the stable installed apphost/hash and explicit ACL/private locator configuration; T09 still needs provider/identity/TLS/RBAC design. The API folder cleanup is integrated by moving current implementations, preserving T07 additions; the primary worktree's staged cleanup and Clients remain untouched. Unrelated dotnet-tools.json is excluded from implementation ownership.

## T09.1 bounded Architect handoff — preparation before acceptance, 2026-10-24

PR15 head ef42551 had all three hosted checks passing and was human-merged as main 40acd58; this is not a claim about a separate main-branch CI run. The owner selected namespaced Kubernetes workloads before Helm release inventory. The native current turn is verified gpt-6.1-sol/high; [ADR-0016](../decisions/0016-kubernetes-workload-observation.md) and [Kubernetes workloads v1](../contracts/kubernetes-workloads-v1.md) are the bounded proposed design, not accepted implementation authority.

[NEEDS-DECISION]: exact approval must explicitly include the post-C transient managed-header exception, private-PKI/offline revocation residual, exact named Namespace GET bootstrap privilege and additive config/scope/freshness admission changes. Do not approve them from the scope preference alone. After acceptance, Luna can implement T09.1a–d in existing projects using the contract's exact planned files and K09-01–12; update owner mappings only when files exist. T09.1e live access, Helm/OBS health, Clients, runtime installation provisioning and the wider pilot remain separate. Preserve T07/T08 and original checkpoints without replay; no extra independent AI/PR review is part of this design.


Accepted scope update — 2026-10-24: NukeGeng approved exact ADR-0016 and Kubernetes workloads v1 for T09.1a–d, including the post-C managed-header exception, offline private-PKI residual, exact Namespace identity GET and additive config/scope/freshness caps. This supersedes pending-design approval wording in this section only. Runtime implementation and K09-01–12 verification remain pending. T09.1e, existing secrets, real clusters, Helm and OBS runtime remain excluded; historical checkpoints are preserved without replay.

Developer next action: start T09.1a using the accepted contract’s planned files and compatibility vectors, then T09.1b–d. No new design approval or Sol review is needed for those covered slices. The bounded Architect task ends here. Product GOAL checkpoints remain unchanged; this design receipt is not native GOAL completion.

## T09.2 Nomad continuation handoff — 2026-10-24

The owner authorized and received a personal lab; [local runbook](../operations/local-nomad-lab.md) identifies its exact scope/private artifacts. [ADR-0017](../decisions/0017-nomad-workload-observation.md) plus [Nomad workloads v1](../contracts/nomad-workloads-v1.md) were accepted by NukeGeng on 2026-10-24 for exact T09.2a–d adoption, preserving accepted T09.1 rather than relabeling Kubernetes code. Luna starts T09.2a now under the recorded acceptance, then b–d, under the planned N09 checks and existing single-agent AMR. No extra Sol review is requested. T09.2e is a separate exact new lab/Keychain integration scope.

The current bounded Architect/setup turn has no Product source changes, routine Product tests or PR actions. A read-only goal-status check reports task paths not mapped to owner feature for the retained historical checkpoint; do not replay completed tasks or falsely mark it valid. Inspect retained artifacts and prepare a mapped approved provider scope through the existing reconciliation/continuation workflow. Preserve all old checkpoints; this design/setup receipt is separate from GOAL execution.

## T09.2a retained implementation — 2026-10-24

The user explicitly permitted gpt-6.1-sol/medium for T09.2a–d, and native turn metadata verified that profile for this increment. T09.2a now contains INV trust/parser and compatible IAM admission/result caps; existing Host rejects config-qualified plans until the T09.2c registry. Targeted evidence: 40 trust/58 admission cases, no skips. Use the final source-bound gate receipt under .amr-local/t09/implementation/ before claiming complete validation. Next source slice is T09.2b transport/projection; do not replay T09.2a or reapprove ADR-0017. Historical checkpoint mapping diagnostics are retained, with no native GOAL completion fabricated.

## Active T09 Nomad source/conformance handoff — 2026-10-24

The explicit Sol Medium implementation exception remains in force and was verified from native turn metadata. T09.2b–d now has bounded transport/projection, immutable registry and guarded endpoint, private stopped-installation activation, exact schema artifacts and negative/current-authority tests. T09.2e personal case also passed actual native Keychain/U/C/D/live Nomad; its store and synthetic owner rows were removed. Do not replay these operations or request design approval again. Use the final private source-bound gates/completion receipt before claiming validation; PR/CI/human merge remain distinct. The old mapping-diagnostic checkpoint is unchanged and must not be falsely marked native-completed. The next Product feature needs its own already-approved scope; deferred Kubernetes/Helm and OBS health are not automatically enabled.

## T09.1 continuation implementation — 2026-10-24

FEAT-INV-001 with IAM/SEC/AUD now implements accepted ADR-0016 T09.1a–d in existing projects: immutable private namespace trust, pinned bounded six-GET transport, Pod/Deployment/StatefulSet/DaemonSet projections, configured workloads route and current U/C/D with freshness caps. Nomad T09.2 remains a separate adapter; metadata and legacy authority serialization are preserved. Exact source/test mappings are in the INV owner feature, wire artifacts in Kubernetes workloads v1 and operator custody/rollback in the runbook. Current-session evidence is private under .amr-local/t09/kubernetes/; final gates and PR/CI/review identities remain distinct. No checkpoint replay, local AI review, new package/project, Clients change, production operation or ADR approval is performed. Kubernetes live T09.1e, Helm/OBS health and complete OP-P0 remain outside implementation acceptance; do not report the entire broader T09 vision as qualified.

## T10 exact design handoff — proposed 2026-10-25

Current verified source base is main b70def19f69d460d4d9dcac099fa46a56978718d: Nomad #16 and Kubernetes #17 integrated through merged #18. #18 head f69e3081d41b60ba2e693b450108049ad04ccaf2 passed docs-integrity/artifact-domain/clients-pin and automatic Codex review; main's tree matches the reviewed final Kubernetes fix. This supersedes older provider implementation-pending notes above, without claiming Kubernetes live T09.1e, Helm, OBS health or whole OP-P0 qualification. The retained historical GOAL checkpoint is unchanged; this is not its replay/completion receipt.

Owner selected a private OCI/Docker Distribution registry. [ADR-0018](../decisions/0018-project-artifact-registration.md) and [Artifact registration v1](../contracts/artifact-registration-v1.md) are the reviewable proposed T10 package. Exact acceptance must cover ART persistence, new project permissions/fixed eligibility, typed SEC registry selection/global native-reference allocation, direct-auth/header/TLS residuals and transactional receipt/recovery semantics. NukeGeng is proposed slice custodian. Registry preference alone does not supply this approval; proposed release-execution v1/ADR-0005 stay unadopted.

Developer sequence is T10.1a store/identity → b current project authority → c SEC allocation/broker seam → d bounded verifier → e guarded API/recovery → f conformance/docs/feature PR, with the exact planned files and ART10-01–16 matrix in the contract. Preserve existing Domain tests and T01–T09 consumers; no fake authorizer, new runtime service, extra model session, broad refactor or Clients change. Configure no real registry and read no existing credential merely to make tests pass. Live T10.1g and new native fixture access need separate exact scope. Targeted tests during slices; required Product gates at implementation/PR boundaries; Codex Auto PR Review then human merge.

This bounded design changes Markdown only. It does not dispatch/accept a GOAL task, run Product suites/migrations/providers or trigger AI/PR review. Exact model evidence for this turn is gpt-6.1-sol/max, not a claimed high invocation; private receipts preserve it. Before implementation, verify the Developer turn's actual gpt-6-luna/max or an explicit scoped exception. Save proposal hashes/snapshots and document-check receipts privately under .amr-local/t10-design; human decision annotations must bind the reviewed content. Sol's bounded design ends at that handoff.

## T10.1a–f accepted handoff — 2026-10-25

NukeGeng approved exact ADR-0018 and Artifact registration v1 after the frozen proposal. All 14 proposal hashes matched before annotation; original ADR/contract hashes and the explicit decision are recorded in ADR-0018 and private .amr-local/approvals/adr-0018-t10/acceptance.json. This supersedes pending-approval language in the T10 proposal only. No design content changed; no covered-slice repeat approval/Sol review is needed. Start T10.1a with actual verified Developer invocation evidence and an owner-mapped current-session task. There is no T10 execution checkpoint yet: preserve the historical Foundation checkpoint, inspect its retained evidence once, and prepare an explicit approved T10 plan rather than relabeling/replaying it.

### Reusable Product Developer session instruction

Paste the following into the Product chat at the start/resumption of a Developer session; it is an instruction to the active agent, not a shell/native background GOAL command:

```text
Tiếp tục GOAL KORDRA Product theo AMR v4 trong phiên hiện tại.
Đọc checkpoint/evidence hiện có và handoff mới nhất; chọn task eligible tiếp theo có thiết kế đã duyệt. Nếu chưa có plan cho feature đó, tạo plan giới hạn, owner-mapped trong AMR hiện có; giữ nguyên lịch sử cũ, không reset/replay hoặc xin duyệt lại phần đã được duyệt.
Xác minh model/effort native của phiên Developer. Tự thực hiện tuần tự các task đã được duyệt: code, targeted tests, cập nhật tài liệu/checkpoint; chạy đầy đủ gate tại ranh giới validation/PR và tái sử dụng evidence chỉ khi còn hợp lệ.
Feature hoàn chỉnh thì commit theo feature, push và tạo PR theo workflow; không đưa phần dở dang/ngoài scope vào commit. Codex Auto PR Review là reviewer; xử lý findings theo BIR, không tự gọi thêm Sol review hoặc subagent. Human maintainer quyết định merge.
Chỉ dừng khi hết phạm vi đã duyệt, thiếu quyết định thiết kế/model chính xác, có blocker thực sự hoặc cần quyền cho thao tác nhạy cảm. Không tự duyệt ADR, mở rộng quyền, dùng secret/hạ tầng thật hoặc chuyển sang milestone chưa được duyệt.
Không thao tác Clients/Website. Báo task đã hoàn thành, checkpoint/evidence và bước tiếp theo.
```

For the next session the selected feature is FEAT-ART-001, accepted T10.1a–f; begin T10.1a. The instruction continues later tasks only when their own designs are approved. Local CLI supports step/finish/reconciliation, not an unattended multi-session loop: no goal run/--auto command exists. An active session must perform implementation between step and finish, generate real private context/evidence and honor the native model/user merge/production boundaries. Nothing here starts a scheduled job or promises execution after the chat/host stops.

## T10.1a retained local implementation — 2026-10-25

Authorized native Sol 6.1 Medium has implemented immutable Domain values/receipt candidates plus ART Contracts/runtime/store/migration/tests and offline command/graph. The original 48 Domain tests remain within 111 passing Domain cases; new owner/boundary tests pass 31/31, actual architecture checks 6/6 and Release build 0 warnings/errors. Use the current private persistence-progress/gate receipts after final formatting/docs, not a stale pre-graph test receipt. Store staging is not authorization or a confirmed commit; T10.1b current project authority, T10.1c SEC seam, T10.1d verifier and T10.1e/f API/recovery/conformance are still required. No real migration/provider read or PR has occurred.

The CLI child plan remains pending because current-session completion admits only Luna Max; actual Medium evidence is retained under .amr-local/t10-implementation without forging another profile or terminal receipt. This tool limitation does not revoke the owner's explicit Medium implementation authorization or require design approval again. Preserve the historical Foundation checkpoint; continue the covered source work, and reconcile supported checkpoint evidence honestly at finalization.

## T10.1b retained current-authority source — 2026-10-25

Accepted b scope now has project-only artifact actions, current PRJ owner lookup/Host adapters, IAM invocation-bound partial eligibility snapshots and a common capacity16 lease used by legacy protected reads. Targeted 100 IAM/admission/role, 4 real-owner PRJ and 31 synthetic T09 HTTP/schema cases passed. Use current private t10b evidence/gates at the validation boundary; old T10.1a gates are historical after these code changes. There is no artifact route or independent positive permit. Next T10.1c must add the typed SEC registry selection/global allocation and reuse the existing broker/helper slots; then verifier and actual guarded U/C/M/D/HTTP/recovery/conformance remain d–f. Do not ask design approval again or launch Sol review. Preserve the CLI pending/honest Medium evidence limitation instead of manufacturing a terminal receipt.

## T10 current implementation handoff — 2026-10-26

Use existing foundation-t03 worktree/branch and approved T10.1a–f; do not replay historical checkpoints or ask for covered design approval again. T10.1a–b and SEC metadata/broker/offline seam have source-bound local gates; latest full pre-verifier gates passed 218 tooling/924 Product, one explicitly excluded live Nomad case skipped. Verifier additions subsequently passed all 135 ART tests; full current-tree gates remain due at the final feature boundary. Added SEC conflicting-history migration case passed separately (three migration cases). Evidence is private .amr-local/t10-implementation/t10c-validation.json and t10d-validation.json; CLI goal finish is not fabricated for the owner's authorized Medium exception.

Next eligible scope is T10.1e: compose current project/session/TEN/IAM/registry/SEC evidence U→helper→C→provider→M→D, durable Pending/terminal receipts and atomic ART/AUD/floor under existing gate/capacity; exact own status/detail/list/selectors; stopped metadata-only recovery. Verify provider facts against original/current expiry/config/SEC revisions before M/D and preserve confirmed M if disclosure is denied. Use approved APIs/budgets and existing owner ports; no runtime startup DDL, task takeover/replay, permissive fixture in production or existing secret/provider access. T10.1f owns final conformance/fixtures/OpenAPI/runbook, complete feature commit/push/PR and native Auto PR Review; human merge remains separate.

## T10 current implementation handoff — 2026-10-27

The owner explicitly selected `gpt-6.1-terra` for this continuation; native turn metadata records `gpt-6.1-terra/xhigh`. T10.1e HTTP, synthetic authenticated Keychain integration and stopped exact-plan recovery have source and targeted evidence; inspect the latest private evidence instead of assuming completion. Finish the ART10-01–16 matrix against actual tests, remaining negative/compatibility cases, current full gates and feature publication. The CLI's Luna-only completion restriction is not evidence of another model or a reason to replay work. Preserve the historical checkpoint and unrelated drafts.

## T11 exact owner acceptance — 2026-10-27

NukeGeng accepted ADR-0005, Release and approval v1 and FEAT-APR-002/REL-001/APR-001 for T11.0–T11.3. This supersedes pending-approval wording in the earlier T11 proposal, not historical evidence. Exact pre-annotation hashes and private acceptance receipt are recorded in ADR-0005. T11 runtime implementation and validation remain in progress; no deployment, real database/secret/target operation or merge is authorized by this design acceptance. The user explicitly selected gpt-6.1-terra/xhigh for T11; actual turn metadata and native model catalog confirm this profile.


## T12 design entry — 2026-10-30

T11 PR #21 đã merge thành main `69c5d86f3bc85a23636a475a8785543f1641df84`; reviewed/tested PR head `384449c8f07998add18f32078ceaf34e59e8cafc` có ba CI checks đạt và native Codex follow-up không có finding. Đây không phải chứng nhận deployment hoặc production readiness. [Thiết kế T12 tiếng Việt](../engineering/t12-design.vi.md) và [ADR-0019](../decisions/0019-durable-transport-workflow-bootstrap.md) hiện proposed: PoC MSG/EF/PostgreSQL/Wolverine đi trước adoption MSG/WFL/ControlWorker và distributed authority. Chưa triển khai, cài dependency, duyệt ADR hay thay đổi hạ tầng.


## T12.0 scoped approval recorded — 2026-10-30

NukeGeng duyệt ADR-0019 đúng phạm vi compatibility/fault PoC, candidate pins Wolverine 6.59.0, EF/Npgsql hiện có và test-only UUID fixtures trong kordra_test. Exact proposal hashes/approval receipt được lưu riêng; ADR-0019 được accepted cho scope đó, chưa triển khai và chưa có kết quả compatibility. Toàn bộ T12.1–T12.4, distributed authority, runtime ControlWorker và real-host/provider operations vẫn gated. Case runtime least-privilege chưa thể coi đạt bằng database-owner role; không có quyền tạo role bổ sung. Phiên Sol High hiện tại chỉ ghi nhận approval/handoff, không thực thi Developer task.


## T12.0 two-process Developer evidence — 2026-10-30

The test executable now pre-generates Wolverine handlers in Static mode using only the approved package graph. Regenerate from `tests/Messaging.Durability.Tests` with `dotnet run -p:T12CodegenBootstrap=true -- codegen write`, then build normally; the bootstrap property excludes stale generated source only during regeneration. It does not bypass runtime tests. Native queue and envelope schemas are both explicitly UUID-scoped (see DRIFT-T12-001).

Real process probes verified normal delivery, producer crash before commit (no state/envelope), producer crash after commit before flush (outbox recovery), consumer crash before effect and after durable effect before ACK, restart, and different-envelope/same-logical-key dedup. Short fixture node-health/reassignment periods (1 s polling, 3 s stale timeout) are explicit; they are not production defaults. A loopback PostgreSQL wire proxy drops the server's actual COMMIT response: the client fails, while a new direct connection finds the committed logical key without replay.

Five rejection/retry cases passed before the later isolation/serializer changes: wrong case, tenant, schema version and expired deadline reach DLQ after one attempt with no logical effect; transient failure reaches DLQ after five attempts with measured 1/5/15/60 s delays. Current-content rerun remains required. Default inbox retention is explicitly 10 minutes; a separate shortened retention fixture must verify cleanup without manually deleting inbox rows. Closed fixture serialization rejects unknown fields (including synthetic secret fields), duplicate keys, wrong type, bodies over 16 KiB and depth over eight. These tests do not establish a trusted production service identity.

The corrected restricted-role probe starts Wolverine with AutoCreate.None, handles one synthetic message, denies DDL and leaves relation inventory unchanged. Exact role/schema/HBA cleanup passed. Private receipts/logs remain under `.amr-local/t12-design/`; the initial default-schema failure and residual-object debt are retained rather than erased.

Remaining acceptance: current-content fault/retry/serializer/retention results, consumer inbox/state/output transaction analysis, unknown-type quarantine behavior, final deterministic gates, residual-object disposition and complete-feature publication. Producer multi-context atomicity and synthetic effect dedup do not prove atomic production consumer state/inbox/output or distributed authorization. No runtime MSG/WFL/ControlWorker adoption or T12 completion is claimed.


## Final local validation — 2026-10-30

Canonical `gates --base 69c5d86f3bc85a23636a475a8785543f1641df84 --product-test-timeout 600 --validation-context <private-nonsecret-epoch>` passed with stable source: docs, scope, 220 tooling tests, Clients pin metadata only, locked restore, Release build, serial Product tests and source mapping. Product result: 1,594 passed, zero failed, one existing T09 live Nomad test skipped; all 34 T12.0 tests passed with zero skips. No existing Keychain item, VM or real provider was accessed to replace the skipped test.

The full PoC implementation is locally validated. Hosted CI, exact PR review evidence and human merge remain separate. T12.1–T12.4/runtime adoption is still unapproved. Earlier partial/failing receipts remain retained, including default-schema escape, initial unknown-type fixture error, missing graph entry, missing synthetic fixture-root environment and the 300-second serial-suite timeout. The corrected final run used the approved isolated synthetic Keychain root, explicit bounded timeout and a rotated fixture epoch after interruption. Existing historical GOAL checkpoints were not rewritten or replayed. A pending bounded CLI plan is not represented as completed native execution for the user-authorized Medium session; actual coordinator/model/source/test receipts are retained separately.

## T12.1 — bounded MSG kernel implementation, 2026-10-31

FEAT-MSG-001 / MOD-MSG implements accepted ADR-0020/Durable Messaging v1: closed contracts/wire, public caller-owned native transaction staging with actual AUD, immutable receipts/reconciliation, native bounded recovery/capacity/retention and explicit closed diagnostic outputs. AUD receives only its approved guarded test seam. Package pins, architecture isolation and empty business catalog remain as designed; no Product API/worker activation, new architecture decision or production authority. The [owner feature](../features/messaging/features/durable-delivery.md) records current conformance and limits.

Canonical local gates passed220tooling and1,770product tests with0failures; one separately scoped optional live-Nomad test skipped. Actual synthetic DB/Keychain/native process evidence and isolated restricted startup cleanup are retained privately, with source-bound receipts and original failed checkpoints unchanged. Final documentation reconciliation must rerun current docs/scope/mapping/tooling and reuse unchanged product evidence only through the deterministic one-hour gate policy. Hosted CI/native PR review and human merge remain pending publication gates. Incremental public draft notes were consolidated here; their complete prior contents/hashes remain in the private pre-final snapshot alongside progress checkpoints.

Continue from the latest private coordinator checkpoint and gate receipt. Do not replay tasks or re-request already granted fixture permissions. Keep user-owned docs/README.md, operational-intent-expansion-brief.md and dotnet-tools.json out of the feature commit. PR22 is merged at main8f07308; initial HEAD954c9e9 has the same tree. Reconcile Git ancestry without discarding working changes. Publish this kernel only after current gates, then rely on Codex Auto PR Review/BIR; no local AI review/subagent or automatic human merge.
