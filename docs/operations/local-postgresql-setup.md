---
id: OPS-LOCAL-POSTGRESQL
status: verified
owner: engineering-maintainers
last_reviewed: 2026-10-25
---

# Kordra local PostgreSQL setup

This runbook records the local database instance created on 2026-10-18 under [ADR-0011](../decisions/0011-local-postgresql-roles.md). It is for this Mac's development environment; it does not configure product source or a remote/production server.

## Instance

| Setting | Value |
|---|---|
| Engine | Homebrew PostgreSQL 15.18 |
| Address | `127.0.0.1:5432` (`listen_addresses=localhost`) |
| Password verification | SCRAM-SHA-256 |
| Main database | `kordra`, owner `kordra_admin`, application schema `app` |
| Integration-test database | `kordra_test`, owner `kordra_test` |
| Separate PostgreSQL 18 process | Not selected or modified |

The database contains no Kordra application tables yet. This repository's API does not connect to it.

## Accounts and privileges

| Login | Database access | Privileges |
|---|---|---|
| `postgres` | Server-wide superuser | Full PostgreSQL administration. Its password was imported from the prior local secret file. |
| `kordra_admin` | `kordra` | Owns the database and `app` schema; may perform migrations and grant privileges on objects it owns. Cannot create server roles/databases and is not a superuser. |
| `kordra_app` | `kordra` | `USAGE` on `app`; `SELECT`, `INSERT`, `UPDATE`, `DELETE` on tables; `USAGE`, `SELECT`, `UPDATE` on sequences, including default grants for future objects. No DDL, test-database access or superuser rights. |
| `kordra_test` | `kordra_test` only among the Kordra databases | Owns the isolated test database and can create test schemas/objects there. Cannot connect to `kordra`; no superuser rights. |

The database administrator can grant/revoke privileges for objects it owns. Creation or alteration of server-wide login roles remains a `postgres` superuser task. Existing host account `dangvyhao` was already a PostgreSQL superuser and remains unchanged.

## Credential storage

Each of the three Kordra roles has a separate, randomly generated password in the form `kordra@` followed by nine hyphen-separated random words. The `postgres` password is the value previously supplied in local-only `docs/secret/accountpostgre.md`; that file now contains only this Keychain locator:

| Keychain service | Account |
|---|---|
| `Kordra/PostgreSQL/local/pg15` | `postgres` |
| `Kordra/PostgreSQL/local/pg15` | `kordra_admin` |
| `Kordra/PostgreSQL/local/pg15` | `kordra_app` |
| `Kordra/PostgreSQL/local/pg15` | `kordra_test` |

For manual local logins, read the ignored, owner-only `docs/secret/accountpostgre.md`. The same values are mirrored in macOS Keychain for a future approved integration. After rotating, update the PostgreSQL role, the local file and Keychain together. Never copy passwords into tracked documentation, a connection URI, shell history, logs or a shared `.env` file. T08 now implements a bounded native credential reader, verified only with separately authorized isolated synthetic fixtures; no existing PostgreSQL Keychain entry has been read or adopted by Product.

## Connection references

Use host `127.0.0.1`, port `5432`, database `kordra`, schema `app`, and role `kordra_app` for application access. Use `kordra_admin` only for schema migration and object grants. Integration tests use `kordra_test` with `kordra_test`; do not point them at the main database. Enter a retrieved password only at a protected interactive prompt. Do not embed it in a command-line argument or commit a passfile.

## Verification record

Provisioning verified on the target instance: all four credentials authenticated over loopback; role flags and database owners matched this matrix; `kordra_admin` has database/schema creation rights; `kordra_app` has schema usage but not schema creation; future app tables receive only DML; future app sequences receive usage/select/update; and `kordra_test` and `kordra_app` lack access to the other Kordra database. A short-lived mode-0600 libpq passfile was removed after these checks. Credential values were not printed or retained in the receipt.

This is a local database setup check, not a code integration test, schema migration, backup/restore rehearsal or production readiness check. To stop using it, first confirm that no needed local data or unrelated object ownership depends on these resources. Remove only databases `kordra` and `kordra_test`, then the three dedicated roles `kordra_admin`, `kordra_app` and `kordra_test`. Retain both pre-existing superusers, `postgres` and `dangvyhao`, and the PostgreSQL service. The four credential entries above include `postgres`; that credential count is not a teardown role list. This runbook correction performs no database or role deletion.

## T04 TEN migration and recovery boundary — 2026-10-22

