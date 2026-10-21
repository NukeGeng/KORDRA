---
id: FEAT-DRV-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-20
module_id: MOD-DRV
feature_id: FEAT-DRV-001
phase: MVP
source_paths: []
test_paths: []
---

# FEAT-DRV-001 — Execute Helm and Docker Compose via capability-based drivers

## First driver conformance slice — proposed 2026-10-20

Luna T13 DRV task implements Helm first on explicitly approved isolated Kubernetes, after exact ADR-0006/provider-version/operation/resource/hook/shell policy adoption. Compose is a later parity increment, not an implicit dependency. Inputs are typed immutable operation/spec/target/epoch/deadline/capability references, never arbitrary CLI strings or raw browser credentials. Plan/Execute/Observe and supported Rollback remain separate capability outcomes. Credentials resolve through SEC only for the exact authorized operation.

[Release-execution v1](../../../contracts/release-execution-v1.md) owns cross-owner dispatch/Unknown rules. A DB epoch or Helm command exit does not fence a partitioned old provider call; block replacement writer until observed isolation/reconciliation if the provider cannot enforce stale-operation rejection. Respect existing GitOps/controller ownership. Approve hook/template/resource allowlists before rendering/execution; no hidden Secret/exec/broker operation follows from target.read.

Acceptance DRV-C1: plan/execute/observe and supported rollback bind exact immutable target/spec and sanitized receipts. DRV-C2: unsupported version/capability, changed artifact, wrong scope/credential, stale epoch/deadline and arbitrary command reject before effects. DRV-C3: provider partition/timeouts and ACK loss return explicit Unknown and no automatic duplicate Execute. DRV-C4: real isolated conformance proves observed rollout and safe cleanup; unsupported rollback is surfaced. Agent is added only with separately accepted enrollment/protocol, not as a fake prerequisite for worker-direct access.


## User story / outcome
**Actor:** Deployment Worker / Agent. **Goal:** Execute Helm and Docker Compose via capability-based drivers. **Primary owner:** `MOD-DRV`. **Phase:** MVP.

## Pre-conditions
Target verified; driver capabilities and approved plan match.

## Main flow
Validate capability → generate dry-run plan if supported → execute immutable version → return operation receipt → observe target.

## Alternatives / exceptional flow
Helm rollback by revision if valid; Compose redeploy prior digest/config; Argo CD handled by external ownership.

## Messages and integration contracts
`deployment.dispatch.v1; deployment.finished.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Unsupported rollback; unscoped SSH command; provider API timeout returns Unknown.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Driver conformance suite passes plan/execute/observe, duplication, credential and rollback cases.
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
