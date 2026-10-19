---
id: FEATURE-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Module and Feature Registry

## Ownership by module
Each module has exactly one `module.md` and feature specs under `features/`. The registry is designed to expand without moving behavior into unowned `shared` code.

| Module ID | Module | Example feature | Phase |
|---|---|---|---|
| `MOD-IAM` | [Identity & Access](identity-access/module.md) | [FEAT-IAM-001](identity-access/features/authorize-resource.md) | MVP |
| `MOD-TEN` | [Tenant & Membership](tenant-management/module.md) | [FEAT-TEN-001](tenant-management/features/membership-lifecycle.md) | MVP |
| `MOD-PAD` | [Platform Administration](platform-admin/module.md) | [FEAT-PAD-001](platform-admin/features/support-elevation.md) | Enterprise foundation |
| `MOD-PRJ` | [Project, Application & Environment](project-environment/module.md) | [FEAT-PRJ-001](project-environment/features/environment-catalog.md) | MVP |
| `MOD-INV` | [Infrastructure Inventory](infrastructure-inventory/module.md) | [FEAT-INV-001](infrastructure-inventory/features/register-target.md) | MVP |
| `MOD-SEC` | [Connections & Secret References](connection-secrets/module.md) | [FEAT-SEC-001](connection-secrets/features/secret-binding.md) | MVP |
| `MOD-ART` | [Artifact Management](artifact-management/module.md) | [FEAT-ART-001](artifact-management/features/immutable-artifact.md) | MVP |
| `MOD-REL` | [Release Management](release-management/module.md) | [FEAT-REL-001](release-management/features/create-release.md) | MVP |
| `MOD-APR` | [Approval & Policy Governance](approval-governance/module.md) | [FEAT-APR-001](approval-governance/features/approve-deployment.md) | MVP |
| `MOD-DEP` | [Deployment Management](deployment-management/module.md) | [FEAT-DEP-001](deployment-management/features/deploy-rollback.md) | MVP |
| `MOD-WFL` | [Workflow Orchestration](workflow-orchestration/module.md) | [FEAT-WFL-001](workflow-orchestration/features/durable-workflow.md) | MVP |
| `MOD-MSG` | [Messaging Infrastructure](messaging/module.md) | [FEAT-MSG-001](messaging/features/durable-delivery.md) | MVP |
| `MOD-AGT` | [Agent Management & Gateway](execution-agents/module.md) | [FEAT-AGT-001](execution-agents/features/agent-enrollment.md) | MVP |
| `MOD-DRV` | [Deployment Driver Integrations](deployment-drivers/module.md) | [FEAT-DRV-001](deployment-drivers/features/helm-compose-drivers.md) | MVP |
| `MOD-REC` | [Desired/Observed Reconciliation](reconciliation/module.md) | [FEAT-REC-001](reconciliation/features/drift-detection.md) | Phase 2 |
| `MOD-OBS` | [Observability & Health](observability/module.md) | [FEAT-OBS-001](observability/features/health-verification.md) | MVP core / Phase 2 full |
| `MOD-AUD` | [Audit & Governance Ledger](audit-compliance/module.md) | [FEAT-AUD-001](audit-compliance/features/immutable-audit.md) | MVP |
| `MOD-CI` | [CI & Source Integrations](ci-integrations/module.md) | [FEAT-CI-001](ci-integrations/features/signed-ci-webhook.md) | Phase 2 |
| `MOD-NOT` | [Notifications](notifications/module.md) | [FEAT-NOT-001](notifications/features/deployment-notifications.md) | Phase 2 |
| `MOD-SCH` | [Scheduler & Change Windows](scheduler/module.md) | [FEAT-SCH-001](scheduler/features/scheduled-deployment.md) | Phase 2 |

**Phase interpretation:** existing `MVP` metadata means design-horizon planning, **not** that every one of the 20 documented modules must ship for first OSS Beta. Beta priorities are [scope](../product/scope.md), [release criteria](../product/oss-beta-acceptance.md) and [A–F technology landscape](../product/technology-landscape.md).

## New feature procedure
Copy [`templates/feature.md`](../templates/feature.md) into `features/<module>/features/<slug>.md`, add it to module registry and `docs/engineering/traceability.md`, then run `python3 docs/tooling/doccheck.py`. New module only after boundary review/ADR.
