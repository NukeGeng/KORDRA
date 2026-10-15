---
id: GOV-ADR
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Quy trình ra quyết định kiến trúc

Cần ADR khi thay đổi: boundary, persistence, transport, isolation strategy, role/scope model, execution protocol, retry semantics, deployment ownership, rollback policy, public contract; không cần ADR cho thay đổi wording hoặc bug không thay invariant.

ADR chứa: context/problem, decision, options & tradeoffs, consequences, migration, reversal plan, stakeholders, status/effective date. Đặt tại [`decisions/`](../decisions/README.md), **không ghi đè quyết định cũ**: tạo ADR mới và `Supersedes: ADR-xxxx`.

Trước khi merge: security reviewer duyệt thay đổi tenant/authorization; module owners duyệt cross-context contract; ops owner duyệt rollout/recovery.
