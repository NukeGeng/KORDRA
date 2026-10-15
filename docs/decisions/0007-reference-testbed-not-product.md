---
id: ADR-0007
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# ADR-0007 — Reference testbed vs product architecture

## Context
Có cluster thực tế chạy Talos/Kubernetes, Helm, RabbitMQ/VerneMQ, CNPG, NFS, Valkey, Prometheus/Loki, hiện có tool Python/SQLite. Nếu sao chép topology hoặc queue business thành lõi sản phẩm sẽ gây vendor/company lock-in, sai quyền và mất tính tái sử dụng.

## Proposed decision
- Xem LAB-01 là **external reference environment**, không phải module domain hoặc mặc định hạ tầng của sản phẩm.
- Dùng `EnvironmentProfile` + `ConnectorCapabilities` + `TargetOwnership` + permission scopes; testbed metadata ngoài sản phẩm.
- P0 observe only; P1 sandbox; P2 isolated clone; P3 optional change-controlled pilot.
- MQTT business bus, RabbitMQ business queues, SignalR telemetry tách biệt Control Plane command/event transport.
- Status `reported` không được tự coi `verified`. Không tự động sửa các rủi ro cluster.

## Alternatives rejected
1. Sao chép toàn bộ stack hiện tại vào Control Plane: coupling cao, không áp dụng cho công ty dùng Docker/SSH/Jenkins.
2. Tự động thay đổi hạ tầng thật để chạy test: nguy cơ gián đoạn và dữ liệu.
3. Bỏ qua hệ thống thật và chỉ test mock: không kiểm chứng được integration boundaries/operation constraints.

## Consequences
Tăng yêu cầu contract/capability tests và quản lý evidence, đổi lại kiểm chứng thực tế mà không phá domain abstraction. Yêu cầu reviewer chấp nhận ADR trước khi trạng thái chuyển `accepted`.
