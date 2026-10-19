---
id: MOD-INV
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
module_id: MOD-INV
---

# Infrastructure Inventory — module definition

**Module ID:** `MOD-INV` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Infrastructure Inventory owns: **Target, ClusterRef, NodeSnapshot, TargetCapability, OwnershipMode**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
RegisterTarget, DiscoverCapability, ChangeOwnershipMode.

## Outgoing facts / events
TargetRegistered, TargetCapabilityChanged.

## Authorized capabilities
target.read, target.manage. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
project-environment; execution-agents. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Target có tenant + ownership mode; không assume mọi k8s context là reachable/permitted.

## Failure / risk scenarios
Stale agent capability, target re-assigned to wrong tenant, stale cluster namespace. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-INV-001 — Register infrastructure target and discover capability](features/register-target.md)

## Implementation mapping
[FEAT-INV-001/I1](features/register-target.md) maps the actual Contracts/runtime, local API and tests authorized by ADR-0010. Local full gates (19 bootstrap + 48 Artifact tests), independent native High review and triage passed on 2026-10-18; new remote CI is pending. Inventory ingress is denied; target registration, trusted authorization and real discovery remain proposed/unimplemented. The module is not complete.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **inventory discovery / storage / CNPG / Talos**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
