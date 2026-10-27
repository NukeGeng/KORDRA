---
id: MOD-APR
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
module_id: MOD-APR
---

# Approval & Policy Governance — module definition

**Module ID:** `MOD-APR` · **Phase:** MVP · **Proposed T11 custodian:** NukeGeng, pending exact acceptance.

## Responsibility and boundary

APR owns immutable business-policy revisions, current heads/environment bindings, exact release approval requests, append-only decisions and scoped command receipts. IAM/TEN remain permission/grant owners. Host adapts the neutral REL frozen-subject contract; no APR/REL runtime cycle or cross-owner storage access. Policy restricts authority and cannot grant it.

This exact T11 proposal is governed by [ADR-0005](../../decisions/0005-immutable-release.md) and [Release and approval v1](../../contracts/release-approval-v1.md). Later [release/execution](../../contracts/release-execution-v1.md) remains proposed. Broad earlier deployment sketches are not adopted through this module summary.

## Inbound commands / exposed operations

Stopped policy inspect/apply/reconciliation, RequestApproval, Grant, Reject, Revoke and bounded approval queue/detail. Exact release.request-approval, release.approval.read, release.approve and release.approval.revoke environment scopes apply; additional current read/SoD rules are in the contract.

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

- [FEAT-APR-002 — Provision an exact release-approval policy](features/approval-policy.md)
- [FEAT-APR-001 — Decide an immutable release intent](features/approve-deployment.md)

## Implementation mapping

No runtime implementation exists at main bc91c2a. Feature source_paths/test_paths remain empty until real files and tests exist. Planned paths are not evidence. See contract T11.0–T11.3 for dependency-ordered complete feature PRs and acceptance cases.

## Definition of done

Follow [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md) and [architecture](../../architecture/bounded-contexts.md). Source/test/HTTP/schema/isolated migration/ACL/gate evidence, default Codex PR review and human merge are distinct. Design approval grants no installation/secret/company/production operation.
