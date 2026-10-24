---
id: CTR-KUBERNETES-WORKLOADS-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-24
---

# Kubernetes workloads v1 — T09.1

Normative accepted companion to [ADR-0016](../decisions/0016-kubernetes-workload-observation.md), owned by FEAT-INV-001 with IAM/SEC/AUD composition. NukeGeng accepted the exact T09.1a–d design on 2026-10-24; implementation may proceed within that scope. The owner selected Kubernetes workloads first; Helm release inventory remains later. No existing credential/cluster access or implementation evidence is conferred here; technology adoption is limited to accepted ADR-0016.

## 1. Ownership and compatibility

INV owns target trust expectations, provider transport and workload snapshots. SEC retains ConnectionProfile/SecretReference/CredentialBinding and borrowed credential use. IAM retains current authorization, U/C/D and disclosure. Host composes these through owner ports; no runtime assembly reads another owner's database. OBS remains a prospective consumer and health-policy owner: no healthy/business-verification result, StatusProjection, new OBS project or broad module adoption in this slice.

Reuse existing .NET 10 BCL networking/JSON and existing projects/packages. INV Contracts stay BCL-only and contain no credential-returning method. The trusted INV runtime transport may consume borrowed credential bytes through the Host's registered SEC consumer, never through a public HTTP/business contract. No driver/Agent/worker or new persistence.

GET /api/v1/targets/{targetId} remains the existing local seven-field metadata v1 response. The new opt-in route is GET /api/v1/targets/{targetId}/workloads, with its own schemaVersion 1 DTO and strict fixtures/OpenAPI. No Clients repository edits or assumptions about implemented frontend consumers.

## 2. Immutable installation target trust

Use an API-stopped, owner-only regular JSON file with an out-of-band approved SHA-256 and installation identity, loaded once under canonical custody. Reject symlinks, duplicate/unknown fields, trailing data, missing identities, oversized files (64 KiB maximum), more than 32 entries and duplicate target identities. Do not accept a config path/hash from browser requests. Absence leaves the new capability unregistered.

Root keys are exactly schemaVersion (1), installationId and targets, in that canonical order. Each targets entry has exactly tenantId, targetId, scopeKind (project/environment), scopeId, apiOrigin, destinationIps, caCertificateDerBase64, serverSpkiSha256, namespaceName and namespaceUid. All fields are required; reject duplicate/unknown/null fields. Use 1–8 IPs and one CA/one server key pin per entry. CA DER is at most 16 KiB, parses as a CA certificate with certificate-signing usage and contains no private key. Scope IDs are trusted catalog expectations, not permission grants. No token, kubeconfig, exec command, browser context, arbitrary path or online credential binding.

Origin must have no userinfo/query/fragment or custom base path. Namespace is one DNS label, at most 63 characters; UID is a nonempty opaque identity of at most 128 characters without whitespace/control characters. No wildcard, namespace selector or all-namespaces mode. Reject registering the same namespace UID to multiple distinct targets within the installation; sharing/aliasing requires a later explicit design.

Canonical entry digest is SHA-256 of compact UTF-8 JSON with keys in this exact order: schemaVersion, installationId, tenantId, targetId, scopeKind, scopeId, apiOrigin, destinationIps, caCertificateDerSha256, serverSpkiSha256, namespaceName, namespaceUid. Use schemaVersion 1, nonempty lowercase D-form GUIDs, lowercase 64-hex digests, no insignificant whitespace, ordinal-sorted unique normalized IP strings and the CA DER SHA-256 instead of input Base64. Origin canonicalization uses lowercase ASCII host, no trailing dot/IDN/zone identifier, bracketed IPv6 and omits default port 443; reject ambiguous/userinfo encodings. Normalize IPv4-mapped addresses before validation/comparison. Store/compare the approved original file hash separately; tests must pin canonical vectors. No private origin, CA/pin, destination address or UID goes to the browser/public evidence. Approved config changes require stopped API/restart and new hash; boot/session invalidation prevents continuation under a replaced config. No hot reload or silent DNS/namespace fallback.

