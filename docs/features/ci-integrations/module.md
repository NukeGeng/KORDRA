---
id: MOD-CI
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-CI
---

# CI & Source Integrations — module definition

**Module ID:** `MOD-CI` · **Phase:** Phase 2 · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
CI & Source Integrations owns: **ProviderConnection, WebhookReceipt, PipelineRunRef, CITriggerRule**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
ReceiveWebhook, TriggerPipeline, SyncArtifact.

## Outgoing facts / events
PipelineCompleted, ArtifactDiscovered.

## Authorized capabilities
project.manage, artifact.register. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
artifact-management; connection-secrets. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Webhook signature verified, replay/dedup enforced; CI complete ≠ deployment approval.

## Failure / risk scenarios
Unsigned webhook, rerun delivered twice, CI provider unavailable. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-CI-001 — Ingest signed CI webhooks and discover artifact](features/signed-ci-webhook.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **GitLab adapter truthfulness**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
