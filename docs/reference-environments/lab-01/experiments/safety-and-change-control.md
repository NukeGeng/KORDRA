---
id: EXP-LAB-SAFETY
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — hard safety policy and preflight gates

## Default deny
**Shared/production resources: OBSERVE_ONLY**. Script và UI phải mặc định `--dry-run` khi có mutation; hành động thay đổi cần `explicit target`, `environment identity`, `actor scope`, `approval ID`, `immutable spec hash`, `lease`, `confirmed restore plan` và mode `sandbox` hoặc change ticket hợp lệ.

## Allowed actions by mode
| Operation | P0 observe | P1 sandbox | P2 cloned resilience | P3 optional production |
|---|---|---|---|---|
| Kubernetes inventory, HPA/PDB, Helm list/status | YES read-only | YES | YES | YES read-only |
| Registry metadata HEAD/GET/digest lookup | YES read-only | YES | YES | YES read-only |
| RabbitMQ Management API GET / broker metrics | YES read-only | YES | YES | YES read-only |
| VerneMQ stats / Prometheus / Loki query | YES read-only | YES | YES | YES read-only |
| Helm install/upgrade test app | NO | YES dedicated target | YES | per release approval only |
| Compose deploy | NO | YES disposable VM | YES | per release approval only |
| MQTT publish / queue produce | NO | SYNTHETIC, dedicated topic/vhost | YES | default NO |
| Queue purge/delete, CNPG failover, Talos reboot, NFS disruption | NO | NO on shared | only isolated clone | NO in pilot |
| Secret read/export | NO | NO | NO | NO |

## Before any write
1. Confirm operator identity and two-person approval when required; reviewer cannot equal requester for production.
2. Verify live target identity (cluster UID, namespace, server host key), ensure not accidentally targeting shared environment.
3. Validate allowlisted `targetId`, `tenantScope`, permission, account expiry, driver capability and policy version.
4. Lock immutable artifact digest, manifest/spec hash, diff plan and expected resource changes. Reject unspecified image tags (`latest` alone).
5. Verify tested rollback path, availability of backups for stateful components and observability coverage.
6. Acquire target lease, use bounded timeout, monitor budget, attach correlationId and stop conditions.

## Stop conditions (abort; operator review)
- Environment/namespace mismatch, unexpected resource affected, unknown agent outcome, unverified backup, missing approval, permission change, or observed health regression beyond preset budget.
- No automatic retry after unknown external side-effect; reconcile first.
- Do not use real device credentials, serial numbers, Firebase tokens, API keys or decrypted MQTT payloads in artifacts.

## Data classification
Inventory snapshots may expose topology/IPs, image tags, endpoints and tenant/device data. Do not commit raw exports to a public repository. Keep only redacted samples in docs; store encrypted, access-controlled evidence externally with retention policy.
