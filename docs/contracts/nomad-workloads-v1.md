---
id: CTR-NOMAD-WORKLOADS-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-24
---

# Nomad workloads v1 — T09.2

Normative accepted contract with [ADR-0017](../decisions/0017-nomad-workload-observation.md). FEAT-INV-001/MOD-INV owns transport/trust/projection; IAM owns authorization/U/C/D, SEC owns binding/borrowed credentials, AUD owns durable admission intents, Host composes. Actor is an authenticated tenant member with exact target.read scope; platform-only authority is insufficient. No company access, deployment, OBS health or implemented Product provider is asserted.

## Trust and compatibility

Use existing .NET 10 BCL HttpClient/SocketsHttpHandler/System.Text.Json and existing projects. No Nomad SDK or CLI execution in Product. Keep current metadata response/route unchanged. Do not convert this into generic provider infrastructure or add business permissions.

Owner-only regular private configuration (max 64 KiB, max 32 targets), approved raw SHA-256 and stopped-API loading/rotation follow the strict custody, unknown/duplicate/null/symlink rejection and no-hot-reload rules in [Kubernetes workloads v1 sections 2–3](kubernetes-workloads-v1.md#2-immutable-installation-target-trust). Nomad input root keys are exactly schemaVersion (1), installationId, targets. Entry keys are exactly tenantId, targetId, scopeKind, scopeId, apiOrigin, destinationIps, caCertificateDerBase64, serverSpkiSha256, region, namespaceName, namespaceCreateIndex, destinationKind. No Kubernetes UID, arbitrary path, token or browser-supplied plan. Duplicate target keys and the same origin/region/namespace tuple mapped to different targets are rejected.

Canonical compact UTF-8 entry digest key order: schemaVersion, provider (literal nomad-v1), installationId, tenantId, targetId, scopeKind, scopeId, apiOrigin, destinationIps, caCertificateDerSha256, serverSpkiSha256, region, namespaceName, namespaceCreateIndex, destinationKind. Reuse ADR-0016 GUID/hex/CA/IP/origin canonicalization and bounds. Region and namespace are explicit nonempty ASCII [a-zA-Z0-9-], max 63; namespace '*' is forbidden. namespaceCreateIndex is a canonical positive UInt64 decimal string, with no sign/leading zeros; never a floating point number or global cluster GUID.

destinationKind is private-network or personal-loopback. The former retains ADR-0016 production destination restrictions. The latter requires exactly one destination IP, 127.0.0.1, an explicit HTTPS port, approved CA/pin and installation hash. Never silently select the mode from the URL; no other loopback/IPv6/metadata address or insecure bypass. The new VM has no host directory mounts or forwarded host SSH agent. Lab-specific instance names/ports stay in operational configuration, not generic source.

Bind ConsumerConfigurationDigest and exact project/environment ScopeKind/ScopeId in the additive plan/authority digest designed in ADR-0016. Preserve original metadata v1/credential v2 serialization when additions are null. Keep original SEC digest in ReadDependencySnapshot for the broker; do not replace it with the provider digest. Config-qualified internal authority digest v3 must include provider-specific config hash so no Kubernetes/Nomad plan can substitute for another. CurrentCredentialDependencyReader resolves only a server registry entry under the fence, validating installation/tenant/target/scope/digest. Existing synthetic null-digest consumer registration stays test-only.

## Credential and disclosure

Use the existing inventory.readonly.v1 / target.read / opaque-bytes-v1 SEC binding. Token bytes are 1–4096 ASCII bearer-compatible bytes, with no whitespace/control/CRLF/configuration. After C only, the registered Nomad transport may create temporary X-Nomad-Token header strings; never use Authorization concurrently, default headers, request reuse, diagnostic export, exception payloads or public DTOs. Dispose request/response/handler scope; clear owned mutable buffers. All other consumers retain their T08 prohibition unless separately approved. NukeGeng accepted this exact exception under ADR-0017 for T09.2a–d on 2026-10-24.

Sequence remains U → bounded Keychain helper/confirmed exit → current C/durable intent → provider → current D/durable intent → caller-bound output. Provider finishes within the existing 5-second budget measured from U, overall 10 seconds; no extra time for C. Keep helper bounds/cleanup/fencing unchanged. Add optional UTC DisclosureValidUntilUtc to PrivateReadBuffer as in ADR-0016; old constructor/null behavior remains valid. T09.2 sets capture+15 seconds, and D checks the minimum of original/current session/grant/policy/SEC/result deadlines. No body on stale/denied/unknown outcome; no cached permit/result.

## TLS, identity and bounded requests

Use invocation-owned HttpClient/SocketsHttpHandler, HTTP/1.1 exact, TLS 1.2/1.3, zero pooled lifetime and Connection: close per logical request. Disable proxy/redirect/cookies/decompression/ambient credential fallback. Verify destination before connect and exact CA chain, hostname/SAN, serverAuth, certificate validity and server SPKI pin before token transmission. No certificate downloads/online revocation. No application retries, pagination follow-up, blocking query, polling, streaming or cross-region fallback. Framework idempotent GET recovery within the deadline is a residual, not exactly-once delivery.

Exactly four logical GETs, sequentially, with escaped server-selected namespace/region and no user query passthrough:

1. /v1/namespace/{namespace}?region={region}
2. /v1/jobs?namespace={namespace}&region={region}&per_page=25&meta=false
3. /v1/allocations?namespace={namespace}&region={region}&per_page=25&resources=false&task_states=false
4. Repeat the exact Namespace GET.

Require Name and CreateIndex to equal the approved expectations on both identity responses; reject missing/recreated/mismatched identity. Require X-Nomad-KnownLeader true on data responses and valid positive X-Nomad-Index; capture local trusted time after the first identity result. Nomad default reads are not a cross-request atomic snapshot; never describe them as strictly serializable or globally atomic. No stale/index/wait queries. Under this pilot a pinned dedicated server key plus namespace CreateIndex is the installation continuity assumption; a restore/rebuild requires offline re-enrollment, not first-seen trust.

Bound each request to min(1 second, remaining provider budget), response headers to 16 KiB, each body to 256 KiB, total bodies to 1 MiB, JSON depth to 32, and each data list to 25 entries. Final output max 256 KiB/50 records. Reject duplicate JSON properties throughout, invalid/null identity, oversized lists, malformed/trailing JSON, unsupported compression and byte/deadline overflow. Unknown provider fields may be skipped within limits, never copied into output. X-Nomad-NextToken marks that collection partial and is neither followed nor returned; never claim a complete count from a partial page.

Identity, TLS, cancellation, parse/size/deadline failures invalidate the whole invocation. A bounded 403/404/429/5xx for one data collection produces unavailable for that set; require at least one observed set plus successful final identity. Other non-200 statuses fail the whole result. An empty successful list is observed/complete; unavailable is not empty inventory. An unknown leader invalidates that collection. Never report provider body/error text or retarget/retry.

## Public projection

Opt-in GET /api/v1/targets/{targetId}/nomad-workloads uses the existing guarded local session GET/caller-bound response pattern, no query/body/Transfer-Encoding, no-store and existing safe preheader failure behavior. No new credential/target management API. Top-level exact schemaVersion 1 fields: schemaVersion, invocationId, targetId, provider (nomad), namespace, region, capturedAtUtc, completedAtUtc, validUntilUtc, coverage, resourceSets. Time comes from the trusted local clock; final completion must precede validUntilUtc and agree with the private cap.

resourceSets has exactly two entries, Jobs then Allocations, each with exact kind, status, coverage, observationIndex, items. status is observed/unavailable; coverage complete/partial/unavailable; index is canonical positive UInt64 decimal string or null when unavailable. Overall coverage is complete only when both sets are complete, otherwise partial. Sort each item list by ordinal ID; duplicate IDs or namespace mismatch invalidate the observation. Omitted required identity fields are invalid; missing optional fields are null, never fabricated zero/false.

Job exact fields: id, name, type, status. id/name are nonempty max-256 printable ASCII strings, JSON-escaped. type permits service/batch/system/sysbatch or null for unknown; status permits pending/running/dead or null for unknown. Allocation exact fields: id, jobId, taskGroup, clientStatus, desiredStatus. id is a canonical lowercase nonempty GUID; jobId max 256/taskGroup max 128 printable ASCII; clientStatus permits pending/running/complete/failed/lost/unknown or null; desiredStatus run/stop/evict or null. A partial job list does not require every returned allocation's jobId to be present in that page.

No job specifications, image references, env/arguments/templates/variables/Vault/Consul tokens, task events/logs/filesystem, node IDs/IPs, addresses, labels/metadata, raw provider messages, deployment/evaluation details or health conclusions. Running/complete are provider facts, not application readiness. Images require a later scoped detail-read contract; do not add GET job/allocation details to obtain them.

## Provider authority and implementation plan

The installation operator provisions an expiring client ACL token with only explicit namespace capabilities list-jobs and read-job, not a management token or coarse write/read role. Provider ACLs can permit more sensitive reads than this projection: the adapter must still enforce exact four-request/field allowlists. Filtered collection APIs can return 200 with no unauthorized entries; that is not proof of a permission grant. Expiry/revocation returns unavailable and cannot refresh/fallback automatically.

Planned files, not current source mappings:

- INV Contracts: src/Modules/InfrastructureInventory/Contracts/NomadWorkloadsV1.cs.
- INV runtime: src/Modules/InfrastructureInventory/Infrastructure/Nomad/{NomadTargetTrust,NomadTrustConfiguration,NomadReadOnlyTransport,NomadWorkloadProjection,NomadWorkloadReader}.cs.
- Host: src/Host/Kordra.Api/Authorization/NomadObservationConsumer.cs, Endpoints/NomadWorkloadEndpoints.cs; bounded edits to existing credential reader/registry, ResourceAuthorityServices and Hosting options/composition.
- IAM: existing Contracts/AuthorityAdmission.cs and Authorization/ProtectedReadAdmission.cs for approved additive fields/hash/freshness; no permission/data-model changes.
- Tests: tests/InfrastructureInventory.Bootstrap.Tests/{NomadTrustTests,NomadTransportTests,NomadProjectionTests}.cs; tests/IdentityAccess.Tests/T09NomadWorkloadHttpTests.cs for actual-owner HTTP; update existing architecture/admission vectors.
- docs/contracts/fixtures/nomad-workloads-v1/, docs/contracts/openapi/nomad-workloads-v1.openapi.json and docs/operations/nomad-workloads-v1.md at implementation time. Map paths when files exist and predeclare execution outputs before edits.

| Cases | Required deterministic evidence |
|---|---|
| N09-01 | Canonical vectors; reject malformed/duplicate/foreign installation/config/scope/namespace/index and wrong destination mode |
| N09-02 | Exact four paths/queries; no unlisted methods/endpoints, retry, page follow-up or raw output |
| N09-03 | Wrong CA/name/pin/time/destination, redirect/proxy/encoding failure sends no credential to an untrusted endpoint |
| N09-04 | Namespace recreation/restore, missing/foreign scope and incompatible configuration fail before disclosure; legacy hash vectors unchanged |
| N09-05 | Byte/depth/header/list/time limits, malformed/duplicate JSON and cancellation fail boundedly with no body |
| N09-06 | Complete empty vs unavailable vs partial; unknown leader; unavailable one/both collections; no fabricated health/count |
| N09-07 | Projection golden vectors, unknown status/nulls, cross-namespace records, secret canaries and no header/error export |
| N09-08 | Actual-owner wrong tenant/permission/platform/session/expiry/revoke tests cause no provider call or body |
| N09-09 | Mutations between U/C/provider/D, config mismatch, stale cap/clock jump and audit/unknown-helper failures retain fencing |
| N09-10 | Metadata/T08 compatibility, concurrent bounds and architecture/source ownership; deterministic required gates/PR evidence |

## Primary references

Nomad API paths, pagination and ACL semantics: [Jobs](https://developer.hashicorp.com/nomad/api-docs/jobs), [Allocations](https://developer.hashicorp.com/nomad/api-docs/allocations), [Namespace](https://developer.hashicorp.com/nomad/api-docs/namespaces). Token headers/region/default consistency: [HTTP API](https://developer.hashicorp.com/nomad/api-docs). Exact capabilities: [ACL policies](https://developer.hashicorp.com/nomad/docs/secure/acl/policies). Numerical limits, projection, identity residuals and Product integration above are Kordra design choices, not provider guarantees.

## Exact-content acceptance — 2026-10-24

NukeGeng approved this contract with ADR-0017 for T09.2a–d. See [ADR acceptance evidence](../decisions/0017-nomad-workload-observation.md#exact-content-acceptance-evidence--2026-10-24) for pre-annotation hashes and the private receipt. Acceptance covers the specified design and synthetic implementation checks; it is not Product implementation, N09 test evidence, company access or automatic lab Keychain activation.

## T09.2a implementation evidence — 2026-10-24

The local tree implements the strict immutable trust parser/canonical vectors and compatible admission configuration/scope/disclosure-cap additions. Existing Host composition deliberately rejects configured provider plans until T09.2c registers exact trust entries. This is partial implementation only: transport/projection/HTTP fixtures and full N09 conformance remain pending. Exact deterministic evidence is retained privately in .amr-local/t09/implementation/.

## T09.2b–e implementation/conformance evidence — 2026-10-24

The local tree now implements the exact four-GET Nomad transport, safe projection, config-bound registry/endpoint and stopped-installation activation. Synthetic TLS and actual-owner HTTP tests cover the N09 matrix; the separately recorded personal case passed native Keychain/U/C/D against the new local Nomad lab and deleted its isolated store. [OpenAPI](openapi/nomad-workloads-v1.openapi.json), [fixtures](fixtures/nomad-workloads-v1/http.json) and [runbook](../operations/nomad-workloads-v1.md) define wire/operation evidence. Final full gates/PR/CI and any permanent installation are separate; accepted normative rules above remain unchanged.
