---
id: REF-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Reference environments — testbed registry

**Purpose:** hồ sơ môi trường thật / mô phỏng để kiểm thử các tính năng Control Plane. **Không** biến hiện trạng một công ty thành yêu cầu kiến trúc sản phẩm. Mỗi môi trường có danh tính độc lập, scope, evidence và owner.

- [LAB-01 — reference environment](lab-01/README.md): baseline báo cáo, không phải kết quả xác minh do Control Plane tự thu thập.
- [Test plan](lab-01/experiments/README.md): cách chạy quan sát, mô phỏng, triển khai có kiểm soát.
- [Scenario coverage CSV](lab-01/experiments/coverage-matrix.csv): bảng ID cụ thể, loại quyền, mức nguy cơ, feature owner và bằng chứng.
- [Safety rules](lab-01/experiments/safety-and-change-control.md): điều kiện cho phép chạy và điều kiện dừng.

## Tách ba sự thật
1. **Product specification** nằm tại `docs/architecture/`, `docs/features/`, `docs/contracts/`.
2. **Environment evidence** nằm tại `docs/reference-environments/<env>/`: quan sát theo thời điểm, có thể lỗi thời.
3. **Experiment result** nằm ở nơi lưu evidence được phê duyệt, dẫn ID/timestamp/commit; không ghi kết quả chạy giả hoặc đưa token/thiết bị/người dùng vào repo.

Một deployment driver được chấp nhận khi vượt contract tests trên fixture và ít nhất một môi trường phù hợp; không buộc mọi môi trường phải có Talos, VerneMQ, RabbitMQ, Grafana hay CNPG.
