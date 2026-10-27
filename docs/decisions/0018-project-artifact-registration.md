---
id: ADR-0018
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-25
---

# ADR-0018 — Project-scoped immutable artifact registration, T10

## Status, scope and baseline

Accepted bounded adoption with [Artifact registration v1](../contracts/artifact-registration-v1.md), owned by FEAT-ART-001 with IAM/TEN/PRJ/SEC/AUD composition. NukeGeng selected a private OCI/Docker Distribution registry and explicitly accepted this exact design for T10.1a–f on 2026-10-25. Provider preference alone did not supply adoption. Design acceptance supplies no real registry, existing credential or migration authority.

Baseline: main b70def19f69d460d4d9dcac099fa46a56978718d, integrating Nomad #16 and Kubernetes #17 through #18, including the workload-name correction. ART currently contains only the isolated OCIImageDigest Domain library/48 tests. FoundationPermissions does not accept artifact actions; Resource Policy v1 supports target.read only; SEC v1 and its database constraints accept only inventory.readonly.v1/target.read with TargetId. None is a ready-made artifact admission/registry credential implementation.

## Problem and decision drivers

T11 needs stable project-owned artifact references, rather than a mutable image tag or an unverified digest string. T10 must register exact image identity, inspect bounded registry evidence, survive duplicate/uncertain database outcomes and disclose only under current project authority. It must preserve the single-process Foundation and existing inventory/credential contracts.

## Alternatives and tradeoffs

| Alternative | Assessment |
|---|---|
| Metadata-only digest entry marked verified | Rejected: syntax alone proves no registry content/platform or provenance. |
| Provider SDK, Docker daemon/CLI or arbitrary registry challenge/redirect handling | Deferred: introduces dependencies, ambient credential/process behavior and additional trust destinations. |
| Generalize target.read/TargetId into all operations | Rejected: artifact authority is project-scoped; an inventory permit must not become artifact authority. |
| New credential service or background workflow engine | Rejected for T10: reuse SEC/native helper, common admission and existing AUD transactions; T12 owns later durable execution. |
| Digest-first registration through bounded BCL HTTP and explicit project/registry contracts | Selected proposal: useful registry-backed catalog with narrowly stated verification and no runtime deployment claim. |
| Adopt Marten for ART immediately | Deferred: that architecture remains proposed; reuse current EF/Npgsql versions for this bounded owner store without accepting messaging/worker choices. |

## Accepted decision

1. Adopt the contract’s digest-first POST, registration-status GET, artifact GET/list and configured registry-selector query. No tag resolution, webhook, push/delete, layer download, deployment, release approval or provider administration. OCI/Docker schema-2 image manifests and one index/list level are supported only within the explicit bounded profile.
2. ART owns immutable artifact identity, verification facts, manual provenance declarations and registration/idempotency receipts. Reuse existing SHA-256/SHA-512 Domain semantics. Registration proves the declared manifest/config verification level, not signed provenance, layer availability, successful pull, hardware compatibility or deployment approval.
3. Adopt artifact.register and artifact.read only at exact project scope in the existing IAM/TEN machinery; no default/backfilled grants or role-name checks. PRJ supplies current project→tenant identity through its owner port. Adopt the fixed artifact-registration-v1 eligibility predicate, not a fabricated target or a permissive reuse of target-read Resource Policy v1. Existing target policies/digests remain unchanged.
4. Add a typed SEC registry selection/binding for exact installation/tenant/project/registry/repository and consumer artifact.registry.read.v1, operation artifact.register. Inventory v1 contracts, rows and digest bytes stay compatible. Registry binding/profile/revision/head data stays SEC-owned; do not put plaintext in ART or browser contracts.
5. Reuse CredentialBroker’s existing helper/secret lookup and two slots with an additive typed registry overload; no second helper protocol/service or private slot pool. Add a neutral SEC reference-allocation table with global installation/reference uniqueness and typed owner/profile binding, backfill existing inventory references without granting authority, and require both registry and legacy reference writers to allocate transactionally. This prevents two subjects selecting the same native Keychain service/account identity. Retain allocations after revoke; old snapshots/digest serialization do not change.
6. Registry configuration is private, immutable per boot and installation/project/repository bound. Select anonymous, direct Basic or pre-provisioned static Bearer explicitly. Allow only the narrow post-C per-request managed Authorization representations described in the contract; no automatic Basic/Bearer fallback, token exchange, challenge realm, ambient Docker credential or redirect. Challenge-only deployments such as some Harbor/GHCR/Docker Hub configurations are not claimed supported by this first profile.
7. Reuse private-PKI/SAN/SPKI/offline chain and explicit destination custody, with no production loopback bypass. Own bounds are a one-second deadline per request, at most three logical GETs, 1 MiB/document and 3 MiB/attempt, inside existing five-second-from-U/ten-second invocation budgets. T09 transport behavior is unchanged.
8. Introduce durable registration receipts before provider use: Pending plus U in one transaction, then C, bounded external reads, current M and artifact/result plus audit atomically, then current D. Registered/Failed/Abandoned are terminal; uncertainty is an outcome requiring existing fencing/reconciliation, not an invented confirmed database state. No automatic replay or background-job promise. Explicit stopped-API recovery is metadata-only, never another registry use or artifact creation.
9. Add only ART Contracts/runtime/test projects when implementation reaches them; retain the existing Domain/tests. ART runtime reuses EF Core/Npgsql at currently locked versions for its own app.art_* tables. Contracts stay BCL-only, Domain stays isolated; no cross-owner table reads or runtime-module reference cycles. Host composes transactions and authority. Existing SEC receives additive migrations. No startup DDL or real-installation migration authorization.
10. Use the existing fair gate, trusted clock/floor and AUD schema/stages. Share the current sixteen-invocation capacity with protected inventory reads rather than silently doubling it. Unknown commits/helper quiescence retain fencing; pre-commit setup failure must not poison the shared gate. No second authorization framework or distributed guarantee.
11. Accepted accountable custodian for this exact ART/registry slice is NukeGeng, with existing owner responsibilities retained. The exact human acceptance below makes this narrow assignment effective. No automatic adoption of the wider module, ADR-0009, release/execution v1, worker or company operations.

