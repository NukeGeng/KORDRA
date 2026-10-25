---
id: OPS-KUBERNETES-WORKLOADS-V1
status: implemented
owner: NukeGeng
last_reviewed: 2026-10-24
---

# Kubernetes workloads v1 — local pilot

[ADR-0016](../decisions/0016-kubernetes-workload-observation.md) and [contract](../contracts/kubernetes-workloads-v1.md) own T09.1a–d. Source implements a separate INV Kubernetes adapter and opt-in GET /api/v1/targets/{targetId}/workloads. It reports bounded facts, not application health or deployment success. Nomad uses its own route/configuration. Live Kubernetes T09.1e, Helm, company systems and existing credentials are not authorized or verified by these fixtures.

## Stopped-installation activation

This is private installation configuration, never browser input, environment fallback or appsettings. Under the existing canonical installation lock, a stopped API may load these additive local-pilot.json fields:

| Field | Value supplied under operator custody |
|---|---|
| KubernetesTrustConfigurationFile | Absolute owner-private regular JSON file, no symlink or symlink ancestor |
| KubernetesTrustConfigurationSha256 | Approved raw-file lowercase SHA-256 from an independent trusted source |
| CredentialExecutableFile | Approved installed Kordra.Api apphost |
| CredentialExecutableSha256 | Exact approved executable hash |
| CredentialKeychainLocators | Private locator UUID → absolute Keychain path map |

Each enabled provider needs its file/hash pair and the shared credential group. Kubernetes alone, Nomad alone and both are supported. No provider configuration leaves its endpoint absent; orphaned hash/credential fields fail startup. Removing one provider must retain shared credential configuration while the other is enabled. Restart invalidates old boot sessions; no hot reload or first-observed trust bootstrap.

The strict trust file shape and canonical entry digest are defined once in the contract. Obtain the exact installation/tenant/target/project-or-environment scope, HTTPS origin, approved IPs, CA DER, SPKI pin and namespace name/UID independently. A rebuilt namespace with the same name is a different target expectation. Namespace UID aliases to multiple targets are rejected. Configured scope is not a permission: current session, target.read, policy, ownership and active SEC binding are still required.

Production parsing rejects loopback/link-local/metadata/multicast/unspecified destinations; there is no production synthetic-loopback switch. HTTPS fixture tests construct loopback trust only inside their test assembly. Startup performs no provider read, Keychain import, database migration or RBAC provisioning. Existing installation/account/binding provisioning remains a separate operator task.

## Target privilege and transport

A separately authorized target administrator provisions list Pods, Deployments, StatefulSets and DaemonSets in exactly one namespace. Namespace identity additionally requires get on namespaces restricted by resourceNames to that exact namespace; a namespaced Role alone cannot grant it. Do not grant Secrets, ConfigMaps, watch, discovery, logs, exec, proxy, node or write privileges. Provider workload objects can contain literal environment data; RBAC cannot field-filter them. Projection discards such fields and never logs raw objects.

The adapter performs only six sequential logical GETs: exact Namespace, four lists in Pod/Deployment/StatefulSet/DaemonSet order, then exact Namespace. Both identity replies must match the independently configured UID/name and Active/nondeleting state. Lists have exactly limit=25&timeoutSeconds=1. No continue follow-up, retry package, discovery or cache. Framework/TCP recovery inside the existing deadline is not an exactly-once network guarantee.

Every request gets a fresh checked destination and TLS handshake with HTTP/1.1 exact, TLS 1.2/1.3, private CA/serverAuth/current validity/SAN/SPKI verification. Redirects, proxies, cookies, ambient credentials, decompression and certificate downloads/online revocation are disabled. The accepted pilot residual is offline revocation plus pinned key/stopped rotation, not production TLS qualification. Authorization Bearer exists only in invocation-local requests after C; framework-managed copies cannot be deterministically erased. Do not install instrumentation exporting request headers, bodies or raw errors.

Limits: one second/request inside five-second provider and ten-second invocation budgets; 16 KiB headers, 256 KiB/body, 1 MiB/attempt, JSON depth 32, 25 items/kind, 16 regular images/item and final body 256 KiB. Oversize/malformed/TLS/identity/deadline failures discard the whole result. An unavailable kind differs from an observed empty kind; at least one kind must be observed. Continuation marks partial without returning the token. Resource versions are opaque per-kind facts, not an atomic cross-kind snapshot.

Current U → Keychain → current durable C → provider → current durable D → caller-bound response remains unchanged. Trusted capture+15 seconds caps D along with the earliest current/original owner and SEC expiry. Revocation during I/O or expiry during D prevents output. Unknown helper/commit outcomes retain existing fencing; do not replay effects to conceal an unresolved state.

## Validation, evidence and rollback

[HTTP fixture](../contracts/fixtures/kubernetes-workloads-v1.http.json) and [OpenAPI](../contracts/openapi/kubernetes-workloads-v1.openapi.json) define exact public fields and source pins. INV tests cover canonical trust/file custody, pinned synthetic HTTPS, protocol/size/identity/partial failures and typed projections. Actual-owner IAM HTTPS tests exercise project/environment U/C/D, wrong authority, SEC revoke, delayed freshness, failed C audit and withheld output, including oversized projected output. Existing T07/T08 and Nomad regressions remain required. Private current-session receipts are in .amr-local/t09/kubernetes/; historical failed checkpoints are retained unchanged, without native GOAL completion claims.

No real Kubernetes version, RBAC installation, cluster or existing Keychain item has been validated. T09.1e needs separately identified target, rights-owner authorization and private credentials. Deterministic tests do not establish a complete deployable Operator Pilot or authorize company operations. Codex Auto PR Review and human merge are separate PR boundaries; no additional local AI review is run.

For rollback, stop the API, remove the Kubernetes optional file/hash pair and restart. Remove the shared credential group only if no provider remains enabled. Preserve SEC/AUD history and Nomad configuration. Endpoint removal does not revoke the provider credential; its owner manages revocation separately. Do not stop Website services or alter Clients.
