---
id: FEAT-OBS-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-24
module_id: MOD-OBS
feature_id: FEAT-OBS-001
phase: MVP core / Phase 2 full
source_paths: []
test_paths: []
---

# FEAT-OBS-001 — Verify deployment health and publish scoped status

## Read-only status and deployment verification slices — proposed 2026-10-20

Luna T09 supplies scoped read-only observations; T13 adds verification evidence consumed by DEP. Actor authorized User with `target.read`/`project.read` at catalog scope, or exact delegated worker identity. OBS owns HealthPolicy/ProbeResult/StatusProjection; INV owns inventory/target identity and DEP owns final deployment outcome. Freshness/coverage/source revision and current disclosure authority are independent: a current grant never makes stale data healthy.

Before adapter code, approve probe destinations/SSRF/TLS policy, signal-specific freshness/clock/deadline limits and bounded result DTOs. OP-P0 can label unknown/unavailable/stale without pretending health verification is implemented. M2 verification compares observed target/workload/image digest, rollout readiness and approved health requirements with frozen intent; UI projections are not deployment authority. No secret-bearing raw logs/manifests, unbounded stream or cross-tenant subscription is enabled by this first slice.

Acceptance OBS-V1: healthy requires fresh identity-bound coverage for every policy condition. OBS-V2: old/future/missing timestamps, partial coverage, wrong target/digest, denied credentials and network failure cannot pass as healthy. OBS-V3: selection/revoke while data waits prevents unauthorized disclosure; bounded polls never reuse another tenant's cached data. OBS-V4: failed/unknown verification reaches DEP safely with correlation and redacted evidence; fake probe success alone does not establish real rollout. Follow [handoff](../../../engineering/luna-implementation-handoff.md) and contract fixtures before clients integration.


## User story / outcome
**Actor:** System / Operator. **Goal:** Verify deployment health and publish scoped status. **Primary owner:** `MOD-OBS`. **Phase:** MVP core / Phase 2 full.

## Pre-conditions
Health policy set per application and target capability.

## Main flow
Collect rollout status → readiness checks → optional HTTP/smoke checks → publish bounded result and observability projection.

## Alternatives / exceptional flow
Probe timeout produces Unknown/Failed per policy with evidence; do not fabricate healthy state.

## Messages and integration contracts
`target.observed.v1; deployment.verified.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Cross-org websocket subscription, raw credentials in logs, stale observation counted as success.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

## Acceptance criteria
Status includes freshness/correlation; tenant-scoped dashboard shows only allowed resources.
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

## T09.1 workload-first boundary — proposed 2026-10-24

The owner's workload-first preference is captured in [ADR-0016](../../../decisions/0016-kubernetes-workload-observation.md) and its [contract](../../../contracts/kubernetes-workloads-v1.md). This first slice proposes INV-owned timestamped workload facts and a disclosure freshness bound; it does not implement OBS health policy, verification, telemetry projections or assign this module a runtime owner. OBS remains a prospective consumer; readiness/Running or a current snapshot is not business health. [NEEDS-DECISION]: provider design acceptance remains separate. Actual OBS source/test mappings stay empty.


Accepted scope update — 2026-10-24: NukeGeng approved exact ADR-0016 and Kubernetes workloads v1 for T09.1a–d, including the post-C managed-header exception, offline private-PKI residual, exact Namespace identity GET and additive config/scope/freshness caps. This supersedes pending-design approval wording in this section only. Runtime implementation and K09-01–12 verification remain pending. T09.1e, existing secrets, real clusters, Helm and OBS runtime remain excluded; historical checkpoints are preserved without replay.
