---
id: FEAT-REL-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
module_id: MOD-REL
feature_id: FEAT-REL-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-REL-001 — Create immutable release intent

## Frozen release implementation slice — proposed 2026-10-20

Luna T11 REL task follows ART registration and current scoped catalog authority. Actor User with `release.create` project. Adopt [release-execution v1](../../../contracts/release-execution-v1.md) and the exact ADR-0005 scope before implementing new state. REL owns canonical bytes/hash, immutable revision and references; APR owns approval decisions. Composition uses Contracts rather than REL/APR runtime cycles. Preserve artifact provenance, target binding, health/policy/config/driver revisions and no plaintext secret.

Acceptance REL-F1: deterministic golden vectors produce the same server hash across property-order changes and different hashes for meaningful changes. REL-F2: foreign artifact/target, tag-only artifact, malformed/oversized/duplicate-key spec and unknown version reject. REL-F3: Draft→Frozen fixes bytes; attempted mutation rejects and a changed intent creates a new revision. REL-F4: duplicate request/commit ACK loss returns/reconciles one revision with audit; no dispatch is implied. API/schema/error examples and migration fixtures accompany actual owner source/test mapping.


## User story / outcome
**Actor:** Developer / Release Manager. **Goal:** Create immutable release intent. **Primary owner:** `MOD-REL`. **Phase:** MVP.

## Pre-conditions
Artifact pin verified; environment/target/config selected.

## Main flow
Select artifact → canonicalize target/config → compute spec hash → persist release revision → request policy validation/approval.

## Alternatives / exceptional flow
Change environment or digest creates a new release revision requiring new approval.

## Messages and integration contracts
`release.created.v1; deployment.requested.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Tag-only release, artifact from different tenant, spec edited after approval.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Release revision immutable once approved; each change creates auditable revision.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## Source & test traceability
- Source files: **unmapped** (`source_paths: []`).
- Test files: **unmapped** (`test_paths: []`).
- Evidence: `PENDING` — link PR / commit / test report when implemented.
- Open decisions: `NEEDS-DECISION` for any policy-specific variant not covered by accepted ADR.
