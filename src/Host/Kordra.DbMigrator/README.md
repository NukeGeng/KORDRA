---
id: LAYOUT-SRC-HOST-KORDRA-DBMIGRATOR
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-25
---

# src/Host/Kordra.DbMigrator

This separate executable applies only owner-scoped migrations for FEAT-IAM-002 T01 and FEAT-AUD-001 T02. Run `identity-access` with the private `KORDRA_IDENTITYACCESS_MIGRATION_CONNECTION_STRING`, or `audit-compliance` with `KORDRA_AUDITCOMPLIANCE_MIGRATION_CONNECTION_STRING`. Each command rejects targets other than loopback PostgreSQL database `kordra` with role `kordra_admin`. The API never runs migrations on startup. Failure output does not include connection details.

The migrator is not an account-enrollment or runtime-authentication tool. IAM and AUD retain ownership of their separate schemas, histories and commands; the AUD command has not been run against `kordra`. See [FEAT-IAM-002](../../../docs/features/identity-access/features/local-account-session.md) and [FEAT-AUD-001](../../../docs/features/audit-compliance/features/immutable-audit.md) for their mappings and local validation evidence.

The [source architecture ADR-0009](../../../docs/decisions/0009-source-architecture.md) remains proposed; this narrow host is present only for the accepted ADR-0012 T01 scope and does not adopt the broader proposed tree or integrations.

## ART T10 owner command

Accepted ADR-0018 adds artifact-management using private KORDRA_ARTIFACT_MIGRATION_CONNECTION_STRING under the executable's existing stopped-installation OS custody. Exact target remains 127.0.0.1:5432/kordra as kordra_admin. ART retains its own app.art_* tables/history; no API startup migration or company/provider access. Local migration/SQL tests use kordra_test; no real ART installation migration has been performed. See the ART feature and local PostgreSQL runbook for current evidence and separate activation approval.