This is INV target-trust metadata, not a replacement SEC ConnectionProfile. An active SEC binding for that same trusted tenant/target/consumer is still required.

## 3. Plan, credential and U/C/D integration

Add an optional ConsumerConfigurationDigest to ProtectedReadPlan and optional trusted ScopeKind/ScopeId to ReadDependencySelection. Defaults preserve existing metadata/T08 producer/consumer constructors. Scope fields are either both null (legacy) or exact valid project/environment plus nonempty scope ID (configured T09). Preserve the legacy metadata v1 / credential v2 authority-hash serialization when the new fields are null. Config-qualified T09 uses internal authority-digest schemaVersion 3, explicitly including its configuration digest and trusted scope; this is not a new business message or AUD wire schema. Metadata cannot carry a provider digest; production T09 plans require a valid digest and exact configured scope. Add compatibility hash vectors and do not rely on automatic record serialization to preserve old bytes.

The Host dependency adapter validates the registered digest, installation/tenant/target/scope and current SEC snapshot under the common fence before U. Keep ReadDependencySnapshot.Digest as the SEC digest consumed by CredentialBroker; include the immutable provider digest/scope in IAM's invocation authority digest instead of substituting a composite digest into the broker. Existing synthetic T08 consumers remain test-only; no implicit production registration for a null-digest plan.

Select the registered target plan on the server, never from HTTP input. Missing configuration/foreign scope cannot reveal target existence or reach a helper. Current session, exact target.read grant/definition, resource policy and owner facts remain necessary. Platform-only authority is insufficient. No new permission key.

Sequence: current U → bounded Keychain lookup/confirmed helper exit → current C plus durable C intent → consumer TLS/namespace/workload reads → trusted freshness/result → current D plus durable D intent → caller-bound output. Preserve invocation/tenant/target/scope and earliest original/current expiry throughout. No reusable permit or later retargeting.

Extend PrivateReadBuffer with an optional UTC DisclosureValidUntilUtc, rejecting non-UTC, default or DateTimeOffset.MaxValue values. The existing two-argument constructor remains valid with a null cap. Existing metadata buffers use null, retaining behavior. T09 uses capture time + 15 seconds; D must recheck the minimum of this deadline and original/current authority/SEC deadlines before commitment and handoff. A clock jump/expiry between consumer completion and D yields no body. The serialized expiry and private cap must agree; provider timestamps cannot extend it.

## 4. Credential representation and lifetime

Only opaque-bytes-v1 from the existing inventory.readonly.v1 / target.read binding. Interpret bytes as a bearer token only in this registered transport after C: 1–4096 bytes, ASCII bearer-token syntax, no whitespace/control characters, CR/LF or embedded configuration. No JSON/kubeconfig parsing, refresh flow, exec plugin, ambient credentials or alternate reference.

The accepted managed-header exception is exactly ADR-0016's narrow T09.1 exception; other consumers retain their existing prohibition. A per-call request may contain the transient managed Authorization header; never HttpClient.DefaultRequestHeaders, a retained request/token field, cookie, URI or log. Use no logging/enrichment handler that exports request headers/body or raw provider errors. Trusted diagnostic listeners/exporters must not capture authorization; installing new HTTP instrumentation requires review. Dispose request/response and handler scope; clear owned mutable buffers. No deterministic managed-string/framework-memory erasure claim.

No new helper mode or subprocess. Existing Keychain helper isolation/slots remain unchanged. The consumer receives only its invocation's borrowed bytes; it cannot retain or reuse them after completion/cancellation.

## 5. TLS and destination policy

Use SocketsHttpHandler/HttpClient, fixed HTTPS origin and HTTP/1.1 exact for the initial adapter. Use an invocation-owned handler/client, PooledConnectionLifetime zero and Connection: close for each logical request, so each call gets a fresh checked destination/TLS handshake; do not reuse a cross-invocation connection or token-bearing client. Disable ambient proxies, redirects, cookies, automatic decompression and credential fallback. Reject unexpected Content-Encoding rather than decompressing unbounded input. Never send authorization to an HTTP URL or redirected origin.

