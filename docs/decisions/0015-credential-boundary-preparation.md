---
id: ADR-0015
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-23
---

# ADR-0015 — Local credential boundary and bounded macOS Keychain access

## Context / problem / forces

The owner requested T08 design completion and selected macOS Keychain for the local Mac pilot. ADR-0012 accepted T01–T07 only. SEC is still proposed with no runtime; existing target.read U/D has no credential-binding evidence, and FoundationPermissions deliberately excludes credential.bind. Enabling a secret reader without current SEC evidence or a credential-use commitment would silently broaden authority. A cancelled managed await also does not prove a blocking native Keychain call has stopped.

This complete proposal replaces this ADR's earlier preparation brief; no previous version was accepted. The exact [Credential boundary v1](../contracts/credential-boundary-v1.md) is normative for T08 implementation after human acceptance. Its named bounds/protocols are accepted design decisions, not execution evidence. Native model/effort for this design turn remains unverified; no independent AI reviewer or subagent is invoked or claimed.

## Decision drivers and invariants

SEC ownership; permission plus exact tenant/resource scope; no browser/plaintext/event credential exposure; immutable provider/reference/consumer selection; current rotation/revocation evidence at every use/disclosure boundary; bounded native-call cleanup; explicit audit/unknown recovery; no new service/provider package or automatic live infrastructure access. Preserve accepted authentication/resource-policy contracts, T07 metadata v1 and historical checkpoints.

## Considered alternatives / trade-offs

| Alternative | Assessment |
|---|---|
| In-process native lookup + Task.Run | Small implementation, but cancellation cannot stop the native call or prove quiescence. Rejected for the pilot request path. |
| /usr/bin/security through shell/CLI | Existing binary, but secret output encoding/diagnostics and shared tool identity complicate raw-byte, ACL and bounded-output guarantees. Not selected. |
| Data-protection Keychain/app bundle | Better aligned with Apple's modern app model; command-line signing, entitlements and provisioning add adoption requirements not established for this pilot. Revisit for packaged distribution. |
| File-based Keychain + native short-lived child mode in existing API executable | Selected local-pilot proposal: explicit store, raw-byte IPC, kill/reap boundary and no new service/project/package. Requires careful interop/ACL/platform proof; file-based API longevity is a known trade-off. |
| External vault or secured file | Not selected by the owner for this local pilot. Remote worker custody will need its own design; never silently copy a Mac credential into it. |

## Decision — accepted for the exact T08 scope

1. Adopt Credential boundary v1 only for T08 local metadata/broker/Keychain conformance. T09 real Kubernetes/Helm identity/TLS/bootstrap/resource RBAC/provider packages and target access remain gated. No new credential HTTP endpoint or real network consumer is enabled in T08.
2. Use the existing reserved ConnectionManagement directory for MOD-SEC runtime and BCL Contracts, with owned metadata/history/receipt storage and synthetic tests. Reuse existing pinned EF/Npgsql versions; add only needed projects/lock files, offline migrations and graph checks. Do not create a competing ConnectionSecrets module.
3. Keep binding changes offline under existing API-stopped installation custody. Add SEC metadata+IAM clock-floor+AUD transaction enlistment through owner ports. Runtime SEC access is SELECT-only, with command receipts excluded. No credential.bind permission-catalog expansion or role-name authorization.
4. Bind profile/reference to exact installation/tenant/target/consumer/operation. Use append-only revisions, monotonic current heads, mandatory finite binding expiry (at most 30 days at publication), exact revision comparisons and terminal revoke. New reference for every rotation; old revision or change-back never restores authority.
5. Canonical schemaVersion 1 private manifests and exact receipt/hash/AUD reconciliation settle create/rotate/revoke. Keychain provisioning and database publication are separate operations; no two-phase atomicity or automatic orphan deletion is claimed. Missing physical items stay unavailable.
6. Add owner-neutral IAM dependency evidence supplied by Host from SEC. Preserve existing metadata reads with an explicit no-credential plan. Credential-bound plans fail closed on missing/invalid evidence and compare full dependency identities/revisions/expiry at U, C and D.
7. C is a separate current-authority/credential-consumption commitment between local lookup and consumer admission. Add an optional in-process AuthorityCommitment purpose, defaulting old constructors to ReadAdmission. Record C as a unique existing AUD Use stage with credential-use-<digest> safe reason. Existing AUD v1 stages/schema and metadata HTTP v1 remain unchanged.
8. Use native Security/CoreFoundation APIs in a read-only short-lived child mode of the installed Kordra.Api apphost. Explicit file-based keychain, exact generic item service/account, no prompts/enumeration/default search fallback, no secrets in argv/environment/logs or general serializer. Existing same-UID/OS-admin trust remains; no process sandbox or universal zeroization guarantee.
9. Use bounded private binary pipe IPC, at most 2 children within the 16-invocation cap, 16 KiB maximum secret, 2-second lookup and 500 ms cancellation/kill/reap. These consume the existing 5-second provider/10-second overall budgets; no retry. Only confirmed helper exit permits C. Unresolved child/consumer work closes new admission and is retained for investigation; never kill unrelated processes.
10. At C/D recheck all subject/grant/owner/policy/SEC evidence and earliest expiry. No credential consumer starts if revocation won before C. Already admitted read-only I/O may finish afterward, but changed authority prevents D; already handed-off bytes remain nonretractable. This bounded residual is explicitly part of the proposal.
11. Synthetic consumer and fake providers remain test-only. Real Keychain fixture proof requires explicit operational permission for a new isolated temporary store/items/test executable and exact cleanup; accepting this design alone does not authorize reading existing personal, installation or company credentials. Missing actual platform/ACL proof prevents a full T08 verification claim.
12. Implementation follows the five contract slices and existing deterministic/CI/Codex Auto PR Review boundaries. Human design approval and human merge remain separate. No automatic model dispatch, extra Sol review, replay, provider settings change or external operation follows from this proposal.

