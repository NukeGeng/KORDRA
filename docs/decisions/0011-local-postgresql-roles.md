---
id: ADR-0011
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-18
---

# ADR-0011 — Local PostgreSQL databases and least-privilege Kordra roles

**Decision status: Accepted for local Mac development only (2026-10-18).** The maintainer requested creating PostgreSQL databases and the four accounts/privilege groups shown in the supplied image, with a `kordra@` password followed by a random nine-word phrase for Kordra roles and the existing local secret for `postgres`.

## Scope

Provision the already active Homebrew PostgreSQL 15.18 service on `127.0.0.1:5432`, whose listener is restricted to localhost. Create `kordra` and a separate `kordra_test` on that instance. Store credentials in the current macOS login Keychain. This accepts a local development database layout only; it does not select the product's production persistence, PostgreSQL/Marten integration, messaging durability, migration framework or deployment architecture. ADR-0002 and ADR-0009 remain proposed.

## Accepted role and database boundaries

- `postgres`: login superuser, password taken from the prior local `docs/secret/accountpostgre.md` value and moved to Keychain. This manages server-wide roles/databases.
- `kordra_admin`: owner of `kordra` and schema `app`; may create and alter schemas/objects for migrations and grant rights over objects it owns. It has no superuser, `CREATEDB` or `CREATEROLE` attribute.
- `kordra_app`: connects to `kordra`; may use schema `app`, perform `SELECT/INSERT/UPDATE/DELETE`, and use application sequences. It cannot create schema/objects, access `kordra_test`, or administer the server. Default privileges cover future tables/sequences created by `kordra_admin`.
- `kordra_test`: owner/login for `kordra_test`; cannot connect to `kordra`. It has no superuser, `CREATEDB` or `CREATEROLE` attribute.

Each Kordra login has a unique `kordra@` password with nine randomly selected words, stored in Keychain. PostgreSQL uses SCRAM-SHA-256 password verification. The existing host-administration role `dangvyhao` remains unchanged; this setup does not revoke existing local administration.

## Verification and limits

The exact role logins authenticated successfully over loopback against their assigned databases. Catalog checks confirmed database owners, role attributes, DDL/DML grants, future-object default grants, and denial of `kordra_test`/`kordra_app` access to the wrong Kordra database. The maintainer requested manual login details in a local secret document; the credential file is owner-only and excluded through this checkout’s `.git/info/exclude`. It is not tracked or included in receipts. The values are mirrored in macOS Keychain. Detailed connection references and the current matrix are in [local PostgreSQL setup](../operations/local-postgresql-setup.md).

No application source connects to this database; no database package, schema migration or product runtime integration was added. The separate PostgreSQL 18 process was not selected or changed. A local database is not a production backup, authorization or availability design.
