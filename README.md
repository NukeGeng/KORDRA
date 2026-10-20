[![Kordra — DevOps Control Plane](banner.png)](https://kordra.id.vn)

<div align="center">

# Kordra

**Điều phối release. Kiểm soát triển khai. Truy vết mọi thay đổi.**

*One Control Plane. Any Infrastructure.*

[kordra.id.vn](https://kordra.id.vn) · [Tài liệu](docs/README.md) · [Kiến trúc](docs/architecture/README.md) · [Roadmap](docs/product/roadmap.md)

[![Documentation integrity](https://github.com/NukeGeng/KORDRA/actions/workflows/docs-ci.yml/badge.svg?branch=main)](https://github.com/NukeGeng/KORDRA/actions/workflows/docs-ci.yml)
[![Product validation](https://github.com/NukeGeng/KORDRA/actions/workflows/product-ci.yml/badge.svg?branch=main)](https://github.com/NukeGeng/KORDRA/actions/workflows/product-ci.yml)

</div>

Kordra hướng tới một **DevOps Control Plane** giúp tổ chức quản lý hành trình từ artifact đến release, phê duyệt, triển khai và phục hồi trên hạ tầng hiện có. Kubernetes, Helm, Docker Compose/SSH và các CI provider là những đích tích hợp trong thiết kế.

Kordra tập trung vào **ý định triển khai, policy, approval và audit**, đồng thời tích hợp với các công cụ CI/CD và execution hiện có. Mỗi thay đổi cần gắn với artifact bất biến, đúng target và phạm vi quyền đã xác nhận.

> **Giai đoạn hiện tại: Foundation / phát triển ban đầu.** Repository có tài liệu thiết kế, công cụ AMR, domain increment .NET và bootstrap API loopback chạy cục bộ. Chưa có ứng dụng backend/UI hoàn chỉnh hoặc bản triển khai production. Các khả năng bên dưới là định hướng, trừ phần được ghi rõ đã triển khai.

OSS Beta là mốc tương lai. Tại baseline `2aa156f4`, repo chưa có `LICENSE`; lựa chọn giấy phép và quyền phát hành vẫn cần quyết định của chủ dự án.

## Ưu tiên trước mắt: Private Operator Pilot

Định hướng v1.5 là phục vụ nhu cầu vận hành thực tế trước, sau đó tái sử dụng phần lõi đủ tổng quát cho OSS Beta. Khởi đầu có thể là Vue Web UI trên trình duyệt Mac; Tauri là lựa chọn đóng gói sau. Client chỉ hiển thị và gửi yêu cầu; workflow dài cần backend/worker hoặc provider job độc lập để tiếp tục khi Mac đóng máy.

| Bước đề xuất | Kết quả hướng tới |
| --- | --- |
| **OP-P0** | Profile kết nối, inventory, health, version, logs và backup status — read-only. |
| **OP-P1** | Candidate → hàng chờ → duyệt → deploy → verify → history/rollback trên target thử nghiệm được phép. |
| **OP-P2** | Backup dữ liệu/cấu hình, checksum, lưu trữ và thử restore trong sandbox. |
| **OP-P3** | HPA/HA/capacity preflight, plan/dry-run và thay đổi có kiểm soát. |
| **OP-P4** | Workflow editor và automation broker/VM/worker khi có nhu cầu, quyền hạn và thiết kế được duyệt. |

Đây là **kế hoạch**, chưa phải chức năng đang chạy. [10 nhóm yêu cầu OP-01–OP-10](docs/product/operator-workstation.md) và [kế hoạch pilot](docs/product/operator-pilot-plan.md) được đối chiếu với [43 nhóm công nghệ DevOps A–F](docs/product/technology-landscape.md). Cấu hình, mã nguồn và dữ liệu công ty chỉ được dùng theo quyền được cấp; chỉ phần có quyền sở hữu rõ ràng, đủ tổng quát và đã kiểm thử mới được xem xét đưa vào OSS Core.

## Kordra hướng tới điều gì?

| Khả năng | Mục tiêu thiết kế |
| --- | --- |
| **Application & environment catalog** | Tổ chức Project → Application → Environment → Target theo ownership rõ ràng. |
| **Immutable releases** | Pin artifact bằng digest/hash; release đã duyệt giữ nguyên artifact và cấu hình được phê duyệt. |
| **Policy & approval** | Kiểm soát thay đổi theo permission, resource scope và điều kiện của môi trường. |
| **Durable deployment workflows** | Điều phối retry, timeout, crash recovery và unknown outcome với trạng thái có thể truy vết. |
| **Agents & deployment drivers** | Thực thi trên hạ tầng qua adapter có capability, target scope và approval được kiểm soát. |
| **Operations & audit** | Theo dõi observed state, drift, lịch sử thay đổi và phục hồi theo policy của target. |

Các ranh giới tenant/platform, data ownership và messaging được mô tả tại [bounded contexts](docs/architecture/bounded-contexts.md), [security & multi-tenancy](docs/architecture/security-multitenancy.md) và [messaging](docs/architecture/messaging.md). Trạng thái phê duyệt của từng tài liệu và ADR quyết định nội dung nào đã là baseline.

## Những gì đã có trong repository

- **Artifact identity domain — FEAT-ART-001/I1:** thư viện .NET kiểm tra cú pháp OCI digest SHA-256/SHA-512, đối chiếu nội dung/độ dài và giữ tính bất biến; có test project riêng. Phần này chưa bao gồm registry discovery, artifact registration hay provenance verification. Xem [feature và bằng chứng kiểm thử](docs/features/artifact-management/features/immutable-artifact.md).
- **Local API bootstrap — FEAT-INV-001/I1:** API chỉ nghe IPv4 loopback, liveness trả `alive`, inventory luôn từ chối 403. Có Contracts/runtime seam và 19 bootstrap tests; chưa có đăng nhập, đọc credential hoặc cluster thật. [ADR-0010](docs/decisions/0010-local-readonly-bootstrap.md) được duyệt đúng phạm vi này; bằng chứng local chưa là GitHub CI của bản chưa push.
- **AMR v4 engineering tooling:** Luna Max triển khai và điều phối GOAL; Sol High thiết kế có phạm vi khi cần quyết định mới; Codex Auto PR Review review PR. Context continuity, checkpoint và quality gates nằm trong `docs/tooling/`. Workflow dùng một coordinator, không dùng collaboration sub-agent; native/provider review chỉ được xác nhận khi có runtime evidence. Đây là công cụ hỗ trợ phát triển, không phải runtime của sản phẩm; build/tests không cần model credentials. Xem [AMR guide](docs/engineering/ai-orchestration/README.md).
- **Tài liệu kiến trúc và governance:** module/feature specifications, contracts, ADRs, traceability và runbooks. Source & Solution Architecture v1.2 cùng [ADR-0009](docs/decisions/0009-source-architecture.md) vẫn đang **proposed**.
- **CI validation:** kiểm tra tính toàn vẹn tài liệu, source-to-feature mapping, AMR tests và domain build/tests. Badge ở trên phản ánh trạng thái workflow trên `main`.

## Bắt đầu phát triển

Cần Git, Python 3.11+ và .NET SDK theo [global.json](global.json). Python tooling dùng standard library; lần restore .NET đầu tiên cần tải các test dependencies đã pin.

```bash
git clone https://github.com/NukeGeng/KORDRA.git
cd KORDRA

# Kiểm tra tài liệu
python3 docs/tooling/doccheck.py --strict-sources

# Build và kiểm thử các increment hiện có
dotnet restore Kordra.slnx --locked-mode
dotnet build Kordra.slnx --configuration Release --no-restore
dotnet test Kordra.slnx --configuration Release --no-build --no-restore

# Chạy toàn bộ quality gates cục bộ
python3 docs/tooling/amr.py gates
```

Chạy API bootstrap sau khi build:

```bash
dotnet run --project src/Host/Kordra.Api/Kordra.Api.csproj --configuration Release --no-build --no-launch-profile
# Trong terminal khác:
curl http://127.0.0.1:5127/health/live
```

Liveness chỉ xác nhận process đang chạy; inventory trả 403 kể cả khi thêm token/tenant giả. API dừng khi process hoặc Mac dừng. Chưa có UI, IAM, cluster reader hoặc worker bền vững. Phạm vi được duyệt nằm trong [ADR-0008](docs/decisions/0008-domain-bootstrap-validation.md) và [ADR-0010](docs/decisions/0010-local-readonly-bootstrap.md); persistence, authentication, messaging và deployment stack cần quyết định riêng.

## Bản đồ repository hiện tại

```text
KORDRA/
├── src/
│   ├── Modules/ArtifactManagement/Domain/ # Actual digest domain
│   ├── Modules/InfrastructureInventory/  # Actual runtime + Contracts/
│   ├── Modules/<other-module>/           # Reserved README only
│   ├── Host/Kordra.Api/                  # Actual local API
│   ├── Host/<other-host>/                # Reserved README only
│   ├── BuildingBlocks/                   # Reserved README only
│   └── Execution/                        # Reserved README only
├── tests/                               # Actual Artifact + bootstrap tests
├── clients/                             # Independent KORDRA-Clients Git submodule
├── tools/kordra-cli/                     # Reserved; AMR remains docs/tooling
├── deploy/                              # Reserved Kordra deployment assets
├── samples/                             # Reserved synthetic examples
├── extensions/                          # Reserved; no Enterprise implementation
├── docs/                                # Specs, ADRs, contracts, AMR/tooling
├── .github/workflows/                    # Docs + product validation
├── Kordra.slnx                           # Six actual projects
├── GOAL.md
├── AGENTS.md
└── WORKFLOW.md
```

[Khung thư mục hiện tại](docs/engineering/repository-folder-framework.md) phân biệt source thật và 45 vị trí chỉ dành trước. Root solution, root tests và AMR giữ nguyên; [ADR-0009](docs/decisions/0009-source-architecture.md) vẫn proposed.

`clients/` là Git submodule của [KORDRA-Clients](https://github.com/NukeGeng/KORDRA-Clients), giữ đúng vị trí web/desktop/packages trong cây root. Khi cần frontend, clone bằng `git clone --recurse-submodules https://github.com/NukeGeng/KORDRA.git`. Phiên backend mở root KORDRA; phiên frontend mở riêng `KORDRA/clients`. Xem [hướng dẫn phát triển độc lập](docs/engineering/clients-development.md) và [ADR-0012](docs/decisions/0012-clients-git-submodule.md). Đây là scaffold được tách repo; chưa có UI runtime.

## Tài liệu và đóng góp

Đọc theo thứ tự [GOAL](GOAL.md) → [mục lục tài liệu](docs/README.md) → [source of truth](docs/governance/source-of-truth.md) → [WORKFLOW](WORKFLOW.md), đồng thời tuân thủ [AGENTS.md](AGENTS.md). Trước khi sửa source, xác định feature/module sở hữu, các contract bị ảnh hưởng và test tương ứng. Cập nhật tài liệu cùng thay đổi và gửi PR với kết quả kiểm tra thực tế.

| Bạn muốn tìm | Bắt đầu tại |
| --- | --- |
| Phạm vi sản phẩm và thứ tự triển khai | [Product scope](docs/product/scope.md) · [Roadmap](docs/product/roadmap.md) |
| Nhu cầu vận hành và pilot trên Mac | [Operator Workstation](docs/product/operator-workstation.md) · [Pilot plan](docs/product/operator-pilot-plan.md) |
| Định hướng OSS Beta và phạm vi công nghệ | [Beta acceptance](docs/product/oss-beta-acceptance.md) · [DevOps A–F](docs/product/technology-landscape.md) |
| Kiến trúc và các quyết định đang mở | [Architecture](docs/architecture/README.md) · [ADRs](docs/decisions/README.md) |
| Ownership và yêu cầu từng feature | [Feature registry](docs/features/README.md) |
| Công cụ kiểm tra và AI workflow | [Tooling](docs/tooling/README.md) · [AMR v4](docs/engineering/ai-orchestration/README.md) |
| Vận hành và thử nghiệm | [Operations](docs/operations/README.md) · [Reference environments](docs/reference-environments/README.md) |

Production operations cần phê duyệt rõ ràng của con người. Không đưa secret, kubeconfig hoặc dữ liệu khách hàng vào source, tài liệu hay logs. Thử nghiệm trên môi trường tham chiếu phải theo [quy tắc an toàn LAB-01](docs/reference-environments/lab-01/experiments/safety-and-change-control.md).

---

<div align="center">

**Kordra** · [kordra.id.vn](https://kordra.id.vn)

</div>
