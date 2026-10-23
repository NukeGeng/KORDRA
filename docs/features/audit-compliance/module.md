---
id: MOD-AUD
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-21
module_id: MOD-AUD
---

# Audit & Governance Ledger — module definition

**Module ID:** `MOD-AUD` · **Phase:** MVP · **Owner:** NukeGeng (accountable for ADR-0012 Foundation T01–T07; wider module scope remains proposed).

## Responsibility and boundary
Audit & Governance Ledger owns: **AuditRecord, PolicyDecisionAudit, OperationTimeline**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
AppendAuditRecord, ExportAudit.

## Outgoing facts / events
AuditRecorded.

## Authorized capabilities
audit.read. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
identity-access; deployment-management. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Every privileged mutation audited with actor, reason, tenant, target, correlation and result; no plaintext secrets.

## Failure / risk scenarios
Audit sink down, unauthorized purge, event timeline ambiguous. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-AUD-001 — Tamper-evident privileged action audit trail](features/immutable-audit.md)

## Implementation mapping
FEAT-AUD-001 maps the T02 append/lookup foundation to actual AUD, DbMigrator and PostgreSQL test paths; its 34 isolated tests and full deterministic gates pass locally. The original native T02 checkpoint remains failed without an execution receipt; a separate T02 evidence-recovery successor now records the inspected completion without replay, as detailed in the owner feature. The broader module and query/export capabilities remain proposed.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).

## Reference-environment validation (not product ownership)
LAB-01 covers **evidence and backup status**; see [coverage matrix](../../reference-environments/lab-01/experiments/coverage-matrix.csv) and [safety policy](../../reference-environments/lab-01/experiments/safety-and-change-control.md). Testbed specifics must remain outside generic domain implementation.
