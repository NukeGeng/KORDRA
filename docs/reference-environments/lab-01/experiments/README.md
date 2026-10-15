---
id: EXP-LAB-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — experiment plan and phase gates

## Goals (what this testbed proves)
1. Tích hợp nhiều hạ tầng thông qua drivers/profile, không hard-code domain doanh nghiệp.
2. Độ đúng của discovery → plan → approval → immutable artifact → execute → observe → audit → rollback trong **đích cách ly**.
3. Quản trị tenant/role/permission đúng scope; Super Admin không được ngầm đọc/deploy tenant khác.
4. Chuyển thông điệp bền vững; duplicate/crash/unknown outcome không gây side effect hai lần.
5. Dữ liệu quan sát phản ánh đúng trạng thái thiếu/chưa xác thực (Valkey, OTel, classic queue, backup).

## Phases and exit gates
| Phase | Name | Where | Allowed | Exit gate |
|---|---|---|---|---|
| P0 | Discovery and shadow mode | Existing cluster via *read-only credentials* | GET/list/describe/metrics/status | Inventory timestamp, permissions log, zero writes |
| P1 | Contract + sandbox deployment | Dedicated namespace/nonprod nodes; ideally isolated cluster | test workloads, synthetic data, locked artifacts | All core E2E cases, clean rollback, isolated audit |
| P2 | Resilience/security | Dedicated cloned environment | agent offline, queue fault, worker kill, node simulation | durable recovery, idempotency, tenant negative tests |
| P3 | Controlled pilot (optional) | Real production-like workloads | ONLY individually approved, reversible, low-risk releases | signed change ticket, maintenance window, backup proof, rollback owner |

P3 **không tự động** cho phép restart node, CNPG failover, RabbitMQ quorum migration, NFS outage, backup restore hay thay đổi Metrics Server. Đây là những change riêng.

## Resources
- [Coverage matrix](coverage-matrix.csv) — source of truth cho case ID, mode, module, evidence.
- [Acceptance gates](acceptance-gates.md) — định nghĩa pass/fail/blocked.
- [Safety rules](safety-and-change-control.md) — cơ chế chặn hành động nguy hiểm.
- [Evidence template](evidence-template.md) — ghi nhận kết quả thật.
- [Test data](synthetic-data.md) — tránh tác động lên device/customer data.
- [Phased runbooks](phase-runbooks.md) — các bước đo/triển khai/phục hồi.

## Evidence flow
`Case ID → environment snapshot → plan hash → approval → execution ID → observation → assertions → result/evidence link → incident/debt/feature/ADR`.
Kết quả `NOT_RUN`, `BLOCKED`, `PASS`, `FAIL`, `INCONCLUSIVE`; không có `PASS` chỉ vì có tài liệu.

- [Inventory import and provenance](import-and-provenance.md) — exact 13 queue mapping is blocked until source import.
- [Execution order](execution-order.md) — phased safe-first roadmap.
