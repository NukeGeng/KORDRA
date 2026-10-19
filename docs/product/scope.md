---
id: PROD-SCOPE
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Product scope, stages and non-goals

## Product boundary
Kordra owns release intent, immutable artifact binding, approval, secure execution orchestration, observed deployment verification, rollback decisioning and audit. It integrates existing CI, registry, Kubernetes/Helm, Docker Compose/SSH and operations data through explicit target capabilities.

## Target personas
Developer requests a release; Release Approver approves within scope; Operator verifies and recovers; Tenant Admin manages organization access; Platform Operator has platform-scoped responsibilities; machine agent executes scoped operations. Roles are **templates**, never hardcoded authorization switches.

## Prioritized private Operator Pilot (proposal)
Near-term engineering focus is a generic **Mac-first operator UI** for authorized, existing environments: OP-P0 read-only environment profiles, cluster/registry/service health and digest/checksum version comparison; OP-P1 controlled release request/approval queue, deploy→verify→rollback and audit on **isolated approved targets**. OP-P2 backup readiness/test restore and OP-P3 HPA/HA change planning remain later. Do not turn internal Proxmox/Talos/DB administration into mandatory OSS Beta capability. The remote durable worker must own long-running operations; Mac UI is not a background-job guarantee.

The owner selected Kubernetes/Helm read-only with an API local on Mac first as the OP-P0 design direction on 2026-10-18; see the [recorded scope and remaining decisions](operator-pilot-plan.md#first-op-p0-target--owner-preference-recorded-2026-10-18). This is not a provider implementation, authorization grant or selection of the first write-capable Beta driver.

Company use requires rights-owner permission, explicit environment credentials, private profiles and separate operation approvals. Generic definitions are in [Operator Workstation](operator-workstation.md) and [Private pilot plan](operator-pilot-plan.md); actual company system details must **not** be committed here.

## OSS Beta (proposed narrow slice)
- One independently installable and tested non-production deploy/verify/rollback experience, with source and artifact traceability.
- Minimal trusted organization/project/environment/target references and scoped authorization.
- Artifact digest/repository association, approved immutable deployment spec, controlled execution, health checks and audit.
- One driver end-to-end first (Helm/Kubernetes or Compose/SSH); second adapter follows through capability contract and tests.
- Durable execution or equivalent crash/retry-safe operation tracking before declaring any workflow safe for shared/production use.

## Product expansion stages
- **Production-readiness:** tenant isolation hardening, operability, emergency restore, incident/backup/upgrade procedures and security evidence.
- **Post-Beta integrations:** CI provider hooks, GitOps providers, Prometheus/Loki correlation, inventory and notifications based on validated demand.
- **Enterprise:** SSO/governance/compliance/advanced organization policies and support.
- **Cloud:** managed multi-tenant control plane and optional subscriptions only after readiness and demand.

## Initial non-goals
Do not replace Jenkins/GitLab build engines, Argo CD/Flux reconciler, Kubernetes scheduler, Prometheus/Loki storage engine, DB migration/backup products or all public cloud controls. No generic arbitrary-shell SaaS automation and no customer production changes under LAB-01 testing.

## Phase authority
This scope page is proposed. Accepted feature/contracts/ADRs remain authoritative for engineering invariants; the owner must approve any narrowed or expanded product release scope before treating it as binding.

See [positioning](positioning.md), [OSS Beta acceptance](oss-beta-acceptance.md), [full A–F technology landscape](technology-landscape.md) and [roadmap](roadmap.md).
