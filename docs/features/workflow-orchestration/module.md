---
id: MOD-WFL
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-WFL
---

# Workflow Orchestration — module definition

**Module ID:** `MOD-WFL` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Workflow Orchestration owns: **WorkflowInstance, WorkflowStep, Timer, TargetLease, RecoveryDecision**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
BeginWorkflow, DispatchOperation, ResumeWorkflow, ResolveUnknownOutcome.

## Outgoing facts / events
WorkflowTransitioned, OperationDispatched, OutcomeUnknown.

## Authorized capabilities
deployment.execute. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
messaging; approval-governance; execution-agents. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Durable monotonic state; CAS transitions; lease+fencing on target; unknown != failed.

## Failure / risk scenarios
Worker crash, duplicate dispatch, lease takeover, canceled workflow active in target. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-WFL-001 — Durable deployment workflow through failures](features/durable-workflow.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
