---
id: MOD-IAM
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-20
module_id: MOD-IAM
---

# Identity & Access — module definition

**Module ID:** `MOD-IAM` · **Phase:** MVP · **Owner:** NukeGeng (accountable for ADR-0012 Foundation T01–T07; wider module scope remains proposed).

## Responsibility and boundary
Identity & Access owns: **Principal, IdentityLink, Session, RoleDefinition, PolicyDecision**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
AuthenticatePrincipal, EvaluateAuthorization, RevokeSession.

## Outgoing facts / events
PrincipalAuthenticated, AccessDenied.

## Authorized capabilities
org.read, org.manage, platform.system.manage. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
tenant-management; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Không quyết định quyền chỉ bằng tên role; deny-by-default; principal token được xác minh.

## Failure / risk scenarios
Token hết hạn, cache permission stale, permission escalation. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-IAM-001 — Authorize scoped resource access](features/authorize-resource.md)
- [FEAT-IAM-002 — Local accounts and revocable browser sessions](features/local-account-session.md)

## Implementation mapping

The separate FEAT-IAM-002 feature owns approved T01–T03 design. Persistence and runtime implementation follow in their own commits. FEAT-IAM-001 retains its existing synthetic policy evidence.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **cross-tenant test cases**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.

## Local-account design preference — 2026-10-18

On 2026-10-18 the owner selected Kordra-managed local accounts as a design preference. The separate local-account feature and authentication contract were later accepted for Foundation T01–T07 under ADR-0012 on 2026-10-20. T01 persistence and its isolated migration tests are approved for implementation. No login/session endpoint or runtime authentication behavior is implemented yet, and login grants no implicit platform-to-tenant access.

## Current synthetic policy evidence — 2026-10-18

[FEAT-IAM-001/I1](features/authorize-resource.md#i1--synthetic-policy-proof-authorized-test-only-candidate) maps one test-only file in the existing bootstrap test assembly: 66 candidate permit/deny cases passed locally, with historical native Sol 6.1 High execution/design/post-review and contributor triage. The earlier empty mappings/no-tests statements describe I0 history. The IAM product persistence project now exists for T01, but no authentication/session provider or production authorizer exists; the broader module remains proposed. NukeGeng is accountable for the Foundation T01–T07 scope accepted by ADR-0012. Real login/enrollment/recovery and trusted runtime authority remain unimplemented pending their task evidence.

## Scoped decisions — ADR-0003 (2026-10-18) and ADR-0012 (2026-10-20)

The maintainer accepted ADR-0003 for its conceptual permission/trusted-resource-scope baseline, then accepted ADR-0012 for the exact Foundation T01–T07 implementation scope. NukeGeng is accountable for that Foundation slice; broader module scope remains proposed. The synthetic policy fixture remains test-only. T01 persistence is locally implemented and verified; login/session, authorization enforcement, audit admission and protected inventory behavior remain unimplemented.
