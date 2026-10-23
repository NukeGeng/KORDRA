---
id: OPS-RESOURCE-POLICY-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-23
---

# Local Resource Policy v1 — provisioning and recovery

Owners: [IAM authorization](../features/identity-access/features/authorize-resource.md), [PRJ binding](../features/project-environment/features/environment-catalog.md), [ADR-0014](../decisions/0014-resource-policy-v1.md) and [resource-policy contract](../contracts/resource-policy-v1.md). This runbook describes implemented local command paths and planned operator actions; it is not permission to migrate/provision real `kordra`, access company targets or deploy. Isolated synthetic test evidence is separate from real installation readiness.

## Trigger, scope and preflight

Use when the maintainer needs to create/publish a fixed `target.read` policy, explicitly bind selected scopes, inspect shared-policy impact or reconcile an unknown offline command. This can deny access to unlisted stale bindings. It cannot grant IAM permission, execute a deployment, fetch a credential or access Kubernetes/Helm.

Before any real operation obtain explicit approval for that installation/database and exact manifest hash. Stop the API. Use the existing canonical private installation identity/data directory; no alternate root or lock path is supported. The API, offline identity/policy commands and migration executable share the same `process.lock`; held runtime custody cannot be repurposed as offline custody. Migrations require private installation identity/directories to exist before execution, but do not require the API to run.

Confirm already accepted local roles from [PostgreSQL setup](local-postgresql-setup.md): runtime `kordra_app`, offline migration/provisioning `kordra_admin`, synthetic tests `kordra_test`. Keep connection locators, manifest/approval evidence, TLS private key and password denylist private. Do not place passwords or connection strings in arguments, tracked docs or logs. The policy command reads its admin locator from a regular private file, validates exact loopback/database/role and reads the existing runtime locator from installed configuration. Normal migration and runtime role guards remain distinct.

Apply the IAM/PRJ owner migrations through the existing migrator only under separately authorized offline change control, with backup/rollback evidence. AUD must already be available. T06 adds the IAM clock floor/policy tables, PRJ binding/history tables, legacy environment-column nullability and the exact history-scope constraint. The latter derives `scope_id = COALESCE(environment_id, project_id)` solely for the PRJ-local FK; it is not a new permission or policy authority. Preserve all legacy policyRevision values and AUD history; no Allow backfill is performed.

## Inspect and approve

Create a schemaVersion 1 private typed manifest exactly as contract section 6. It specifies one policy create/publish/bind, expected revisions and an explicit bounded binding list. Empty bindings are legal for create/publish and can intentionally leave all older shared bindings denied. A bind-only operation requires at least one explicit selection.

The following are command shapes with operator-supplied private paths, not commands executed by this documentation task:

```sh
dotnet run --project src/Host/Kordra.Api -- policy inspect PRIVATE_MANIFEST_FILE PRIVATE_ADMIN_LOCATOR_FILE
dotnet run --project src/Host/Kordra.Api -- policy apply PRIVATE_MANIFEST_FILE PRIVATE_ADMIN_LOCATOR_FILE APPROVED_SHA256
```

Inspect is read-only: it returns canonical manifest SHA-256, existing binding count and at most 50 binding references with a truncation indicator. It creates no approval or receipt. Review all affected scopes privately and approve that exact hash before apply. A hash argument is a precondition, not authentication of who approved it. Installed custody and human authorization are still required. No automatic publication, wildcard binding upgrade or policy editor is provided.

## Apply, verify and stop conditions

Apply enlists IAM policy/head/receipt/clock floor, selected PRJ bindings/history and AUD in one transaction. Owner handlers verify tenant/project/environment facts, current heads, explicit scope and expected revisions. Confirm a `success` result and exact policy/binding references. A policy allow still requires an independent exact IAM/TEN grant before reads can succeed. For new online environment creation, the v2 endpoint inserts an explicit initial binding under `project.manage`; it does not publish policies or replace existing bindings.

The installed local-pilot profile exposes `GET /api/v1/targets/{target}` for bounded metadata and `POST /api/v2/projects/{project}/environments` for the approved versioned creation shape. The latter requires the current session, same-origin/Host/antiforgery controls and Idempotency-Key. Metadata uses current session/grant/ownership/policy at U and D with distinct durable audit intents and caller-bound output. Legacy integer-only create is unregistered, legacy inventory paths remain denied, and no provider read is enabled. List/audit-history disclosure, arbitrary catalog APIs and later T07–T09 provider/credential work are not implied by these two routes.

Stop on wrong identity/role, missing current facts, conflict, expiry, unavailable AUD/storage, invalid clock or unknown commit. Do not repair with live SQL, reset a revision, pick another policy implicitly or use a test allowlist. Missing runtime migrations/bindings deny or return unavailable; API startup never runs migrations. Real configuration/CLI application and native PR CI/review must have their own evidence before operational readiness is claimed.

## Unknown outcome and rollback

Keep the exact original manifest and hash. An unknown commit fences admissions for that process. With API stopped and renewed canonical offline custody, rerun the **same** command/hash to reconcile the IAM receipt against its durable AUD intent. Exact historical result replay precedes mutable current-head/expiry validation and does not republish policy, repeat effects or authorize a read. A changed hash conflicts. Missing/contradictory evidence remains unknown; involve NukeGeng, retain private command/incident evidence and investigate before further mutations.

Known pre-commit setup/save failures roll back without closing unrelated admission. Do not turn those failures into an unknown commit solely because setup was slow. Rollback of a bad committed policy is a newly approved monotonic revision/binding change or disabling positive ingress. Down migrations deliberately reject dropping authority/clock history; preserve the database, prior receipts and completed AMR checkpoints.

## Evidence and escalation

Record installation/target identity, scope, exact manifest/hash, privately approved decision, command result, owner/audit references, checks and any incident. Never include payloads, passwords or private infrastructure details in public evidence. [T06 traceability](../engineering/traceability.md) separates local deterministic proof from hosted CI/Codex review, manual configuration and human merge. NukeGeng owns unresolved commit/configuration issues; no automatic Sol review or extra review round is triggered by this runbook.
