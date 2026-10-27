---
id: OPS-CREDENTIAL-BOUNDARY-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-26
---

# Local credential boundary — installation and verification

Scope: accepted ADR-0015/Credential boundary v1 and FEAT-SEC-001 only. Current metadata-only API remains Keychain-independent. T08 registers no credential HTTP management endpoint or network consumer; constructing a broker/reference is not authority. Do not connect Kubernetes/Helm or read existing items using synthetic-fixture permission.

## Storage and offline administration

Use the existing guarded DbMigrator command connection-management with private KORDRA_CREDENTIAL_MIGRATION_CONNECTION_STRING, exact loopback kordra/kordra_admin identity, canonical installation custody and API stopped. This documentation does not execute it or authorize real installation DDL. SEC migrations add app.sec_* metadata/history/receipt tables; runtime gets SELECT on required metadata and no DML or command-receipt access. Runtime startup never migrates. Apply additive migrations to isolated kordra_test first, preserve backups and never drop monotonic authority history for rollback.

The API executable supplies these offline command shapes, with operator-owned private files:

```sh
dotnet run --project src/Host/Kordra.Api -- credential inspect PRIVATE_MANIFEST PRIVATE_ADMIN_LOCATOR
dotnet run --project src/Host/Kordra.Api -- credential apply PRIVATE_MANIFEST PRIVATE_ADMIN_LOCATOR APPROVED_SHA256
```

Manifest schema/canonicalization and exact expiry/revision rules are in the accepted contract. No secret, arbitrary provider path or URL belongs in a manifest. Inspect reads metadata only; apply requires exact approved hash and installation lease. Keychain provisioning is separate: use a new reference/item before publishing rotation. Missing items stay unavailable. Kordra revoke does not invalidate an external token or delete its physical item. No automatic import or orphan cleanup is provided.

## Host configuration and safe activation

KeychainAccessConfiguration is an immutable installation object: installation ID, absolute installed Kordra.Api apphost and approved SHA-256, at most 32 locator-ID/exact-keychain-path mappings. It contains no secret. Provision the intended executable's Keychain ACL explicitly; generic dotnet-wide/Allow all applications access is unsupported. The binary must remain at its pinned identity; changing executable or mapping requires API-stopped maintenance/restart and renewed ACL custody. Never auto-unlock a personal store, disable trust or use a search-list/default fallback to repair access.

A future Host-registered consumer must have the exact inventory.readonly.v1/target.read plan and approved T09 provider identity/TLS/RBAC. Product intentionally registers none in T08. Consumer code must use supplied invocation/resource context, stay bounded/nonblocking at admission, return only sanitized PrivateReadBuffer and never retain/copy material to strings/cache/logs/exceptions. No private locator is a permission or a browser API field.

The internal child mode accepts only redirected streams, does not load API/DB configuration and uses one exact Keychain query. Parent caps output, validates nonce/frame and confirmed process exit, then requires a new C commitment before consumer use. A hung/late/unreaped startup or child closes admission and retains owned cleanup state. Stop/investigate the installation; never terminate an unrelated process/securityd or retry with another credential. Native process/OS memory/dumps are not a perfect-zeroization guarantee; do not enable diagnostic secret capture before live use.

## Exact synthetic test permission

The maintainer separately approved a newly created UUID temporary Keychain under .amr-local/t08/fixtures, new synthetic items and the designated test apphost only. Tests record store/item/executable identities and hash before native operations. Creation may add the new store to the OS search list; fixture cleanup removes only its exact owned reference while preserving current other entries, never restores a stale global list or reads their items. Native mutation bindings live in the test assembly, not the runtime.

Set nonsecret KORDRA_CREDENTIAL_KEYCHAIN_FIXTURE_ROOT to the absolute Product-worktree .amr-local/t08/fixtures path when running Mac conformance. Missing/incorrect root on Mac fails instead of reading a substitute store. Each run uses a new store, verifies create/ACL/raw-byte/missing/locked/service-selection behavior and deletes that exact store through Keychain APIs. Evidence remains private; generated passwords/material stay in memory and are cleared. Existing/personal/installation/company items and all cluster/network operations remain excluded.

Native conformance is explicitly unsupported/skipped on non-macOS CI; a separate neutral-platform test checks unsupported behavior. All synthetic owner/IPC/process-lifecycle/HTTPS tests remain runnable without Keychain or Codex credentials. Linux success cannot replace the recorded local Mac proof. Record applicable local tests, exact source digest and residual configuration requirements before claiming completion.

## Recovery and evidence

After unknown database commit, reacquire offline custody and retry only the identical manifest/hash for receipt lookup; compare its exact history/result and AUD ID/hash/time evidence. No replayed receipt grants new credential use. Known pre-commit setup failures roll back without poisoning admission. Contradictory/missing receipts stay unknown for maintainer investigation. Disable credential-consuming registration for rollback while preserving SEC/AUD revisions. Existing metadata reads can remain enabled when configured independently.

Record exact fixture identity, source/gate digest, native result and deletion status privately. T08 completion is local proof, separate from hosted CI/Codex review, merge, real provider configuration and OP-P0 readiness. No automatic Sol review/model dispatch belongs to recovery.


## Known startup failure versus unresolved helper

A known failure to start the configured helper (for example, missing execute permission) reports unavailable and releases its helper slot while unrelated metadata reads/authority operations remain usable. Correct the installation executable permissions/identity under the existing maintenance procedure; never fall back to another executable or credential. A launch still in progress at its deadline or an owned process whose exit cannot be confirmed remains unknown and closes admission as before. Tests inject the known startup failure and verify repeated attempts do not exhaust the two-slot bound or close the shared gate.

## T10 registry metadata administration — local implementation

Accepted ADR-0018 / Artifact registration v1 adds separate stopped-API commands:

```sh
dotnet run --project src/Host/Kordra.Api -- registry-credential inspect PRIVATE_MANIFEST PRIVATE_ADMIN_LOCATOR
dotnet run --project src/Host/Kordra.Api -- registry-credential apply PRIVATE_MANIFEST PRIVATE_ADMIN_LOCATOR APPROVED_SHA256
```

Use schemaVersion 1 RegistryCredentialManifest with installationId/tenantId/projectId/registryId/repository, fixed artifact.registry.read.v1 consumer and artifact.register operation. The explicit canonical manifest includes expected binding/revision, new binding/profile/reference/locator IDs and UTC validity for create/rotate; revoke omits new profile/reference/locator/validity values. Inspect returns a nonsecret hash/current binding/revision; apply requires that exact approved hash and exclusive installation custody. Tenant and PRJ current active identity must agree; caller fields never establish membership. No credential payload is accepted. These commands use existing private locator validation and do not look up/import Keychain items or contact a registry.

Apply the additive SEC allocation migration only after separate real-installation authorization, backup and stopped-writer checks. It backfills inventory metadata, then constrains every native installation/reference ID to one typed tenant/profile. Existing references, including revoked ones, remain allocated. Conflicting/invalid history stops migration; do not guess ownership, delete allocation or use Keychain contents to resolve it. Code rollback must disable incompatible legacy credential writers; Down deliberately refuses to erase allocations/history. Tests cover nonempty migration and transaction rollback only in isolated kordra_test; this is not evidence of real provisioning.
