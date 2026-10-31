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
- [Queue / DLQ recovery](messaging-recovery-runbook.md) — T12.1 kernel triage, Unknown custody, bounded retry/retention and fixture cleanup; no live replay/activation authority.
- [Incident response](incident-runbook.md)
- [Backup / restore](backup-restore-runbook.md)
- [Credential rotation](credential-rotation-runbook.md)
- [Local Resource Policy v1](resource-policy-v1.md) — T06 offline manifest/impact/apply/recovery and guarded metadata/environment routes; real operations need separate authorization.
- [Local PostgreSQL setup and role access](local-postgresql-setup.md)

## Personal Nomad lab

[Local Nomad lab](local-nomad-lab.md) owns the explicitly authorized 1-vCPU/2-GiB/15-GiB instance, private credential locations, TLS/ACL evidence, lifecycle and limitations. It is independent of company LAB-01 and not Product provider completion.

## Nomad workload observation

[Nomad workloads v1](nomad-workloads-v1.md) covers explicit local-pilot activation, private trust/Keychain metadata, limits/expiry, optional own-lab native conformance and safe rollback. It does not authorize company access or claim permanent installation/production readiness.

- [Kubernetes workloads v1](kubernetes-workloads-v1.md) — implemented bounded T09.1; synthetic validation, no live-cluster qualification.
