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
