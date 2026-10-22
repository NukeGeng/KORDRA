---
id: ENG-LUNA-IMPLEMENTATION-HANDOFF
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-20
---

# Luna Max implementation handoff — approved direction, decision-gated execution

## Readiness and authority

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
| T11 — release then approval | REL-001 then APR-001; T10 | Frozen canonical spec/hash, explicit transitions, approval queue/reject/revoke/expiry and SoD; golden vectors/consumer fixtures | ADR-0005 and owner release-execution v1 adoption; no source-state-machine adoption from this proposed table alone |
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

Read GOAL/roadmap and this handoff in the existing Product worktree. Verify native gpt-6-luna/max for this turn. Preserve completed checkpoints, dirty design changes and accepted ADR scopes. ADR-0012, local-authentication-v1, authority-admission-v1 and Foundation T01–T07 were explicitly accepted by the maintainer on 2026-10-20; do not ask for that approval again. Reconcile the existing AMR plan/checkpoints and execute T01–T07 in dependency order, preserving per-owner scopes and all test/database safety boundaries. Do not auto-launch Sol or local AI review, change accepted architecture, operate live infrastructure, edit clients/Website, or merge.

The original T06 document-integrity issue is resolved: before this reconciliation, [FEAT-IAM-001](../features/identity-access/features/authorize-resource.md) matched the preserved pre-approval input and receipt at SHA-256 `88c2a3a0297ee94da793a440029e11435390f74af9c511f387232f85b6e4faaf`. Its I2.7a–I2.7g evidence remains intact. The acceptance receipt's embedded `91c...` recovery annotation describes the first reconstruction and remains historical; the recovery sidecar records the exact restoration. I then reconciled the feature's older open-decision wording against current ADR-0012, `local-authentication-v1` and `authority-admission-v1`, and added a post-acceptance crosswalk without changing their decisions. The current feature hash includes that annotation and is recorded in `.amr-local/recovery/t06-contract-reconciliation.json`; the immutable approval receipt and prior recovery evidence are preserved.

The crosswalk confirms: IAM consumes the current authenticated subject/session and interprets RoleDefinition; TEN owns membership/assignment; PRJ/INV provide authoritative ownership/lifecycle facts; AUD owns durable append; and protected use/disclosure follows the accepted single-process writer-preferring gate, U/D audit protocol, current-fact revalidation and bounded caller-bound HTTP handoff. Exact bounds and outcome semantics remain in the contracts. Historical I2.7 open-decision text is retained but is no longer operative where those accepted documents decide T01–T07. These decisions do not cover provider/credential access, distributed writers, support, streaming or production operations. T06 remains dependent on T02, T04 and T05 and must prove revoke/fairness/expiry/audit/actual HTTP handoff interleavings before positive ingress is enabled. T01–T05 may proceed only within their accepted slices and prerequisites. This reconciliation needs no repeat design approval and verifies no runtime or protected ingress.

The original feature hash is preserved in the approval receipt and recovery evidence. The current post-acceptance FEAT-IAM-001 crosswalk hash is `37cecb30122b785d693f27674d28a157100b799b0404efcf8dbe95bd4b56ec77`. The current accepted ADR/contract files have hashes different from the pre-acceptance receipt values; no raw pre-acceptance snapshots for those three documents are present here. The crosswalk therefore verifies the T01–T07 provisions in their current normative text and records the limitation rather than claiming byte identity. See the [traceability record](traceability.md#feat-iam-001--t01t07-contract-reconciliation--2026-10-20) and local reconciliation evidence.

## T04 continuation evidence — 2026-10-22

T03 was merged by the human maintainer at main 25bd7e71fbeb5370d19baac1878ab1609c7d72a3. T04 continues in the existing Product worktree/branch as two owner slices: IAM definition Contracts/store/migration/tests, then TEN membership/assignment Contracts/store/handlers/tests with host-only audit enlistment. The user explicitly authorized Sol Medium implementation for T04 only; default AMR responsibilities are unchanged. See the dated traceability entry for validation/publication status. No repeated Architect review or PR side effect was initiated. T05 and T06 remain pending, and T04 HTTP routes remain unregistered.

## T05 owner continuation — 2026-10-22

PRJ catalog and INV metadata/association owner slices are implemented locally with separate persistence/Contracts and one Host-composed owner/AUD transaction. 28 targeted cases and actual XML/PE/negative architecture checks passed; final gate results are in the dated traceability/private T05 evidence. T04 inputs are snapshotted for later commit separation; no historical checkpoint is reset or task replayed. Native model/effort for this resumed turn is unverified. No AI review/PR side effect or HTTP/provider/runtime migration was initiated. T06 still owns trusted adapters/current policy/workspace composition, endpoint authorization and real U/D output proof; these must not be replaced by the fixture adapters.
