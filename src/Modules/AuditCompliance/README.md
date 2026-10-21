---
id: LAYOUT-SRC-MODULES-AUDITCOMPLIANCE
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-21
---

# src/Modules/AuditCompliance

This project contains the FEAT-AUD-001 T02 append and exact-lookup foundation accepted under ADR-0012. AUD owns its `app.aud_*` tables and exposes a BCL-only Contracts assembly. The runtime role can read and insert audit rows; the offline migration explicitly removes its mutation and purge privileges.

The persisted row contains canonical nonsecret intent metadata, its SHA-256 hash, a distinct stage outcome, and the D-stage result digest/size. It never contains result payloads. A durable receipt follows PostgreSQL commit confirmation; exact lookup can resolve a lost acknowledgement but cannot establish protected-work completion. Denial appends are bounded to 100 ms and use no retry queue. Retention is at least 90 days with no automatic purge. No history route, IAM callback, tenant/project/target owner, authorization admission or provider integration is registered here.

The task's local implementation and isolated PostgreSQL regression evidence are recorded in [FEAT-AUD-001](../../../docs/features/audit-compliance/features/immutable-audit.md). The broader module remains proposed.
