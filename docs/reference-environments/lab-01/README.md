---
id: REF-LAB-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — Kubernetes/Talos hybrid deployment reference

## Mục tiêu
Dùng hiện trạng do người dùng cung cấp ngày **2026-10-15** làm môi trường tham chiếu đo lường, thiết kế adapter, test UI/permissions/reliability. **Chưa truy cập cluster, source hoặc file inventory gốc trong lượt này**. Mọi thông số được phân loại `reported / verified / unknown`.

## Hồ sơ thông tin
- `reference_environment_id`: `lab-01` (tên ẩn danh của một môi trường; **không phải tenant ID**).
- `classification`: `existing-shared-infrastructure`; môi trường nguồn có thể phục vụ ứng dụng thực tế.
- `default_mode`: `OBSERVE_ONLY`; mọi thay đổi trạng thái mặc định `DENY`.
- Tài liệu chi tiết đã được mô tả nằm trên máy người dùng: `DEVOPS_CONTROL_PLANE_INVENTORY.md`. Đường dẫn máy Mac là local, không tồn tại trong ZIP; hãy **import bản đã loại bỏ secret** và xác minh timestamp + 13 queue trước khi chạy case tương ứng.

## Đọc theo thứ tự
1. [Reported baseline](reported-baseline.md) → [flows and target ownership](flows.md).
2. [Connector coverage](connector-map.md) → [Profile contract](profile.example.yaml).
3. [Risks/unknowns](risk-register.md) → [Message topology](messaging-boundaries.md).
4. [Experiment plan](experiments/README.md) → [Safety and change control](experiments/safety-and-change-control.md) → [Coverage matrix](experiments/coverage-matrix.csv).

**Status semantics:** `reported`: do người dùng/tài liệu nguồn báo cáo; `verified`: thu thập evidence trực tiếp và được review; `unknown`: chưa đủ dữ liệu. `Ready` không đồng nghĩa production safety, `3 broker` không đồng nghĩa queue đã HA.

- [Source import checklist](experiments/import-and-provenance.md) — only the user-supplied summary is available; missing exact details block verification.
