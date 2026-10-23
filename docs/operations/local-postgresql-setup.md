---
id: OPS-LOCAL-POSTGRESQL
status: verified
owner: engineering-maintainers
last_reviewed: 2026-10-19
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

For manual local logins, read the ignored, owner-only `docs/secret/accountpostgre.md`. The same values are mirrored in macOS Keychain for a future approved integration. After rotating, update the PostgreSQL role, the local file and Keychain together. Never copy passwords into tracked documentation, a connection URI, shell history, logs or a shared `.env` file. Product Keychain access has not been implemented.

## Connection references

Use host `127.0.0.1`, port `5432`, database `kordra`, schema `app`, and role `kordra_app` for application access. Use `kordra_admin` only for schema migration and object grants. Integration tests use `kordra_test` with `kordra_test`; do not point them at the main database. Enter a retrieved password only at a protected interactive prompt. Do not embed it in a command-line argument or commit a passfile.

## Verification record

Provisioning verified on the target instance: all four credentials authenticated over loopback; role flags and database owners matched this matrix; `kordra_admin` has database/schema creation rights; `kordra_app` has schema usage but not schema creation; future app tables receive only DML; future app sequences receive usage/select/update; and `kordra_test` and `kordra_app` lack access to the other Kordra database. A short-lived mode-0600 libpq passfile was removed after these checks. Credential values were not printed or retained in the receipt.

This is a local database setup check, not a code integration test, schema migration, backup/restore rehearsal or production readiness check. To stop using it, first confirm that no needed local data or unrelated object ownership depends on these resources. Remove only databases `kordra` and `kordra_test`, then the three dedicated roles `kordra_admin`, `kordra_app` and `kordra_test`. Retain both pre-existing superusers, `postgres` and `dangvyhao`, and the PostgreSQL service. The four credential entries above include `postgres`; that credential count is not a teardown role list. This runbook correction performs no database or role deletion.

## T04 TEN migration and recovery boundary — 2026-10-22

The offline DbMigrator accepts the typed tenant-management operation with the private KORDRA_TENANTMANAGEMENT_MIGRATION_CONNECTION_STRING locator. It rejects every target except 127.0.0.1:5432/kordra as kordra_admin before connecting; migration is never called at API startup. This documents an available entry point, not an executed runtime migration or permission to run it. Stop the API, retain the installation owner lock, capture backup/expected migration plan and obtain the existing operational approval before an actual runtime change. Apply owner migrations in dependency order; additive IAM/TEN Down operations intentionally reject automatic rollback, so use a reviewed forward fix or separately approved restore.

T04 tests applied IAM/TEN additive migrations only to synthetic kordra_test. Real runtime schema/grants/provisioning are not prepared by these tests. TEN migration history denies PUBLIC/kordra_app access. If an owner commit is unknown, current admission remains closed; offline reconciliation must inspect the exact command receipt/current owner state and audit evidence under fresh authority before normal boot/session invalidation. Restart alone does not establish whether a prior operation succeeded. Do not drop databases, clear audit rows or retry under a new command key to hide an unknown outcome.