Resolve configured DNS with cancellation and connect only to its explicit approved IP set, checking the actual selected address in the connection callback. RFC1918/private addresses may be explicitly approved for the target; reject unconfigured, loopback, link-local/metadata-service, unspecified and multicast destinations in production. Synthetic loopback HTTPS uses test-only construction, not a production bypass flag. DNS changes outside the approved set fail closed.

Allow TLS 1.2/1.3 only. Before any authorization bytes are transmitted, require hostname/IP SAN, server-auth chain/validity to the exact configured private CA and the exact server SPKI SHA-256 pin. No permissive certificate callback, hostname override or unknown-CA fallback. Disable certificate downloads so validation cannot introduce arbitrary AIA/CRL/OCSP destinations. Use trusted current time for validity.

The accepted local pilot chain policy does not fetch online revocation status. The mandatory leaf key pin, offline trust custody and stopped-API rotation are explicit residual controls, not a claim of real-time certificate revocation. Bad TLS/pin/destination prevents namespace/workload requests and all output. Future production TLS/revocation support needs its own decision/evidence.

## 6. Exact provider/RBAC allowlist

| Operation | Exact path | Provider privilege |
|---|---|---|
| Namespace identity before reads and again after reads | GET /api/v1/namespaces/{configuredName} | get namespaces, resourceNames restricted to that exact name |
| Pods | GET /api/v1/namespaces/{configuredName}/pods | list pods in that namespace |
| Deployments | GET /apis/apps/v1/namespaces/{configuredName}/deployments | list deployments in that namespace |
| StatefulSets | GET /apis/apps/v1/namespaces/{configuredName}/statefulsets | list statefulsets in that namespace |
| DaemonSets | GET /apis/apps/v1/namespaces/{configuredName}/daemonsets | list daemonsets in that namespace |

