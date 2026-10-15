---
id: MOD-OBS
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-OBS
---

# Observability & Health — module definition

**Module ID:** `MOD-OBS` · **Phase:** MVP core / Phase 2 full · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Observability & Health owns: **HealthPolicy, HealthProbeResult, TelemetryEnvelope, StatusProjection**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
RecordObservation, VerifyHealth, QueryOperations.

## Outgoing facts / events
TargetObserved, VerificationFailed.

## Authorized capabilities
project.read, target.read. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
deployment-management; execution-agents. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Observed timestamp and freshness mandatory; container running != business healthy.

## Failure / risk scenarios
Stale health, telemetry flood, leaking tenant logs through realtime channels. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-OBS-001 — Verify deployment health and publish scoped status](features/health-verification.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **Prometheus, Loki, Alertmanager, HPA, Jaeger/OTel detection**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
