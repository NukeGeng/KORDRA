---
id: ARCH-REPOSITORY
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Kordra — Root Repository Structure

**Accepted scoped exception:** [ADR-0012](../decisions/0012-clients-git-submodule.md) preserves the `clients/` location but gives its contents independent Git ownership in KORDRA-Clients. Product records a submodule pin. The remainder of this target architecture stays proposed under ADR-0009; the historical monorepo wording does not override that explicit Git split. See [development workflow](../engineering/clients-development.md).

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

## 1. Mục tiêu và nguyên tắc
- Một **monorepo OSS** với nhiều executable host; **module nghiệp vụ không đồng nghĩa microservice**.
- Tham khảo cách tổ chức `BuildingBlocks / Modules / Contracts / Host` của FullStackHero, **không fork/đưa FullStackHero thành runtime dependency** và không thay thế Marten/Wolverine bằng stack của starter kit.
- Phân tách code sản phẩm, clients, công cụ phát triển, tài liệu, hạ tầng cài Kordra, demo và tài nguyên của môi trường thử nghiệm.
- Tạo **theo nhu cầu**: cây bên dưới là mục tiêu, không yêu cầu tạo dự án rỗng hoặc cả 20 modules trong MVP.

## 2. Target layout
```text
kordra/
├── .github/
│   ├── workflows/
│   ├── ISSUE_TEMPLATE/
│   └── pull_request_template.md
├── .codex/                     # optional: AI coding runtime config; không bắt buộc để chạy product
│   └── agents/
├── .editorconfig
├── .gitignore
├── AGENTS.md
├── GOAL.md
├── WORKFLOW.md
├── README.md
├── CONTRIBUTING.md             # tạo trước public contribution
├── SECURITY.md                 # policy nhận báo cáo lỗ hổng
├── LICENSE                     # chốt license trước khi phát hành OSS
├── CHANGELOG.md
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── Kordra.slnx
│   ├── BuildingBlocks/
│   │   ├── Core/
│   │   ├── Persistence/
│   │   ├── Messaging/
│   │   ├── Security/
│   │   └── Observability/
│   ├── Modules/
│   │   ├── IdentityAccess/
│   │   ├── TenantManagement/
│   │   ├── PlatformAdministration/
│   │   ├── ProjectEnvironment/
│   │   ├── InfrastructureInventory/
│   │   ├── ConnectionManagement/
│   │   ├── ArtifactManagement/
│   │   ├── ReleaseManagement/
│   │   ├── ApprovalGovernance/
│   │   ├── DeploymentManagement/
│   │   ├── WorkflowOrchestration/
│   │   ├── Messaging/             # MOD-MSG internal durable delivery
│   │   ├── AgentManagement/
│   │   ├── Reconciliation/
│   │   ├── Observability/
│   │   ├── AuditCompliance/
│   │   ├── CIIntegrations/
│   │   ├── Notifications/
│   │   └── Scheduling/
│   ├── Execution/
│   │   ├── Abstractions/
│   │   ├── Protocols/
│   │   └── Drivers/
│   │       ├── Kubernetes/
│   │       ├── Helm/
│   │       ├── DockerCompose/
│   │       └── Ssh/
│   ├── Host/
│   │   ├── Kordra.Api/
│   │   ├── Kordra.ControlWorker/
│   │   ├── Kordra.Agent/
│   │   ├── Kordra.DbMigrator/
│   │   └── Kordra.AppHost/
│   └── Tests/
│       ├── Unit/
│       ├── Integration/
│       ├── Architecture/
│       ├── Contract/
│       └── EndToEnd/
├── clients/                    # Independent KORDRA-Clients Git submodule (ADR-0012)
│   ├── web/                    # Vue 3
│   ├── desktop/                # Tauri + Vue; shared UI khi có giá trị
│   └── packages/
│       ├── ui/
│       └── api-client/
├── tools/
│   └── kordra-cli/
│
├── deploy/
│   ├── docker/
│   ├── helm/
│   ├── kubernetes/
│   └── terraform/              # optional, chỉ tạo khi triển khai
├── samples/
│   ├── hello-service/
│   └── example-environments/
├── docs/
│   ├── architecture/
│   ├── decisions/
│   ├── contracts/
│   ├── features/
│   ├── engineering/
│   ├── operations/
│   ├── reference-environments/
│   └── tooling/                # current FEAT-AMR-001 location; relocation needs a separate migration decision
└── extensions/                # chỉ thêm khi có extension contract được duyệt
    ├── contracts/
    └── community/
```

