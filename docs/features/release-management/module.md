---
id: MOD-REL
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-REL
---

# Release Management — module definition

**Module ID:** `MOD-REL` · **Phase:** MVP · **Proposed T11 custodian:** NukeGeng, pending exact acceptance.

## Responsibility and boundary

REL owns release lineages, Draft/Frozen/Retired revisions, exact canonical release-intent bytes/hash and scoped command receipts. Host composes neutral ART/PRJ/INV/APR owner Contracts; REL never reads their internal storage or references their runtime projects. It does not own approval decisions, execution operations, provider access or worker dispatch.

This exact T11 proposal is governed by [ADR-0005](../../decisions/0005-immutable-release.md) and [Release and approval v1](../../contracts/release-approval-v1.md). Later [release/execution](../../contracts/release-execution-v1.md) remains proposed. Broad earlier deployment sketches are not adopted through this module summary.

## Inbound commands / exposed operations

CreateDraft, ReplaceDraft, FreezeRevision, RetireRevision; bounded lineage/revision list/detail. Exact release.create/read/retire project permissions and creator/CAS rules apply.

## Outgoing facts / events

T11 commits owner/AUD/floor state locally and publishes no integration event/outbox. Candidate deployment/workflow events require their later owner adoption; expiry is a computed eligibility result, not a fabricated ApprovalExpired event.

## Authorized capabilities

Only the exact release.* actions/scopes in the T11 contract. No deployment.approve alias, override, platform/role-name exemption or automatic grant. Business policy management is a stopped trusted maintainer operation, not an HTTP role privilege.

## Dependencies

Owner-neutral Contracts adapted by Host under existing admission and transaction protocols. Pure own Domain/BCL Contracts; runtime references its own projects and exact approved-on-acceptance EF/Npgsql versions only. This refines the old proposed logical dependency list into an acyclic implementation boundary, not a refactor of existing modules.

## Non-negotiable invariants

Every approval names exact immutable revision/content/policy/scope; recorded history differs from current eligibility. executionAuthorized=false throughout T11. No content substitution, expiry extension, rollback-to-old-head, cross-tenant read, permissive missing dependency or uncertain-commit replay. Initial qualification is Development/Test pilot only.

## Failure / risk scenarios

Wrong tenant/grant, changed-and-restored owner/policy/authority, invalidated preview, stale CAS, concurrent decisions/retirement, audit/clock failure, SQL mutation of frozen history and real ACK loss are mandatory tests. Future worker/dispatch/provider failures remain separately gated T12/T13.

## Feature registry

- [FEAT-REL-001 — Review and freeze an immutable release intent](features/create-release.md)

## Implementation mapping

No runtime implementation exists at main bc91c2a. Feature source_paths/test_paths remain empty until real files and tests exist. Planned paths are not evidence. See contract T11.0–T11.3 for dependency-ordered complete feature PRs and acceptance cases.

## Definition of done

Follow [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md) and [architecture](../../architecture/bounded-contexts.md). Source/test/HTTP/schema/isolated migration/ACL/gate evidence, default Codex PR review and human merge are distinct. Design approval grants no installation/secret/company/production operation.
