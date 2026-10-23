---
id: LAYOUT-SRC-MODULES-IDENTITYACCESS
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-21
---

# src/Modules/IdentityAccess

This project implements the FEAT-IAM-002 T01 persistence slice accepted under ADR-0012: IAM-owned principal/session tables, an additive EF migration, and explicit runtime-versus-migration database-role validation. Its local verification is recorded in the [feature evidence](../../../docs/features/identity-access/features/local-account-session.md).

T03 adds the accepted local-account authentication service, offline enrollment/recovery/disable, boot-scoped browser sessions, self-service authentication endpoints and transactional AUD integration. The explicit local-pilot HTTPS host composes this service; the default API remains health-only. The API does not start migrations. Authentication grants no tenant/platform authority, and T06 resource authorization remains separate. ADR-0009's broader source-tree proposal remains proposed; this project exists under the separately accepted ADR-0012 scope.

Product ownership: [canonical module specification](../../../docs/features/identity-access/module.md).

See [current folder framework](../../../docs/engineering/repository-folder-framework.md) for implemented paths and exceptions. The [source architecture ADR-0009](../../../docs/decisions/0009-source-architecture.md) remains proposed; this reservation does not accept its technology, trust boundaries or integrations. Add source only for an approved, mapped feature with applicable tests and review.
