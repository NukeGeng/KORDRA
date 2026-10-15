---
id: REF-LAB-RISK
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — risks, assumptions, unknowns

| ID | Finding (reported) | Risk | Decision/test | Allowed mode |
|---|---|---|---|---|
| R-01 | Production-like 1 CP + 4 workers | shared blast radius | Never launch failover/chaos without clone and approval | observe |
| R-02 | RabbitMQ 3 nodes, 13 classic queues | broker HA ≠ individual queue replication | Inventory topology; run quorum comparisons in dedicated sandbox | observe / sandbox |
| R-03 | `--kubelet-insecure-tls` | kubelet cert verification bypass | document mitigation proposal, no live flag change | observe |
| R-04 | Assets CronJob suspended | backup gap/data-loss risk | verify restore plan off-cluster; do not unsuspend on shared cluster | observe / isolated |
| R-05 | RWX NFS one pod | potential single point of failure | isolated file workload/clone recovery tests | observe / isolated |
| R-06 | Valkey 2 pods but replication unverified | false HA status | `unknown` until ACL-compliant health evidence | observe |
| R-07 | OTel Collector/Jaeger config only | tracing is not deployed evidence | mark capability `not_observed`, no false green | observe |
| R-08 | HTTP registry in LAN | integrity/trust and potential credential exposure | digest/tls policy, allowlist network, read-only | observe |
| R-09 | GitLab CI not integrated | false claim full CI/CD | adapter disabled; mock until connected | offline |
| R-10 | Device message AES/Base64 payload | secrets/PII/identifier leakage | use synthetic SN/keys/payload; evidence redact | sandbox |
| R-11 | SignalR without confirmed backplane | possible multi-pod messaging gaps | do not assert correctness; separate test with synthetic clients | isolated |
| R-12 | Unknown Helm/GitOps ownership | conflicting reconcilers | list controller writer owner before any rollout | observe |

## Priority and status discipline
Priority P0: R-01, R-02, R-04, R-10, R-12. Status `reported`; no remediation done. Each risk may become `verified` only with signed/timestamped read-only evidence, then an ADR/change request for remediation.
