---
id: EXP-LAB-RUN
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — phased runbooks for observation, deployment, resilience

## P0 — Observe the reported infrastructure (no mutation)
**Preflight:** use read-only Kubernetes user/service account, RabbitMQ Management API reader, metrics-only token, registry pull-only identity; confirm current kubecontext and cluster UID.

Collect **sanitized** evidence: `kubectl get nodes`, `kubectl get deployments,hpa,pdb`, `kubectl get pvc,pv,cronjobs`, Helm `list/status`, RabbitMQ exchange/queue info with `GET`, VerneMQ nodes/topics metrics, Prometheus targets, Loki query, CNPG cluster status, Valkey replication through allowed metrics. Avoid retrieving Secret objects/values or device payloads. Compare with reported baseline; mark `unknown` when blocked by ACL. Verify `--kubelet-insecure-tls` via approved read-only pod spec and backup CronJob suspend state.

**Pass:** inventory changes zero; timestamps/permissions recorded; unknowns remain explicit. **Stop:** unexpected mutation, excessive API load or credentials with write-only policy not approved for observation.

## P1 — Safe Helm and Compose experiments
**Preflight:** dedicated sandbox cluster preferred. If same physical cluster, approved namespace with quotas, network policies, limit ranges, separate service accounts and no shared production PVC/queues. Validate cluster UID/namespace each time. Create disposable app version A and B with pinned image digests. Compare rendered manifests, dry-run plan, approved hash, execute, health, history, rollback. For Compose use disposable VM with host-key pinning and no mapped business ports.

**Pass:** correct version A → B → A; audit, workflow state and actual target agree; no production components changed. **Stop:** unexpected resource owner, missing prior revision or namespace/VM mismatch.

## P2 — Controlled failure injection ONLY on isolated clone
Terminate synthetic worker/agent during a known operation, drop callback, duplicate command, intentionally fail readiness, block broker in test vhost, run bounded backlog. Verify idempotency, timeout, `UNKNOWN` reconciliation, retry policy, DLQ auth and rollbacks. No failover experiments on shared CNPG, RabbitMQ, VerneMQ, NFS or Talos cluster.

**Pass:** committed intent recoverable; no duplicate external side effects; clear owner and trace; failures bounded. **Stop:** message rate/resource budget exceeded or evidence no longer trustworthy.

## P3 — Optional real operational pilot
Only after Gates A-C pass; each release gets its own change ticket, independent approver, pinned spec, rollout plan, scope, maintenance window, rollback owner, backup proof and abort thresholds. Restrict to reversible, low-impact application deployment. Does not grant access to database schema destructive migration or infrastructure outages.

## Example read-only commands (require current context verification)
```bash
kubectl config current-context
kubectl get nodes -o wide
kubectl get deployment,hpa,pdb -A
kubectl get pvc,cronjob -A
helm list -A
```
These are **examples, not executed commands**. `kubectl get -A` output can contain sensitive topology data: sanitize before storing in evidence.
