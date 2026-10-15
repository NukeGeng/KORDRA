---
id: EXP-LAB-IMPORT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Import real inventory without contaminating product design

## Why the original inventory is necessary
Current documentation uses a **user-provided executive summary**. The full `DEVOPS_CONTROL_PLANE_INVENTORY.md` is not included in this archive. Therefore image digests, Helm release names, 13 queue names, exchange-to-event mappings, current kubeconfig identity and some metrics cannot be independently verified. Cases depending on these values must be `BLOCKED` until the latest approved snapshot is imported.

## Safe import workflow
1. Copy a **redacted** inventory into a restricted evidence repository/storage, outside this public docs tree. Remove internal IPs/hostnames unless separately approved, auth tokens, kubeconfig, Secrets, actual MQTT device serial numbers, payload content and user records.
2. Stamp source URI, `observedAt` UTC, collector identity, cluster UID (hashed if necessary), checksum, approval and retention period. Keep older snapshots; never overwrite baseline history without an audit trail.
3. Cross-check latest source with these 2026-10-15 reported facts: 1 control-plane+4 workers, WebAPI 2 pods/HPA2-6/70%, RabbitMQ three nodes and 13 classic queues, VerneMQ two, CNPG primary+replica, Valkey replication unknown, RWX 5Gi NFS single pod, Metrics Server insecure TLS flag and suspended assets backup CronJob.
4. Populate the 13 real queue rows in `messaging-queue-catalog.template.csv` **only** from approved evidence. Capture exchange/binding, publisher, consumer, ACK, retries, DLQ and schema evolution. Confirm queue type individually; broker topology does not determine queue type.
5. Create a verification manifest keyed by `snapshot_id` and `observedAt`; link each test case to the source and verification status.
6. If cluster changed, create a new baseline snapshot plus drift/decision entry; do not silently replace proposed product specs.

## Mandatory blockers
- Missing full inventory → exact queue/event mapping `LAB-MSG-003/004` remains BLOCKED.
- Unknown target writer ownership → `LAB-DEP-002/003` remains BLOCKED.
- Unknown permissions or environment identity → all write tests remain BLOCKED.
- No disposable namespace/cluster and approved risk budget → P1 and P2 remain BLOCKED.

## Data ownership
The source environment owner validates and approves exports; product architects own generic contracts; test lead owns results; security reviews redaction and access. Reference data is **never** the source of truth for product domain rules.
