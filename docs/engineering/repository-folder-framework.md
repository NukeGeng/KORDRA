---
id: ENG-FOLDER-FRAMEWORK
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-20
---

# Repository folder framework — actual and reserved paths

The maintainer requested the folder framework **after** completing the accepted ADR-0010 bootstrap. That bootstrap passed current full gates, independent native GPT-6.1 Sol High review and contributor triage before these directories were created. This is a directory reservation, not acceptance of broad ADR-0009. It preserves existing functional source.

## Actual project graph

Root `Kordra.slnx` contains nine projects after T01: six product projects and three test projects. IAM principal/session persistence, the explicit DbMigrator and 17 IAM tests are the narrow ADR-0012 T01 addition. Existing 48 Artifact and 85 Inventory Bootstrap tests remain. No AUD or login implementation is part of this commit.

- `src/Modules/ArtifactManagement/Domain/`: immutable digest domain; [FEAT-ART-001](../features/artifact-management/features/immutable-artifact.md).
- `src/Modules/InfrastructureInventory/Contracts/` and its parent runtime: immutable in-process read seam and unavailable reader; [FEAT-INV-001/I1](../features/infrastructure-inventory/features/register-target.md).
- `src/Host/Kordra.Api/`: IPv4 loopback API, constant liveness, uniform inventory denial; no IAM or provider.
- `src/Modules/IdentityAccess/`: IAM-owned principal/session persistence for the accepted T01 slice; no login endpoints or authorization runtime.
- `src/Host/Kordra.DbMigrator/`: explicitly invoked, loopback/role-checked IAM migration host; no API startup migration.
- Root `tests/ArtifactManagement.Domain.Tests/`, `tests/InfrastructureInventory.Bootstrap.Tests/` and `tests/IdentityAccess.Tests/`: 48 + 85 + 17 locally passing tests. Coverage includes project/PE boundaries, compiled negative fixtures, loopback behavior, isolated IAM migration round trip and read-only runtime-role permission probe. This is not remote CI evidence.

## Reserved folders

The original reservation created the paths below with proposed READMEs. `src/Modules/IdentityAccess` and `src/Host/Kordra.DbMigrator` were subsequently adopted for the exact FEAT-IAM-002 T01 slice under ADR-0012 and are documented as implemented paths above. Remaining locations below are still reservations only: their presence does not mean a feature, module registration, driver, UI, deployment or Enterprise product exists. Module names follow the [proposed mapping](project-conventions.md); module specs retain their current status and ownership.

| Reserved path | Canonical owner / adoption prerequisite |
|---|---|
| `src/Modules/TenantManagement` | [module](../features/tenant-management/module.md) |
| `src/Modules/PlatformAdministration` | [module](../features/platform-admin/module.md) |
| `src/Modules/ProjectEnvironment` | [module](../features/project-environment/module.md) |
| `src/Modules/ConnectionManagement` | [module](../features/connection-secrets/module.md) |
| `src/Modules/ReleaseManagement` | [module](../features/release-management/module.md) |
| `src/Modules/ApprovalGovernance` | [module](../features/approval-governance/module.md) |
| `src/Modules/DeploymentManagement` | [module](../features/deployment-management/module.md) |
| `src/Modules/WorkflowOrchestration` | [module](../features/workflow-orchestration/module.md) |
| `src/Modules/Messaging` | [module](../features/messaging/module.md) |
| `src/Modules/AgentManagement` | [module](../features/execution-agents/module.md) |
| `src/Modules/Reconciliation` | [module](../features/reconciliation/module.md) |
| `src/Modules/Observability` | [module](../features/observability/module.md) |
| `src/Modules/CIIntegrations` | [module](../features/ci-integrations/module.md) |
| `src/Modules/Notifications` | [module](../features/notifications/module.md) |
| `src/Modules/Scheduling` | [module](../features/scheduler/module.md) |
| `src/Execution/Drivers` | [module](../features/deployment-drivers/module.md) |
| `src/BuildingBlocks/Core` | Feature/contract decision required |
| `src/BuildingBlocks/Persistence` | Feature/contract decision required |
| `src/BuildingBlocks/Messaging` | Feature/contract decision required |
| `src/BuildingBlocks/Security` | Feature/contract decision required |
| `src/BuildingBlocks/Observability` | Feature/contract decision required |
| `src/Execution/Abstractions` | Feature/contract decision required |
| `src/Execution/Protocols` | Feature/contract decision required |
| `src/Execution/Drivers/Kubernetes` | Feature/contract decision required |
| `src/Execution/Drivers/Helm` | Feature/contract decision required |
| `src/Execution/Drivers/DockerCompose` | Feature/contract decision required |
| `src/Execution/Drivers/Ssh` | Feature/contract decision required |
| `src/Host/Kordra.ControlWorker` | Feature/contract decision required |
| `src/Host/Kordra.Agent` | Feature/contract decision required |
| `src/Host/Kordra.AppHost` | Feature/contract decision required |
| `clients/web` | Feature/contract decision required |
| `clients/desktop` | Feature/contract decision required |
| `clients/packages/ui` | Feature/contract decision required |
| `clients/packages/api-client` | Feature/contract decision required |
| `tools/kordra-cli` | Feature/contract decision required |
| `deploy/docker` | Feature/contract decision required |
| `deploy/helm` | Feature/contract decision required |
| `deploy/kubernetes` | Feature/contract decision required |
| `deploy/terraform` | Feature/contract decision required |
| `samples/hello-service` | Feature/contract decision required |
| `samples/example-environments` | Feature/contract decision required |
| `extensions` | Feature/contract decision required |

## Preserved exceptions and adoption

Keep root `Kordra.slnx`, root `tests/` and `docs/tooling/` as the working baseline. Do not add `src/Kordra.slnx`, duplicate `src/Tests`, `tools/amr`, empty future projects, shared build/package imports or dependencies to fill the tree. No current source is moved. Agent/worker/driver paths have no runnable implementation; any future Agent must remain isolated from Control Plane persistence/domain.

The proposed [repository](../architecture/repository-structure.md), [solution](../architecture/solution-architecture.md), [module structure](../architecture/module-internal-structure.md), [dependency rules](../architecture/dependency-rules.md), [host composition](../architecture/host-composition.md) and [ADR-0009](../decisions/0009-source-architecture.md) guide review. [ADR-0008](../decisions/0008-domain-bootstrap-validation.md), [ADR-0010](../decisions/0010-local-readonly-bootstrap.md) and the exact IAM T01 slice of [ADR-0012](../decisions/0012-operator-pilot-runtime-foundation.md) authorize the current scoped source. Before adding more source: approved feature and technology/boundary decision → exact ownership mapping → implementation/tests → gates/review → evidence. A reserved README is not a source mapping.

The FEAT-IAM-001 design and synthetic policy proof are recorded in the [owning feature](../features/identity-access/features/authorize-resource.md); the maintainer accepted ADR-0003 for the narrow permission/trusted-scope baseline. FEAT-IAM-002 T01 persistence is mapped and locally verified. Login/session behavior, runtime authorization and inventory ingress remain unimplemented/denied. The remaining reserved READMEs do not map or authorize those capabilities.