The offline DbMigrator accepts the typed tenant-management operation with the private KORDRA_TENANTMANAGEMENT_MIGRATION_CONNECTION_STRING locator. It rejects every target except 127.0.0.1:5432/kordra as kordra_admin before connecting; migration is never called at API startup. This documents an available entry point, not an executed runtime migration or permission to run it. Stop the API, retain the installation owner lock, capture backup/expected migration plan and obtain the existing operational approval before an actual runtime change. Apply owner migrations in dependency order; additive IAM/TEN Down operations intentionally reject automatic rollback, so use a reviewed forward fix or separately approved restore.

T04 tests applied IAM/TEN additive migrations only to synthetic kordra_test. Real runtime schema/grants/provisioning are not prepared by these tests. TEN migration history denies PUBLIC/kordra_app access. If an owner commit is unknown, current admission remains closed; offline reconciliation must inspect the exact command receipt/current owner state and audit evidence under fresh authority before normal boot/session invalidation. Restart alone does not establish whether a prior operation succeeded. Do not drop databases, clear audit rows or retry under a new command key to hide an unknown outcome.

## T05 owner migration entry points — 2026-10-22

DbMigrator accepts project-environment with private KORDRA_PROJECTENVIRONMENT_MIGRATION_CONNECTION_STRING and infrastructure-inventory with private KORDRA_INVENTORY_MIGRATION_CONNECTION_STRING. Both factories require exact 127.0.0.1:5432/kordra as kordra_admin; runtime mutation enlistment instead validates kordra_app or the exact isolated kordra_test fixture. No runtime migration was executed. Existing stopped-API/installation-lock/backup/approval requirements apply before any real owner migration; PRJ precedes INV composition, and AUD/TEN/IAM prerequisites remain mandatory.

Owner histories prj_ef_migrations_history/inv_ef_migrations_history revoke PUBLIC/kordra_app access; Down rejects automatic destructive rollback. T05 mutation tests applied only isolated test migrations and scoped synthetic-row fault-trigger DDL. Whole-workspace offline initialization, real policy adapters, application table grants and production migration/restore rehearsal are not established by local green tests. Reconcile exact receipts/audit/current owner state for unknown outcomes under fresh authority; neither a new key nor restart proves success.

## T08 SEC migration boundary — 2026-10-24

The offline connection-management command uses KORDRA_CREDENTIAL_MIGRATION_CONNECTION_STRING and the same canonical installation custody/API-stopped requirements. It validates exact loopback kordra/kordra_admin before connecting. SEC-specific migrations explicitly remove generic runtime DML on SEC metadata and deny runtime access to command receipts/migration history; runtime gets only the SELECT metadata required by the broker. Existing generic default grants above do not override these narrower owner grants. Local validation migrated only kordra_test and checked actual catalog privileges. No live kordra migration or adoption of the PostgreSQL Keychain entries is authorized by the synthetic T08 fixture. See [credential installation/verification](credential-boundary-v1.md).

## ART T10 offline migration — implemented command, activation separately authorized

Accepted ADR-0018 adds the artifact-management owner migration command. It reads the private KORDRA_ARTIFACT_MIGRATION_CONNECTION_STRING, requires exactly loopback 127.0.0.1:5432 database kordra/user kordra_admin, and runs only inside the existing stopped-API installation custody held by Kordra.DbMigrator. Do not copy a locator/password into Git or logs. After exact operational approval and the existing custody setup, the command is:

```bash
dotnet run --project src/Host/Kordra.DbMigrator -- artifact-management
```

This command has not been run against kordra. Local T10.1a tests migrate only isolated kordra_test; their synthetic row transactions roll back. ART owns app.art_artifacts, app.art_registration_receipts, app.art_provenance_claims and art_ef_migrations_history. Runtime rights are SELECT/INSERT plus only receipt outcome-column UPDATE; identity/claim UPDATE/DELETE/TRUNCATE and receipt deletion are denied. No app DDL permission is added. Disable registration before compatible rollback; do not execute destructive Down, delete immutable history or downgrade a credential writer. KORDRA_ARTIFACT_CONNECTION_STRING is the accepted later runtime locator; API wiring/admission remains T10.1b–e, so setting it alone does not enable registration.

### T10 atomic-commit fixture evidence

T10 query-store tests use uncommitted synthetic rows and rollback. T10 Host transaction/ACK-loss and query U/D durability tests must commit to prove atomic ART/AUD/IAM-floor behavior; they retain bounded, fresh UUID-scoped synthetic rows in kordra_test as append-only evidence. No real user, registry, secret or installation state is copied. Do not disable immutable triggers or delete receipts/artifacts to clean these tests. This fixture behavior does not authorize real kordra migration or production writes; live/provider checks retain their separate operational gates.
