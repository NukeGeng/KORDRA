# DevOps Control Plane — documentation-first scaffold

Bộ đặc tả dùng để khởi tạo một **nền tảng điều phối triển khai và quản trị hạ tầng** cho Kubernetes, Helm, Docker Compose/SSH và CI providers. Đây là **kiến trúc đề xuất**; tài liệu **không chứng minh code đã được implement**.

**Bắt đầu tại:** [docs/README.md](docs/README.md) → [AGENTS.md](AGENTS.md) → [GOAL.md](GOAL.md) → [WORKFLOW.md](WORKFLOW.md).

- **Nguyên tắc:** tài liệu của feature phải nằm cạnh module sở hữu; cross-cutting concern thuộc `docs/architecture/` hoặc `docs/contracts/`.
- **Kiểm tra:** `python3 docs/tooling/doccheck.py` (Python stdlib, không cần cài package).
- **Quyết định chưa phê duyệt:** mọi tài liệu mang `status: proposed` cần xác nhận trước khi xem là baseline.
- **Công nghệ tham khảo:** .NET 10, PostgreSQL, Marten, Wolverine; RabbitMQ khi cần phân phối nhiều process; Vue + Tauri chỉ là client. Lựa chọn cuối cùng phải có ADR accepted.
- **Cấu trúc repo:** có thể ghép nguyên `docs/`, `AGENTS.md`, `GOAL.md`, `WORKFLOW.md`, `.github/` vào repo dự án; không tự động sửa source.

> Không đưa token, kubeconfig, mật khẩu, URL nội bộ, tên khách hàng hoặc bí mật vào tài liệu. Dùng ví dụ giả và `secretRef`.

## LAB-01: Reference environment for safe experiments
Use [LAB-01 documentation](docs/reference-environments/lab-01/README.md) and [test coverage](docs/reference-environments/lab-01/experiments/README.md) when validating drivers on Talos/Kubernetes/Docker/MQTT/RabbitMQ/observability. **Reported baseline only; never treat these files as active cluster measurements.**

## First product validation increment
[FEAT-ART-001/I1](docs/features/artifact-management/features/immutable-artifact.md) adds a pure immutable OCI digest domain library and its tests. It is a local validation increment under [proposed ADR-0008](docs/decisions/0008-domain-bootstrap-validation.md), not a deployed backend or complete artifact-registration feature. Development order and actual module status are tracked in the existing [roadmap](docs/product/roadmap.md).

```bash
dotnet restore Kordra.slnx --locked-mode
dotnet build Kordra.slnx --configuration Release --no-restore
dotnet test Kordra.slnx --configuration Release --no-build --no-restore
python3 docs/tooling/amr.py gates
```

The SDK is pinned in global.json. Runtime library dependencies remain limited to the .NET framework; pinned test-only dependencies and lock files are described in ADR-0008. No production infrastructure is needed for these checks. Human acceptance of technology/policy ADRs is required before adoption and subsequent integration.
