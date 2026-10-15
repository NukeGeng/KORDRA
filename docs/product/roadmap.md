---
id: PROD-ROADMAP
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Delivery roadmap & architecture maturity

| Stage | Deliverable | Exit criteria |
|---|---|---|
| 0 Foundation | Accepted ADR, auth scope, workflow/message contracts, docs CI | Baseline approved, threat review complete |
| 1 MVP core | Organization/tenant, environments, releases, approval, Helm/Compose | End-to-end happy path + wrong-tenant/restart/duplicate tests |
| 2 Distributed execution | Agent gateway, lease, durable workflow, operator recovery | Agent disconnect + unknown outcome + failover verified |
| 3 CI & ops | Webhooks, CI integration, drift dashboard, alerting | Verified webhook signatures, traceability, on-call runbook |
| 4 Enterprise | Tenant quota, platform admin elevation, HA/DR, policy | Security review, drills, SLO measurement |

Không ước lượng ngày công khi chưa biết team/capacity; mỗi stage có thể chia task riêng. Chỉ được ghi "Done" khi Definition of Done thỏa.