## Delivery, migration and rollback

Implement the contract’s T10.1a–f slices in order after exact acceptance: identity/store → current project authority → SEC registry seam/reference allocation → bounded registry verifier → HTTP/catalog/disclosure/recovery → conformance/docs. Planned source paths are not source mappings until created. Start with synthetic owner data and loopback TLS fixture; isolated native Keychain use needs separately confirmed fixture scope. Existing registry/Keychain/company operations and real migrations need separate authorization.

Offline migration backfills only SEC allocation ownership from existing metadata; no role, policy or credential is invented. Global reference FK/uniqueness, app/test SQL privileges, partial-migration failure and old inventory behavior are explicit migration acceptance. Keep allocations and audit on rollback. Disable ART registration/configuration; do not run destructive Down or restore revoked heads. Rolling back to an old SEC writer that cannot maintain the allocation invariant is unsupported for writes; keep the compatible writer or disable offline credential mutations. Existing T09 reads remain independently testable.

## Consequences, limits and acceptance

The useful outcome is a current-authorized immutable project artifact catalog suitable as a later release input. Work adds real ART persistence, a narrow IAM/SEC extension, and explicit interrupted-registration recovery. Fixed direct-auth/private-PKI support limits initial provider compatibility; declared provenance and unverified layers are intentionally visible limitations. No broad Verified/deployable flag is introduced.

The contract’s ART10-01–16 matrix is required at implementation boundaries; design doc checks do not satisfy it. Preserve original 48 Domain tests, all T07–T09 regressions and checkpoint bytes. Codex Auto PR Review remains the implementation reviewer; no extra Sol review, PR trigger, subagent or routine Product-suite run belongs to this bounded design. Return the exact proposal for human decision before code.

## Related decisions and evidence

Supplements scoped ADR-0012/0015/0016/0017 only after acceptance; supersedes none. Resource Policy v1 is not widened. [OCI Distribution 1.1.1](https://github.com/opencontainers/distribution-spec/blob/v1.1.1/spec.md), [OCI image manifest](https://github.com/opencontainers/image-spec/blob/v1.1.1/manifest.md), [image index](https://github.com/opencontainers/image-spec/blob/v1.1.1/image-index.md) and [Distribution token authentication](https://distribution.github.io/distribution/spec/auth/token/) inform protocol semantics. Numeric bounds, direct-auth support and owner/state/approval rules are Kordra design choices. No live provider conformance or full OCI-client conformance is claimed.

Resolved on 2026-10-25: NukeGeng accepted this exact ADR and Artifact registration v1 for T10.1a–f, including new project permissions/eligibility, ART EF persistence, SEC typed binding/global reference allocation, shared admission/recovery, direct-auth managed-header/offline TLS residuals and slice custody. T10.1g, existing secrets/registries, real migrations and later release/worker decisions remain outside scope.

## Exact-content acceptance evidence — 2026-10-25

The maintainer answered the exact T10.1a–f approval question: “về phần này tôi duyệt”. Pre-annotation approved SHA-256: ADR 1dfb427171bd4e3381c296e50566b8311ec3f087c51c4ebe90dfa7f39da1a639; contract 2bbe896b15633fbe804706b2a69e558f291313d61bdd212b8ff0906a2f05ca6c. Original bytes remain in .amr-local/t10-design/proposal; decision/scope is private .amr-local/approvals/adr-0018-t10/acceptance.json. Status/acceptance annotations change no substantive approved design. Luna may implement the covered slices without repeat design approval or an extra Sol review. This acceptance is not implementation, successful Product validation or native GOAL completion.
