---
id: EXP-LAB-CONTRACTS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — critical contract, authorization, messaging tests

## Tenancy + authorization
| Scenario | Expected |
|---|---|
| user from org-A tries target org-B | denied at API, message handler, worker and agent target validation |
| role with `deployment.request` but no `deployment.approve` | may request, cannot approve |
| platform operator / Super Admin without tenant delegation | can manage platform metadata according to policy, cannot read org-B secrets or deploy org-B apps |
| approval changed after immutable spec changes | blocked; requires new plan+approval |
| revoked permission during long workflow | enforce at protected state transition; no implicit authorization persistence |
| agent message forged with tenantId mismatch | reject and audit |

## Messages and failure semantics
| Scenario | Expected |
|---|---|
| duplicate `DispatchDeployment` with same operationId | no duplicate effective deployment mutation |
| ACK lost after Helm succeeded | observe target before retry; resolve outcome |
| poison message / schemaVersion unknown | bounded retry → quarantine/DLQ + diagnostic audit |
| same target two concurrent requests | lease fencing enforces one writer |
| broker down then restarted | durable intent/Outbox eventually delivered, no phantom success |
| MQTT synthetic `Init/Monitor` and RabbitMQ processing | isolated fixtures only, schema validated, no business queue consume |

## Contract ownership
Contract owner is product module (`docs/features/*`), not LAB-01. Testbed captures empirical evidence for or against contracts; a result does not silently change the contract. If behavior differs, open defect or ADR and update feature acceptance tests.
