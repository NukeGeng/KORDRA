---
id: LAYOUT-SRC-BUILDINGBLOCKS-SECURITY
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-21
---

# src/BuildingBlocks/Security

T03 implements the fair writer-preferring technical admission gate accepted by ADR-0012 and mapped to [FEAT-IAM-002](../../../docs/features/identity-access/features/local-account-session.md). The BCL-only project coordinates authentication readers and invalidating writers; it contains no tenant policy, grants, persistence or provider integration.

Gate behavior and cancellation/fairness regressions are covered by `tests/IdentityAccess.Tests/FairAsyncReaderWriterAdmissionGateTests.cs`. Its existence does not complete the later T06 authorization/admission integration.

See [current folder framework](../../../docs/engineering/repository-folder-framework.md) for implemented paths and exceptions. The [source architecture ADR-0009](../../../docs/decisions/0009-source-architecture.md) remains proposed; this reservation does not accept its technology, trust boundaries or integrations. Add source only for an approved, mapped feature with applicable tests and review.
