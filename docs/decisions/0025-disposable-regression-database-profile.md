---
id: ADR-0025
status: accepted
owner: NukeGeng
last_reviewed: 2026-11-08
---

# Disposable PostgreSQL regression target — test-only explicit profile

## Verified problem and owner direction

Local `kordra_test.app` is not disposable: the owner answered that it contains data to keep or its ownership is uncertain. Read-only catalog/history inspection found 58 relations, nine known owner migration histories and two pending IAM/PRJ workflow-policy migrations. No application rows were read or changed. The owner selected preparation of a separate disposable database, not migration/reset of existing data or approval of this exact design.

Existing test factories, enlisted transaction adapters, isolated WFL/AUD/MSG guards and test-only protocol assertions pin `127.0.0.1:5432/kordra_test`. Merely changing locator variables cannot safely run the full suite elsewhere. Production runtime/offline targets are separate and must stay fixed.

## Proposed narrow decision

1. A serial regression operation uses one new database `kordra_test_<runId:N>` on the existing local PostgreSQL server, with the existing `kordra_test` role. No new server, container, VM, technology dependency, product schema or production architecture is adopted.
2. An explicit immutable test profile binds schemaVersion 1, runId, exact loopback host/port/database/role, app schema, source fingerprint, operation-manifest hash and finite UTC expiry. It contains no locator/password. The approved operation runner checks source/SQL/config hashes and separate owner operation approval before supplying credentials or creating the database. Profile validity alone confers no operation rights.
3. Default runtime, offline migration and legacy test entry points retain their current behavior. New test/isolated-root overloads and test-composition parameters receive the expected disposable database explicitly. No ambient environment/AppContext flag, arbitrary database name, wildcard/prefix-only grant, connection alias, search_path or GUC may widen runtime authorization. Unprofiled new-database locators deny.
4. Exact expected database is validated together with fixed test role/host/port at every test factory and transaction boundary. When an explicit disposable profile is supplied, neither production nor the old test database is a fallback. The profile may never be read from an HTTP/message body or registered by Product startup.
5. Existing test scenarios continue to exercise real owner stores/adapters/transactions. Test composition threads the profile into the same implementation; it must not replace security/commit paths with fakes merely to avoid target guards. Default guard tests and explicit profile negative tests remain independently executable without credentials.
6. Transport-only restricted-role, fault-injection and native host fixtures require their own exact role/target bindings; this profile never grants role/HBA/process permissions or automatically reruns historical cases. No required tests may be skipped or reclassified to make a full gate pass.
7. Database creation requires a fresh-name preflight and a private creation receipt containing its actual database OID/owner/cluster identity. Cleanup checks that receipt, owned processes/connections and exact target; no FORCE, wildcard DROP, unrelated session termination or existing-database reset. Only the newly created database may be dropped under its operation approval. Preserve original failures and independent cleanup evidence.

## Scope, ownership and implementation order

FEAT-WFL-001 coordinates regression readiness; IAM/TEN/PRJ/INV/SEC/ART/REL/APR/AUD retain their own factories/adapters. Changes to test selections do not change product actor/tenant permissions, delegation, worker SQL authority or accepted T12 runtime architecture.

1. Test-only profile parser and negative tests (prepared; not runtime-integrated).
2. Additive explicit test-target parameters through owner factories/adapters/test composition; enumerate all current literal guards before editing. No runtime/default weakening. Preserve source-to-feature mapping per owner.
3. Offline compatibility/guard tests and frozen database SQL/operation plan. Only after source/SQL/rights review may the exact disposable target be created and migration/fixture tests run.
4. Serial full applicable regression, including required native fixtures under their own allowances. Record configuration failures, skipped/pending cases, source identity, fixture epoch and cleanup separately. Existing kordra_test.app remains untouched.

## Alternatives rejected for this purpose

- Migrating/resetting existing app data despite uncertain ownership.
- Locator substitution that hides the real target from validators.
- A process-wide flag silently enabling new runtime targets.
- Refactoring every test to use model creation instead of migrations and then claiming migration compatibility.
- Adding another PostgreSQL server/VM, stopping services or changing ports without a separate approved need.

## Required decision and limitations

**[NEEDS-DECISION]** This proposal needs exact-content owner acceptance before expanding the existing trusted test/transaction target boundaries. Selecting a separate database authorized preparation only. This ADR is not accepted and does not grant CREATE DATABASE, migration, role/HBA changes, secret access, infrastructure access or merge authority.

The current parser is a test-only selection primitive, not proof of database freshness, data ownership, human approval or a complete regression runner. T12.2 exits/full gates and T12.3/T12.4 remain open. Operational SQL drafts are private and unexecuted; freeze a final operation manifest after integration, not against an incomplete implementation.

## Exact scoped acceptance — 2026-11-08

NukeGeng accepted the exact proposal for explicit test/isolated-transaction target integration. The approved proposal SHA-256 is retained in the private acceptance receipt and snapshot. This supersedes the preceding pending-decision wording only for that scoped test design. It grants no database creation/migration/drop, role/HBA, secret, infrastructure or merge rights; runtime/default targets and existing app data remain unchanged.