The identity GET is cluster-scoped but limited to that one named Namespace; a namespaced Role alone cannot grant it. A target administrator provisions the exact Role/RoleBinding and narrowly named ClusterRole permission separately. Kordra never creates RBAC or needs cluster-admin. Kubernetes [RBAC](https://kubernetes.io/docs/reference/access-authn-authz/rbac/) distinguishes resource/subresource and namespace scopes; a GET verb alone is not a safe allowlist.

No Secrets, ConfigMaps, service-account/token operations, raw manifests, logs/events/metrics, Nodes/proxy, exec/attach/port-forward, watch, discovery, arbitrary URL or write verb. Workload GET/list privilege inherently reads the allowed Kubernetes objects, which may contain literal environment data; the bounded parser must skip those properties without logging/persisting them. HTTP projection is an additional restriction, not a claim that provider RBAC filters object fields.

After C and TLS validation, require exact namespace name/UID and Active phase before workloads. Repeat identity check afterward; changed/missing/deleting namespace discards all results. Expected identity comes from independently provisioned trust, never a context label, same credential's claim or first observed UID. Cluster endpoint/key and namespace identity are distinct; do not advertise a universal Kubernetes cluster GUID or business-health proof.

## 7. Bounds, partial coverage and freshness

Preserve T08's two helper slots inside 16 invocations; Keychain 2 seconds plus 500 ms cleanup; existing provider 5 seconds from U, invocation 10 seconds and gate/audit bounds. C supplies no extra time. At most six application HTTP operations: two namespace checks and one list for each of four kinds. No discovery/pagination/watch/application retry, background poller or request queue.

Use one request at a time, in fixed order: namespace, Pods, Deployments, StatefulSets, DaemonSets, namespace. List queries are exactly limit=25 and timeoutSeconds=1; namespace checks have no query. Request deadline is min(1 second, remaining provider/invocation time). BCL/TCP recovery of an idempotent GET may occur inside that same budget; six logical calls is not an exactly-once network guarantee and creates no new C admission. Do not add resilience/retry packages.

Each list requests limit=25, no continue replay. More than 25 returned items is invalid. A nonempty continue token marks that kind partial and is never returned to the caller. Maximum 100 resources total, 256 KiB per response, 1 MiB response bytes across the attempt, JSON depth 32 and final protected body at most the existing 256 KiB. Namespace replies share the byte/deadline budget. Enforce actual streamed bytes, including chunked/no-length responses; reject inconsistent/oversized content without clipping a success.

Use trusted Kordra clock: capturedAtUtc at first verified namespace identity; completedAtUtc after the final identity check; validUntilUtc = capturedAtUtc + 15 seconds. Reject future/reversed/stale times. Each kind retains its own opaque resourceVersion; there is no atomic cross-kind snapshot or numeric version ordering.

A valid first page may be partial. A bounded per-kind 403/unavailable response contributes an unavailable set, not an observed empty set; it may coexist with successfully observed sets if both identity checks and the overall deadline succeed. At least one kind must be observed. TLS/identity/parse/size failures, cancellation, deadline or missing final identity fail the whole invocation without D/body. No cache, stale fallback, auto-retry or false healthy/unhealthy conclusion.

## 8. Public workload DTO

Success schemaVersion 1 fields are exactly: schemaVersion, invocationId, targetId, namespace, capturedAtUtc, completedAtUtc, validUntilUtc, coverage, resourceSets. Coverage is complete only if all four sets are observed without continuation; otherwise partial. Scope/tenant/credential/trust digests are not browser fields.

Each resource set: kind, status (observed/unavailable), coverage (complete/partial/unavailable), resourceVersion (opaque or null), items. Unavailable sets have null version and empty items but must not be interpreted as verified empty inventory.

Sets appear in the fixed Pod/Deployment/StatefulSet/DaemonSet order. Items are ordinal-sorted by name then UID; reject duplicate kind/name or UID. List/item API kind/group, when present, must match the requested path. Require item namespace to equal the configured namespace. Unknown JSON fields are skipped, never passed through; reject duplicate JSON properties and malformed projected fields.

Each item has exactly kind, name, uid, generation, observedGeneration, desiredReplicas, readyReplicas, availableReplicas, podPhase, podReady and images. Name is at most 253 ASCII DNS-subdomain characters; UID/resourceVersion at most 128 non-whitespace/control ASCII characters. Inapplicable/missing facts are null, not invented zero/false. Counts are nonnegative Int32. For Deployment/StatefulSet, replicas come from spec.replicas and status readyReplicas/availableReplicas; for DaemonSet use desiredNumberScheduled/numberReady/numberAvailable. Generation/observedGeneration are copied where available, not treated as proof of health. Pod replica counts are null; podPhase is Pending/Running/Succeeded/Failed/Unknown and Ready condition True/False/Unknown maps to true/false/null. Duplicate Ready conditions are invalid.

Images cover regular containers only, at most 16. Each image has exactly containerName (DNS label, max 63), reportedReference (max 512), declaredDigest and reportedRuntimeDigest (nullable normalized sha256:64-lowercase-hex). Take controller references from spec.template.spec.containers and Pod references from spec.containers. References must be visible ASCII, no whitespace/control, URI scheme, userinfo, query or fragment; accept the conservative OCI repository[:tag][@sha256:digest] form, never an arbitrary URL. Unsupported reference/digest syntax yields null for that image evidence, never a verified version or guessed tag. Pod runtime digest may normalize only an exact sha256 digest, containerd://sha256 digest or docker-pullable reference ending in a valid sha256 digest, matched by unique container name. Do not return the raw imageID. Init/ephemeral containers, tag resolution, registry discovery and all-complete image-version claims are outside v1. Exceeding item/image/string limits fails the response rather than silently truncating a successful object.

Exclude labels/annotations/managedFields, provider endpoints/IPs, pod IPs/node names, env/envFrom, volume/config/secret contents or references, raw conditions/messages/errors and Helm release payloads. Projection uses typed allowlisted fields; no raw object/JsonElement passthrough. Reject duplicate/foreign-namespace/malformed identities rather than merging them. Workload Running/readiness/replica counts are reported facts, not business-health or deployment-verification results.

Require existing same-origin session, GET with no query/body/transfer encoding, no-store and caller-bound D. Authentication/denial/unavailable/abort follows existing guarded route behavior, including safe pre-header aborts; no untrusted provider error body is forwarded. The public validUntilUtc denotes snapshot freshness only, never continuing permission or browser-cache authority. Existing metadata examples remain unchanged. Deliver exact new fixtures/OpenAPI and consumer-compatibility tests before route activation.

## 9. Scoped implementation tasks and actual adoption

| Slice | Existing owner paths / required outputs |
|---|---|
| T09.1a | INV Contracts/Infrastructure/Kubernetes, IAM AuthorityAdmission/ProtectedReadAdmission and Host Authorization/Hosting/Composition: strict trust canonicalization, registered scope/digest and disclosure-expiry compatibility. |
| T09.1b | INV Infrastructure/Kubernetes: cancellation-aware pinned transport, exact identity/RBAC operations, streaming limits and typed projection; test-only synthetic TLS endpoints. |
| T09.1c | Host Endpoints/Authorization/Composition: registered SEC consumer and workloads route; actual session/grant/policy/SEC U/C/D; preserve metadata default. |
| T09.1d | Existing INV Bootstrap/IdentityAccess/ConnectionManagement tests, actual XML/PE architecture assertions, new workloads HTTP fixtures/OpenAPI, operator runbook, feature/traceability/change history and exact-base deterministic gates. |
| T09.1e | Separate actual target identity, rights-owner read authorization, approved endpoints/namespaces/resources, case/evidence and private credentials before any live observation. |

Use existing projects only; do not infer broad acceptance of proposed ADR-0009 source architecture. Future source/test files are planned until created and mapped. No test-only loopback/fake consumer switch in production configuration.

### Planned exact implementation files — not created by this design

- INV Contracts: src/Modules/InfrastructureInventory/Contracts/KubernetesWorkloadsV1.cs.
- INV runtime: src/Modules/InfrastructureInventory/Infrastructure/Kubernetes/KubernetesTargetTrust.cs, KubernetesTrustConfiguration.cs, KubernetesReadOnlyTransport.cs, KubernetesWorkloadProjection.cs and KubernetesWorkloadReader.cs in that same directory.
- Host new files: src/Host/Kordra.Api/Authorization/KubernetesObservationConsumer.cs and src/Host/Kordra.Api/Endpoints/KubernetesWorkloadEndpoints.cs. Update existing Authorization/CurrentCredentialDependencyReader.cs, Authorization/CredentialTargetRead.cs, Composition/ResourceAuthorityServices.cs and Hosting/LocalPilotHostOptions.cs/LocalApiHost.cs only as required for opt-in configuration/registration.
- IAM existing files: src/Modules/IdentityAccess/Contracts/AuthorityAdmission.cs and Authorization/ProtectedReadAdmission.cs for additive plan/scope/deadline changes. Add no runtime dependency from INV to IAM or SEC.
- New tests: tests/InfrastructureInventory.Bootstrap.Tests/KubernetesTrustTests.cs, KubernetesTransportTests.cs and KubernetesProjectionTests.cs in that same test directory; tests/IdentityAccess.Tests/T09KubernetesWorkloadHttpTests.cs. Extend existing architecture and admission tests only for affected compatibility/edges.
- Wire/runbook: docs/contracts/fixtures/kubernetes-workloads-v1.http.json, docs/contracts/openapi/kubernetes-workloads-v1.openapi.json and docs/operations/kubernetes-workloads-v1.md. No sample real endpoint, credential or cluster identity.

After approval, the Developer predeclares exact task outputs in the existing owner plan/spec before creation, then maps actual source/tests and evidence. No placeholder project or source scaffold is required for this approval.

## 10. Mandatory acceptance matrix

| ID | Required evidence before implementation completion |
|---|---|
| K09-01 | Project and environment scope succeed through actual HTTPS + current owners/SEC with distinct U/C/D; metadata remains independent. |
| K09-02 | Wrong tenant/target/scope/config digest, platform-only grant, missing/revoked SEC and invalid token invoke no unauthorized consumer/read/output. |
| K09-03 | Invalid CA/SAN/time/SPKI, DNS rebind, redirect/proxy/environment-credential attempt and forbidden destination disclose no authorization to an untrusted endpoint. |
| K09-04 | Missing/wrong/recreated namespace UID before/after reads yields no workload disclosure; no context-name or first-seen trust bootstrap. |
| K09-05 | Instrumented provider sees only the six logical allowlisted operations, exact configured namespace and approved GET paths; no Secrets/exec/proxy/write/discovery. |
| K09-06 | Header/body/chunked/compression/depth/item/output limits, duplicate JSON/identities, foreign namespaces and cancellation produce safe failure without retry or raw diagnostic/secret output. |
| K09-07 | Partial first page and per-kind unavailability remain distinguishable from verified empty; no healthy or complete assertion from partial evidence. |
| K09-08 | Controlled clock crosses capture+15s between consumer completion and D; no body. Existing authority/SEC expiry/revoke/config-scope changes also deny; no extra provider/overall time. |
| K09-09 | Revocation during lookup prevents C; after C/while provider waits prevents D; unavoidable already admitted packets are not claimed retractable. |
| K09-10 | Unknown audit/consumer task retains fencing; known startup failure still releases slots, as fixed in PR15. All T07/T08 fairness/cleanup/capacity/transport regressions remain. |
| K09-11 | Credentials never appear in public JSON, app logs, safe errors, audit, fixtures or URLs; mutable copies clear; managed-header residual is tested/documented without a false zeroization claim. |
| K09-12 | Actual restored/PE dependencies remain isolated/BCL Contracts; private trust config is not a permission. No startup DDL, extra projects/packages, real cluster, Clients/Website modification or GOAL replay. |

Test normal malformed/unavailable/time/race paths through loopback synthetic HTTPS and fake credential lookups first. Native Keychain proof can reuse only its separately approved new synthetic fixture scope; real Keychain items and clusters remain excluded. Conformance targets core/v1 and apps/v1; advertise no verified real Kubernetes/Helm version matrix before separately authorized live validation.

## 11. Handoff, rollback and decisions

[NEEDS-DECISION] Accept ADR-0016 and this exact contract, including the bounded managed-header exception, private-PKI offline validation residual, single named namespace bootstrap privilege and additive current-plan/disclosure-expiry supplement. Scope preference alone is not approval. Sol High ends after this bounded design and human decision; Luna implements eligible slices without another design/review loop.

Disable workloads registration to roll back. Preserve local metadata route, SEC/AUD history and original checkpoints. Existing real installation provisioning/workspace initialization remains separate work; fixture success is not a usable full Operator Pilot, OBS health result, Beta qualification or production authority.

## Exact-content acceptance — 2026-10-24

NukeGeng approved this contract with ADR-0016 for T09.1a–d, including the four explicit transport/trust/admission decisions. See the [ADR acceptance evidence](../decisions/0016-kubernetes-workload-observation.md#exact-content-acceptance-evidence--2026-10-24) for pre-annotation hashes and the private receipt. No T09.1e or existing credential/cluster operation is authorized. Runtime and K09-01–12 verification remain pending.

## Implementation continuation — 2026-10-24

T09.1a–d source and synthetic conformance are now implemented in the [owner feature](../features/infrastructure-inventory/features/register-target.md#t091-approved-implementation-continuation--2026-10-24), with [OpenAPI](openapi/kubernetes-workloads-v1.openapi.json), [HTTP fixture](fixtures/kubernetes-workloads-v1.http.json) and [runbook](../operations/kubernetes-workloads-v1.md). This dated annotation supersedes earlier pending-implementation wording only; normative design and exact acceptance remain unchanged. Final gate receipts and remote PR review remain separate; no T09.1e or real cluster/version evidence is claimed.
