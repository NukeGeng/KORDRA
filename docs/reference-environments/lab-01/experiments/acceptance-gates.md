---
id: EXP-LAB-GATES
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — release and test acceptance gates

## Gate A — trustworthy inventory (P0)
- Product reads **current** node/workload/release/registry/queue/metrics metadata without changing any workload or ACKing a business message.
- Records `observedAt`, source/permissions and `unknown` for unsupported/unavailable metrics.
- Distinguishes VerneMQ vs RabbitMQ vs MQTT processor; 13 queues verified from sanitized source (if incomplete, corresponding tests BLOCKED).
- Identifies backup suspended, Metrics Server insecure TLS, Valkey HA unknown, one NFS pod, Jaeger/OTel not observed without mutating anything.

## Gate B — safe deployment (P1)
- Sandbox release target spec pinned by OCI digest and Helm revision; approval covers **exact same** spec.
- Exactly one concurrent writer per target; denied cross-tenant/wrong role; rollback succeeds in disposable workload only.
- Agent disconnected: final result `UNKNOWN` or `IN_PROGRESS` until reconciled; never infer failure/success solely from lost ACK.

## Gate C — resilience (P2)
- Crash/restart of API, worker, gateway, and agent during synthetic operation does not lose committed intent.
- Duplicate dispatch/late ACK/retry triggers at-most-one effective external mutation for controlled driver or is reconciled before replay.
- DLQ replay requires permission and produces audit trail; poisoned messages do not block unrelated queue partitions indefinitely.
- Backpressure bounded by configured queue depth/worker concurrency; no uncontrolled load on shared brokers.

## Gate D — operational quality (all phases)
- Every case has result/evidence, owner, timestamp, environment ID, build ID, case version.
- Failed case creates linked defect/risk/debt; functionality not advertised as fully supported until relevant cases PASS.
- Minimum security cases: unauthenticated, insufficient permission, wrong tenant, platform scope without tenant delegation, expired approval, replay with changed spec.

## Quantitative SLOs are NOT assumed
Queue lag, time-to-detect, recovery time, p95 API latency, status freshness, resource overhead and test throughput: record first baseline from P0/P1 before setting thresholds. Do not invent service SLO or assert 70% CPU target implies performance SLO.