## Consequences (pros/cons, risks)

This closes the main authority/cancellation gaps without a second credential service, online admin permission or provider dependency. Process startup and native interop add latency and testing work. File-based Keychain APIs/ACLs differ from the modern data-protection model; stable installed apphost provisioning remains an operator requirement. Raw secrets briefly exist in process/OS memory; trusted consumer discipline is required. Revoking a Kordra reference does not revoke the external token. No production/remote-worker readiness is claimed.

## Migration/rollout/rollback plan

After acceptance: SEC owner metadata/tests → offline transaction/recovery → IAM U/C/D dependency adapters → bounded child/broker → synthetic HTTPS and explicitly authorized isolated-Keychain conformance. Integrate the separately implemented API folder grouping before adding Host files; preserve T07 source/evidence and historical failed checkpoints. Add no guessed source mappings before files exist. Apply migrations only to the authorized isolated test database first; real installation changes require separate offline approval. Default metadata API stays Keychain-independent and no real network plan is registered. Rollback disables credential-consuming registration, retaining monotonic SEC/AUD history; never rewind heads or restore revoked authority.

## How to verify / revisit triggers

Contract SEC08-01 through SEC08-12 are the acceptance matrix: exact negative scopes, expiry/rotation/ABA, U/C/D audit faults and actual HTTPS withholding, commit ACK loss, bounded child protocol/reap, cleanup/redaction, app SQL rights/owner graph, metadata compatibility and isolated native Keychain ACL/lock/raw-byte proof. Execute applicable deterministic gates at implementation/PR boundaries; design/doc validation alone is not passing runtime evidence. Revisit for online administration, new consumer/actions, macOS API/signing changes, persistent/background/remote services, network provider reads, OS trust-boundary changes or production readiness.

## Related modules, contracts, supersedes

FEAT-SEC-001 with IAM-001/002, INV-001, PRJ-001 and AUD-001 consumers. Related: ADR-0012, ADR-0014, authority/admission v1, Resource Policy v1 and the T08 handoff. Supersedes no accepted ADR; supplements those contracts only after its own explicit acceptance. macOS Keychain provider preference is already decided and must not be asked again. This document and Credential boundary v1 were accepted for the exact T08 scope on 2026-10-23; implementation and operational execution evidence remain separate.

## Primary platform references

[Apple TN3137](https://developer.apple.com/documentation/technotes/tn3137-on-mac-keychains) and [SecItemCopyMatching](https://developer.apple.com/documentation/security/secitemcopymatching(_:_:)) were consulted for implementation differences, query scope and blocking behavior. Numeric limits, fencing, process isolation and authorization here are Kordra design choices requiring implementation validation.


## Human decision record — 2026-10-23

NukeGeng approved “Duyệt đúng phạm vi thiết kế T08” and separately authorized “Cho phép fixture Keychain tổng hợp cách ly”. The design approval binds pre-acceptance ADR SHA-256 8c2606bcab03328180046d3c60f92e7864b9a09e4fd3f38df35742e151c3fc0b and Credential boundary v1 SHA-256 250251a09262b5795ecb38c68f30559252fc2342d03265cb7923417aec3eb71d. The exact original snapshots, four-document proposal manifest and approval receipt are retained privately under .amr-local/approvals/adr-0015-t08/. Status annotations do not rewrite those originals.

Fixture permission covers one new UUID-named temporary isolated Keychain under .amr-local/t08/fixtures, new synthetic items and the designated test executable, with create/read/delete limited to those owned resources and exact cleanup. Record the actual store/item/executable identities before executing. Existing/personal/installation/company credentials and every real cluster/network provider remain excluded. No fixture, migration, provider read, runtime implementation, native-model verification, PR or merge is claimed by this acceptance record.
