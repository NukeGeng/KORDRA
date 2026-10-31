---
id: LAYOUT-SRC-MODULES-MESSAGING
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-31
---

# src/Modules/Messaging

ADR-0020 authorizes the bounded T12.1 MSG kernel. Contracts and Infrastructure now contain the implemented envelope/registry, caller-owned staging, receipt/recovery and offline schema components; acceptance remains incomplete. The default business catalog is empty and no business API/ControlWorker is activated.

`Infrastructure/Diagnostics` supplies explicit closed logging and tracker-export projections. Eight no-DB native/boundary cases verify inline and in-memory queued failure logging, exact two-attempt retry success, message identity where available, tracker export and the observed native failure span. A dedicated future MSG composition must opt in; these classes do not replace global logging, mutate exceptions, sanitize every telemetry extension or make raw tracked sessions safe to export. See the owning feature for revision-bound evidence and remaining database/readiness/full-gate work.

Product ownership: [canonical module specification](../../../docs/features/messaging/module.md).

See [current folder framework](../../../docs/engineering/repository-folder-framework.md) for implemented paths and exceptions. The [source architecture ADR-0009](../../../docs/decisions/0009-source-architecture.md) remains proposed; the bounded kernel adoption does not accept its broader technology, trust boundaries or integrations. Add source only for an approved, mapped feature with applicable tests and review.
