---
id: ENG-TRACE
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Requirements ↔ Module ↔ Feature ↔ Code ↔ Tests

## Coverage rules
`UseCase → feature_id → module_id → contract_ids → source_paths → test_paths → evidence/PR`.
- `source_paths: []` = not mapped yet, **not implemented**.
- Mỗi implemented/verified feature phải có ≥1 source, ≥1 test và evidence; manual exceptions require owner approval.
- Incident/regression maps back to feature. Architectural change maps to ADR and affected features.

## Initial cross-reference
| Use case | Feature | Module | Contract / evidence | Implementation status |
|---|---|---|---|---|
| UC-001 | FEAT-TEN-001 | MOD-TEN | permissions.md | proposed/unmapped |
| UC-002 / GOAL outcome 2 / F0-ART-I1 | FEAT-ART-001/I1 | MOD-ART | [digest contract](../contracts/artifact-identity.md), [feature evidence](../features/artifact-management/features/immutable-artifact.md), [merged PR #1](https://github.com/NukeGeng/KORDRA/pull/1) | Standalone digest Domain validation increment merged with docs/product CI passed on `2aa156f4`; ADR-0008 accepted narrowly; registry-backed artifact registration not implemented |
| UC-003 | FEAT-REL-001 | MOD-REL | message-envelope.md | proposed/unmapped |
| UC-004 | FEAT-APR-001 | MOD-APR | permissions.md | proposed/unmapped |
| UC-005/006 | FEAT-DEP-001 | MOD-DEP | driver-contract.md | proposed/unmapped |
| UC-007 | FEAT-AGT-001 | MOD-AGT | agent-protocol.md | proposed/unmapped |
| UC-008 | FEAT-MSG-001 | MOD-MSG | message-catalog.md | proposed/unmapped |
| UC-009 | FEAT-REC-001 | MOD-REC | messaging.md | proposed/unmapped |
| UC-010 | FEAT-PAD-001 | MOD-PAD | permissions.md | proposed/unmapped |
| Developer quality-first workflow | [FEAT-AMR-001](ai-orchestration/features/quality-first-amr.md) | ENG-TOOLING (engineering only) | [local contract](ai-orchestration/contract.md), [evidence](ai-orchestration/testing-and-evidence.md), [PR #1](https://github.com/NukeGeng/KORDRA/pull/1) | Tooling merged; direct coordinator/native desktop model verification and independent/provider automatic review have explicit limitations |
| Source and solution architecture baseline | [ADR-0009](../decisions/0009-source-architecture.md) | Architecture governance (proposal) | [six source specs](../architecture/README.md), [integration audit](source-architecture-adoption.md) | Proposed documentation only; no source migration or broad graph/host checks are accepted. One narrow Domain assembly dependency test already runs in product CI. |
| OP-01–OP-10 / OP-P0–OP-P4 | Candidate existing features; [FEAT-INV-001/I0](../features/infrastructure-inventory/features/register-target.md), [FEAT-PRJ-001/I0](../features/project-environment/features/environment-catalog.md) and [FEAT-SEC-001/I0](../features/connection-secrets/features/secret-binding.md) now have proposed read-only seams | Existing module owners; ConnectionProfile stays MOD-SEC-owned; backup ownership unresolved | [Operator requirements](../product/operator-workstation.md), [pilot sequencing](../product/operator-pilot-plan.md), [dated task evidence](../product/roadmap.md) | All three documentation tasks passed native execution/gates/review/triage, including High credential work after explicit resume. Kubernetes/Helm read-only is the owner-selected design target. Product source/tests remain unmapped. Local disposable host/reference feasibility passed with 14 HTTP assertions; native High re-review/triage recorded separately from the failed sandbox attempt. ADR-0010 is explicitly accepted for the scoped local bootstrap only, implementation pending; no pilot/runtime completion or company authorization. |
| Product technology reference A–F | No implementation feature assigned by catalog inclusion | Product planning; not new modules | [43-group catalog](../product/technology-landscape.md), [Beta gates](../product/oss-beta-acceptance.md) | Nonbinding Build/Integrate/Observe/Defer reference; no dependency or ADR acceptance. |

## File mapping after source exists
Goal-driven execution maps the existing FEAT-AMR-001 source/test registry to [goal workflow](ai-orchestration/goal-execution.md). Runtime smoke, E2E execution and review evidence are distinguished from policy/configuration in [runtime evidence](ai-orchestration/testing-and-evidence.md). Product milestone continuation remains in [roadmap](../product/roadmap.md).

The v1.2 source architecture files are proposals, not code mappings. Existing project paths remain owned by the current feature specs until an explicit decision and incremental migration update those mappings.

Set feature frontmatter `source_paths` and `test_paths` to actual repository-relative paths verified through `git ls-files`, then add `evidence` link in spec. The checker can validate existence in strict mode; review still validates semantic traceability.

## Current scoped bootstrap and folder evidence — 2026-10-18

The OP-P0 row above describes pre-bootstrap specification work. FEAT-INV-001/I1 now maps the actual four new projects and shared root solution/CI (shared paths remain co-owned by FEAT-ART-001). ADR-0010 was explicitly accepted by the maintainer. Current local gates: 114 tooling tests, 48 Artifact + 19 bootstrap tests, locked restore/build, docs/scope/mapping all pass. Stable independent native Sol 6.1 High review and contributor triage completed `KORDRA-OP-P0-BOOTSTRAP-CONT-001`; the old failed attempt remains failed. See [I1 evidence](../features/infrastructure-inventory/features/register-target.md#coordinator-final-acceptance--2026-10-18).

The [folder framework](repository-folder-framework.md) tracks 45 reserved locations with proposed README files, without new project/source mappings or ADR-0009 acceptance. Owner-selected Kordra-managed local accounts feed proposed FEAT-IAM-001 design; its source/test mappings remain empty, ADR-0003 and persistence/credential choices remain open. No company access, positive inventory read, full pilot completion or new GitHub CI evidence is claimed.

FEAT-IAM-001/I0 proposal readiness is now completed as a documentation task: native Sol 6.1 High execution/review, full existing gates and contributor triage passed. [IAM owner spec](../features/identity-access/features/authorize-resource.md) retains empty source/test mappings and lists planned tests separately. No IAM runtime or authentication implementation is verified; contract/PoC/storage and explicit ADR-0003 review decisions remain prerequisites.

## AMR v3.1 policy increment — 2026-10-18

FEAT-AMR-001 / ENG-TOOLING maps the existing `docs/tooling/amr.py`, `amr_goal.py`, `amr_native.py`, `.codex/config.toml` and existing mapped tests to Luna Max routing, critical/milestone/design review and backward-compatible policy-versioned execution receipts. AGENTS/WORKFLOW, owning feature, guide, local contract, [BIR protocol](ai-orchestration/bounded-incremental-review.md), PR template and change history describe the same owner-approved policy. Contributor/repository-local scope introduces no tenant permission or product runtime contract. Deterministic validation and exact native model/review observations are centralized in [runtime evidence](ai-orchestration/testing-and-evidence.md); provider execution/native hard-cap and desktop-control limitations remain explicit debt rather than passing claims.

## Synthetic IAM proof and AMR runner recovery — 2026-10-18

FEAT-IAM-001 / MOD-IAM maps `tests/InfrastructureInventory.Bootstrap.Tests/IdentityAccess/AuthorizationPolicyProofTests.cs` through the existing owner spec. The source map remains empty: evaluator/facts are private test-only candidates in the existing project. [I1 evidence](../features/identity-access/features/authorize-resource.md#coordinator-acceptance--2026-10-18): 66 policy cases, 133 product/141 tooling tests, formatter, native High design/execution/post-review and accepted triage. Earlier I0 empty test mappings are dated history; this does not implement product authorization. Permission keys/wire contracts are unchanged; ADR-0003 is a reviewed proposal awaiting human acceptance.

FEAT-AMR-001 / ENG-TOOLING retains existing adapter/dispatcher/test ownership for the bounded-runner developer context and validated pre-dispatch facts. Runtime source/task/lock/review checks are preserved. Its native task and independent review passed full gates; the later successful IAM source task exercised the corrected context live. Shared contract/workflow/evidence docs are reconciled here after task acceptance. Old no-op/failed checkpoints are not promoted; diagnostic debt DEBT-AMR-004 is explicit.

## ADR-0003 narrow human acceptance — 2026-10-18

FEAT-IAM-001 / MOD-IAM: [ADR-0003](../decisions/0003-tenant-authorization.md) accepted by the maintainer for the conceptual permission/trusted-scope baseline only. Existing synthetic proof and its 66 passing cases are unchanged; source mapping remains empty. The decision index, permissions evidence, module/feature, roadmap and change history record the scope and historical status transition. Runtime contracts, owners, authentication/storage and inventory ingress remain pending; no public schema or consumer migration changes.

## AMR v4 policy migration — 2026-10-19

FEAT-AMR-001 / ENG-TOOLING owns this migration through the existing `docs/tooling/amr.py`, `amr_goal.py`, `amr_native.py`, `.codex/config.toml` and mapped AMR regression suites. The current guide, goal workflow, local contract, BIR protocol, AGENTS.md and WORKFLOW.md describe the same Architect / Developer / Reviewer roles. `goal validate`, `goal accept` and milestone transitions perform deterministic validation and contributor triage without invoking an AI reviewer. [Runtime evidence](ai-orchestration/testing-and-evidence.md#amr-v4-policy-migration--2026-10-19) distinguishes catalog/config support from execution, provider review and hard-cap capabilities. Existing completed v3/v3.1 receipts remain historical; unfinished-task migration is limited to GOAL review obligations. No product runtime, accepted ADR, permission, provider setting, PR, merge or production state changed.

## FEAT-INV-001 Linux fixture recovery — 2026-10-19

The [Inventory architecture test](../features/infrastructure-inventory/features/register-target.md#linux-fixture-first-run-recovery--2026-10-19) now provisions an existing isolated HOME/CLI-home and a private Linux `XDG_RUNTIME_DIR` for the SDK child process. This repairs NuGet migration-mutex startup while retaining the compiled forbidden-assembly-edge checks. The corrected code passed on macOS and in a sanitized Linux SDK 10.0.400 checkout (locked restore, zero-warning Release build, Artifact 48/48 and Inventory Bootstrap 85/85). GitHub CI on the pushed revision is pending; the broader registration feature and real provider reads remain incomplete. No product contract, permission, ADR or runtime behavior changed.
