---
id: MOD-ART
status: proposed
owner: unassigned
last_reviewed: 2026-10-25
module_id: MOD-ART
---

# Artifact Management — module definition

**Module ID:** `MOD-ART` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Artifact Management owns: **Artifact, OCIImageDigest, BuildProvenanceRef, SourceCommit**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
RegisterArtifact, ResolveDigest, VerifyArtifact.

## Outgoing facts / events
ArtifactRegistered, ArtifactRejected.

## Authorized capabilities
artifact.register, artifact.read. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
ci-integrations; project-environment. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Mutable tags for discovery only; deploy pinned by digest and verified provenance policy.

## Failure / risk scenarios
Tag changed between approve/deploy, digest not available, incompatible architecture. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-ART-001 — Discover and pin immutable deployable artifacts](features/immutable-artifact.md)

## T10 bounded adoption proposal — 2026-10-25

[ADR-0018](../../decisions/0018-project-artifact-registration.md) and [Artifact registration v1](../../contracts/artifact-registration-v1.md) propose digest-first project registration/catalog, ART-owned EF storage/receipts/declarations and explicit IAM/PRJ/SEC/AUD ports. NukeGeng is proposed custodian only for this slice, effective upon exact acceptance. Existing module ownership/status and wider CI/events/tag-resolution vision stay proposed. No source, persistence migration, registry access or new runtime behavior is delivered by this design.

## Implementation mapping
At baseline d0eff75 the module had no source/tests. FEAT-ART-001/I1 now maps a module-local digest validation increment under `src/Modules/ArtifactManagement/Domain/` and `tests/ArtifactManagement.Domain.Tests/`, merged via PR #1 at `2aa156f4` with merge-SHA docs/product CI success. It does not implement registry discovery, tenant-owned registration, events, persistence, policy/provenance verification or release integration. See the feature for dated build/test/review evidence; module status stays proposed pending architecture/owner review.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **registry and snapshot discovery**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.

### Exact T10.1a–f acceptance — 2026-10-25

NukeGeng approved exact ADR-0018 and Artifact registration v1, including this owner’s bounded design dependencies and NukeGeng’s ART/registry slice custody. This supersedes pending-approval language in the T10 proposal above only; wider feature/module status, existing accepted contracts and historical evidence remain unchanged. Implementation, ART10 tests, native/live scope, real provisioning and PR/merge evidence are still separate. No repeat design approval or extra Sol review is needed for covered T10.1a–f work.
