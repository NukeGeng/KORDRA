---
id: ARCH-OBS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Observability and operational feedback

## Golden signals
API latency/errors, workflow stuck age, outbox backlog age, queue depth, retry/DLQ rate, agent heartbeat lag, target reconciliation lag, deployment success/failure, release lead time, MTTR.

## Correlation
Every request/command/event/agent step carries `traceId`, `correlationId`, `deploymentId`, `operationId`, `tenantRef` (safe opaque identifier). Do not tag metrics with raw secret, username, unbounded artifact hash or high-cardinality log path.

## Channels
Logs → log collector/object store; metrics → Prometheus-compatible sink; traces → OpenTelemetry OTLP; dashboard → read projection/WebSocket/SignalR. Telemetry channels are separated from deploy command traffic.

## Health semantics
Distinguish `ProcessLiveness`, `InfrastructureReadiness`, `ApplicationReadiness`, `BusinessSmokeCheck`; deployment success requires user-defined verification policy, not only container running. Unknown/stale evidence is neither healthy nor failed.

## Alerts
Operational ownership per alert; avoid alert flood. Alert examples: expired certificate, queue stalled, elevated DLQ, missing agent, prolonged `Unknown`, audit pipeline failures, unauthorized denial spikes. Add runbook links at alert source.
