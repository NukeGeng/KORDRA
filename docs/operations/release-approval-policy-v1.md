---
id: OPS-RELEASE-APPROVAL-POLICY-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
---

# Release approval policy v1 — local offline operations

This is the operational guide for [FEAT-APR-002](../features/approval-governance/features/approval-policy.md), [ADR-0005](../decisions/0005-immutable-release.md) and the exact [Release and approval v1 contract](../contracts/release-approval-v1.md). The code and tests are implemented locally and await PR review; a successful design decision or local test is not permission to migrate or provision a real installation. Real installation changes require the maintainer's explicit approval for that installation, database and exact manifest hash. This guide does not enable a deployment, read credentials or contact a provider.

## Scope and preparation

The approved installation must be stopped and protected by its existing private installation identity/process lock. Ensure the API and any other installation writer are stopped. The offline command uses that lock and the private admin locator; it does not load `local-pilot.json`, the TLS private key, registry configuration or Keychain. Keep locator contents and manifests private; do not put passwords or connection strings in command arguments, tracked files or logs.

The separate DbMigrator accepts `approval-governance` with `KORDRA_APPROVAL_MIGRATION_CONNECTION_STRING`, guarded to loopback database `kordra` and role `kordra_admin`; the process first acquires installation custody. Migration only creates additive APR-owned structures, not a permitting policy, role grant, endpoint or deployment. There is no startup DDL. A real migration is an operation requiring separately approved installation scope; do not execute it merely because this document exists.

Prepare a closed manifest exactly as contract section 3: installation/tenant/project/policy IDs, expected database and role, expected policy revision, complete policy payload and 0–32 sorted explicit environment changes. The manifest must name actual current active owner facts. Initial policy revision expects zero; rebinding names the exact binding ID and current revision. A missing policy never defaults to allow; a changed policy head invalidates bindings left on the old revision.

## Inspect and apply

These are implemented command shapes, not commands executed by this guide. Substitute private file paths and use the installed Kordra.Api executable under the approved installation:

```text
Kordra.Api approval-policy inspect PRIVATE_MANIFEST_FILE PRIVATE_ADMIN_LOCATOR_FILE
Kordra.Api approval-policy apply PRIVATE_MANIFEST_FILE PRIVATE_ADMIN_LOCATOR_FILE APPROVED_SHA256
```

Inspect is read-only and returns the canonical `manifestHash`, current policy revision and selected binding IDs/revisions. Review the exact payload, target installation and impact before an authorized maintainer approves the hash. Apply requires the same exact hash. Its success result contains only schemaVersion, manifestId, policyId, policyRevision, payloadHash, commandId and replayed; it contains no secret or policy body. A hash binds content; it does not prove who approved it.

Policy revision, selected binding revisions, command receipt, audit mutation and trusted time floor commit together. The approved current Development/Test qualification and policy restrict later release use; the command itself grants no user permission. Staging/Production release qualification and deployment authority are separate future decisions.

## Retry, unknown outcome and rollback

Retain the exact original private manifest/hash and safe command/audit references. If a command reports a confirmed success, do not create another manifest merely to repeat it. The same manifest and hash can reconcile an already committed receipt; the operation checks the persisted policy bytes and matching audit evidence first and does not publish again. Changed input under the same manifest ID conflicts.

If the commit was attempted but outcome is unknown, stop the API/command, keep the installation lock discipline and reconcile using the exact manifest/hash under stopped custody. Missing or inconsistent owner/audit evidence remains unknown and requires investigation; never infer success, replay a new grant, reset the clock floor or restore an older policy head. Failure before a commit does not by itself require fencing unrelated authentication. Do not delete immutable policy, binding history or receipts to clear a failed command. The APR migration Down deliberately refuses to erase history; rollback disables new features and retains compatible readers/history.

## Validation and evidence

APR Domain checks canonical policy/manifest bytes and closed schema; isolated PostgreSQL tests check scoped revisions, foreign keys, immutability and runtime role ACLs. Coordinator tests exercise current owner facts, audit/floor enlistment, stale/replayed/concurrent requests, lost acknowledgement, and the case where audit appears durable but the APR owner transaction rolled back. A real installation operation, native PR review and human merge remain separate evidence. Do not publish private paths, locator values or infrastructure identifiers.
