---
id: ENG-PROJECT-CONVENTIONS
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Kordra — Project, Namespace, Feature & Naming Conventions

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

## 1. Source ↔ docs mapping
`src/Modules/<PascalCaseModule>/` maps to `docs/features/<kebab-case-module>/`. A stable **module_id** lives in its `module.md`; folder name alone must not be used to determine authorization or ownership.

| Source folder | Documentation owner |
|---|---|
| `IdentityAccess` | `identity-access` |
| `TenantManagement` | `tenant-management` |
| `PlatformAdministration` | `platform-admin` |
| `ProjectEnvironment` | `project-environment` |
| `InfrastructureInventory` | `infrastructure-inventory` |
| `ConnectionManagement` | `connection-secrets` |
| `ArtifactManagement` | `artifact-management` |
| `ReleaseManagement` | `release-management` |
| `ApprovalGovernance` | `approval-governance` |
| `DeploymentManagement` | `deployment-management` |
| `WorkflowOrchestration` | `workflow-orchestration` |
| `Messaging` | `messaging` |
| `AgentManagement` | `execution-agents` |
| `Reconciliation` | `reconciliation` |
| `Observability` | `observability` |
| `AuditCompliance` | `audit-compliance` |
| `CIIntegrations` | `ci-integrations` |
| `Notifications` | `notifications` |
| `Scheduling` | `scheduler` |
| `Execution/Drivers` | `deployment-drivers` |

> Mapping đề xuất, phải đối chiếu và cập nhật cùng ADR trước khi enforce trên source đang tồn tại. `Messaging` maps to MOD-MSG and owns Kordra's internal envelope, routing, inbox/outbox and dead-letter delivery. `[NEEDS-DECISION]` External broker inventory/topology metadata has no confirmed source owner; do not map it to MOD-MSG or derive a generic module from LAB-01.

## 2. Naming rules
| Item | Convention | Example |
|---|---|---|
| .NET runtime module | `Kordra.Modules.<Name>` | `Kordra.Modules.DeploymentManagement` |
| contracts assembly | `.Contracts` suffix | `Kordra.Modules.DeploymentManagement.Contracts` |
| host | `Kordra.<Purpose>` | `Kordra.ControlWorker` |
| feature directory | verb + business object | `RequestRollback` |
| domain event | past-tense business occurrence | `DeploymentApproved` |
| command | imperative intent | `ExecuteDeployment` |
| public message | versioned in catalog | `deployment.dispatch.v1` |
| unit test | `{Behavior}_When{Condition}_{Outcome}` | `Execute_WhenLeaseExpired_Rejects` |
| infra binding | configuration or `secretRef` | `RegistryCredentialRef` |

Names must be explicit: avoid `CommonService`, `HelperService`, `MiscController`, `BaseEntityRepository` without real bounded abstraction.

## 3. Feature organization
- A feature directory owns handler/validation/endpoint of one use case. A feature may invoke domain policies and persistence abstractions; do not duplicate rules across handlers.
- Create new `docs/features/<owner>/features/<feature>.md` before public contract behavior is implemented.
- `source_paths` and `test_paths` must be actual verified repo paths, using exact paths and no guessed wildcard; tests reference feature IDs in names/metadata where practical.
- Separate internal `Domain.Events` (event sourcing) and published `Contracts.Events` (integration schema); do not expose internal types by default.

## 4. Code & technology discipline
- Net SDK pinned via `global.json`; package versions centralized in `Directory.Packages.props` when adopted.
- Nullable reference types, structured logging/correlation, cancellation token, deterministic clock for time-sensitive tests.
- Domain cannot depend on Marten/Wolverine/provider SDK; adapter includes their integration.
- Configuration fail-closed on critical secrets/authorization; no role string checks in endpoints/handlers.
- Prefer existing libraries over custom reflection-based framework and magic convention.

## 5. Review checklist
Before PR: project ref checked; public contract versioned; bounded context owner confirmed; tenant scope/actor/permission explicit; negative/retry tests; docs & test links updated; no real credentials or endpoint leaks; no unrequested architecture migration.

**See also:** [Repository](../architecture/repository-structure.md), [Module structure](../architecture/module-internal-structure.md), [Source of truth](../governance/source-of-truth.md).
