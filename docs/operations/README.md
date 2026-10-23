---
id: OPS-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Operations & Runbook index

Runbook phải có: **trigger**, blast radius, preflight, safe actions, verification, rollback/stop condition, escalation, evidence/incident link. Các file dưới đây là hướng dẫn thiết kế, không chứng minh môi trường thật đã tồn tại.

- [Deployment & validation](deployment-runbook.md)
- [Rollback](rollback-runbook.md)
- [Agent offline](agent-offline-runbook.md)
- [Queue / DLQ recovery](messaging-recovery-runbook.md)
- [Incident response](incident-runbook.md)
- [Backup / restore](backup-restore-runbook.md)
- [Credential rotation](credential-rotation-runbook.md)
- [Local Resource Policy v1](resource-policy-v1.md) — T06 offline manifest/impact/apply/recovery and guarded metadata/environment routes; real operations need separate authorization.
- [Local PostgreSQL setup and role access](local-postgresql-setup.md)