## 3. Root ownership (không lẫn trách nhiệm)
| Area | Owns | Does not own |
|---|---|---|
| `src/Modules` | nghiệp vụ, domain invariants, feature handlers, owned persistence | kube CLI scripts, code frontend |
| `src/BuildingBlocks` | technical primitives ổn định, đủ generic | permission nghiệp vụ hoặc shared business services |
| `src/Execution` | typed driver ports, protocols, implementations | release approval hoặc tenant business policies |
| `src/Host` | DI composition, process configuration, startup, host health | business logic mới |
| `clients` | Web/Desktop UI, typed client | authorization source of truth, deploy execution |
| `docs/tooling` | current local AMR and documentation tooling | product runtime / production deployment service |
| `deploy` | cài đặt **chính Kordra** | cấu hình cluster/tenant thật của LAB-01 |
| `docs/reference-environments` | testbed synthetic/redacted evidence | hardcoded endpoints trong product |
| `extensions` | extension public contract và community examples | proprietary code nội bộ của doanh nghiệp |

## 4. Quy tắc định danh và tổ chức file
- Project .NET đặt `Kordra.<Area>.<ModuleOrPurpose>`; namespace khớp project, không copy namespace từ FSH.
- `docs/features/<kebab-module>/` là **source of truth của spec**; đường dẫn PascalCase trong `src/Modules/<ModuleName>/` ánh xạ qua `docs/engineering/project-conventions.md`.
- Mỗi file implementation gắn với một `feature_id` (trong spec và PR), mỗi module có một owner module ID.
- Không chèn IP, registry credentials, kubeconfig, raw business payload hoặc công ty cụ thể vào `samples`/`deploy`/source generic.
- `LICENSE` phải được xác minh quyền sở hữu, dependency notices và contributions trước khi phát hành; **tài liệu này không chốt giấy phép**.

## 5. Tạo mới một phần của repo
1. Xác định feature và module owner trong `docs/features/`.
2. Chọn `src/Modules`, `src/Execution`, `src/Host` hoặc `clients`; nếu không khớp, mở RFC thay vì tạo `Shared` tùy ý.
3. Chỉ thêm project/package có trách nhiệm và dependency rõ ràng, ghi trong solution + tests.
4. Cập nhật source_paths/test_paths trong feature spec **sau khi file thật tồn tại**.
5. Chạy architecture tests, doccheck và changeguard; nếu thay baseline phải có ADR.

## 6. Hạn chế và tiêu chí nghiệm thu
- Folder structure là quy ước kỹ thuật, không phải cơ sở để quyết định permission hoặc security scope.
- Keep the current AMR implementation under `docs/tooling/`; do not create a second copy under `tools/`. A future move must update feature mappings, CI, gate discovery, tests and receipts together through an explicit migration decision.
- `Messaging` in this target means internal Kordra durable delivery (MOD-MSG). Ownership for customer external-broker inventory remains `[NEEDS-DECISION]`; do not infer it from LAB-01.
- Mọi testbed chạy thật phải giữ ngoài repo public nếu chứa bí mật hoặc tài sản công ty.
- Nghiệm thu: build từ root có hướng dẫn; module mapping không mơ hồ; không source dependencies ngược; git diff + docs traceable; cấu trúc mới không đòi tạo 20 projects rỗng.

**See also:** [Solution](solution-architecture.md), [Module internal](module-internal-structure.md), [Dependency rules](dependency-rules.md), [Host composition](host-composition.md), [ADR-0009](../decisions/0009-source-architecture.md).
