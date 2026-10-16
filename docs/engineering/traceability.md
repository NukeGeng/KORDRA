---
id: ENG-TRACE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
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
| UC-002 / GOAL outcome 2 / F0-ART-I1 | FEAT-ART-001/I1 | MOD-ART | [digest domain contract](../contracts/artifact-identity.md), [feature evidence](../features/artifact-management/features/immutable-artifact.md) | Module-local validation increment; full registration and ADR acceptance pending |
| UC-003 | FEAT-REL-001 | MOD-REL | message-envelope.md | proposed/unmapped |
| UC-004 | FEAT-APR-001 | MOD-APR | permissions.md | proposed/unmapped |
| UC-005/006 | FEAT-DEP-001 | MOD-DEP | driver-contract.md | proposed/unmapped |
| UC-007 | FEAT-AGT-001 | MOD-AGT | agent-protocol.md | proposed/unmapped |
| UC-008 | FEAT-MSG-001 | MOD-MSG | message-catalog.md | proposed/unmapped |
| UC-009 | FEAT-REC-001 | MOD-REC | messaging.md | proposed/unmapped |
| UC-010 | FEAT-PAD-001 | MOD-PAD | permissions.md | proposed/unmapped |
| Developer quality-first workflow | [FEAT-AMR-001](ai-orchestration/features/quality-first-amr.md) | ENG-TOOLING (engineering only) | [local artifact contract](ai-orchestration/contract.md), [evidence](ai-orchestration/testing-and-evidence.md) | Tooling present in d0eff75; product-gate extension in current branch; owner acceptance pending |
| Source and solution architecture baseline | [ADR-0009](../decisions/0009-source-architecture.md) | Architecture governance (proposal) | [six source specs](../architecture/README.md), [integration audit](source-architecture-adoption.md) | Proposed documentation only; no source migration or broad graph/host checks are accepted. One narrow Domain assembly dependency test already runs in product CI. |

## File mapping after source exists
Goal-driven execution maps the existing FEAT-AMR-001 source/test registry to [goal workflow](ai-orchestration/goal-execution.md). Runtime smoke, E2E execution and review evidence are distinguished from policy/configuration in [runtime evidence](ai-orchestration/testing-and-evidence.md). Product milestone continuation remains in [roadmap](../product/roadmap.md).

The v1.2 source architecture files are proposals, not code mappings. Existing project paths remain owned by the current feature specs until an explicit decision and incremental migration update those mappings.

Set feature frontmatter `source_paths` and `test_paths` to actual repository-relative paths verified through `git ls-files`, then add `evidence` link in spec. The checker can validate existence in strict mode; review still validates semantic traceability.
