---
id: ADR-0016
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-24
---

# ADR-0016 — Namespaced Kubernetes workload observation, T09.1

## Status and bounded decision

Accepted by NukeGeng on 2026-10-24 for T09.1a–d together with [Kubernetes workloads v1](../contracts/kubernetes-workloads-v1.md). The earlier workload-first preference is distinct from this exact-content acceptance. Live access remains excluded.

Design baseline: merged PR15/main 40acd58, accepted ADR-0012/0014/0015 and implemented T07/T08. Native current-turn metadata identifies gpt-6.1-sol/high for this bounded Architect task. This is design, not an independent PR review, implementation, test result or permission to read an existing credential.

## Problem

T08 provides offline SEC metadata, exact binding and U/C/D, but intentionally registers no network consumer. T09 still needs a destination/identity/RBAC contract, bounded workload projection, freshness at disclosure and a supported credential transport. Current metadata v1 must not be replaced with a network-dependent response.

Kubernetes/Helm read-only was the chosen overall pilot direction. The owner now prioritizes namespaced Kubernetes workloads. Helm remains a separate later slice: its documented release storage can contain chart/values and sensitive data; reading Helm release Secrets is not covered by this decision. See [Helm storage](https://helm.sh/docs/topics/advanced/#storage-backends).

## Alternatives

| Alternative | Quality and adoption tradeoff |
|---|---|
| Existing .NET 10 HttpClient / SocketsHttpHandler and streaming System.Text.Json | Selected. No new package or executable; explicit endpoints, TLS, byte/request/deadline limits and testable sockets. Requires the narrow managed-header exception below. |
| Kubernetes C# SDK | Not selected initially. A new package/version, discovery/configuration behavior and credential handling need additional adoption evidence; it does not inherently solve string-based authorization headers. |
| kubectl/Helm subprocesses or ambient kubeconfig | Not selected. Adds executable/version, exec-plugin, process, configuration and secret-file boundaries before the bounded read has been proved. |
| Custom HTTP/TLS implementation to avoid managed header strings | Rejected for this slice. Reimplementing protocols would introduce disproportionate correctness/security risk. |

## Accepted decision

1. T09.1 reads only Pods, Deployments, StatefulSets and DaemonSets in one explicitly configured namespace per target. It does not implement Helm inventory, logs, events, metrics, Nodes, health probes, writes, streaming or full OP-P0. INV owns these infrastructure snapshots. OBS retains health-policy/verification ownership; no OBS runtime or ownership reassignment is adopted here.
2. Use only existing INV runtime/Contracts, IAM Contracts/runtime, SEC consumer boundary and API composition. Add no production project, package, SDK, CLI, database table, worker, broker or service.
3. Register a separate workloads route only through explicitly loaded private installation trust configuration. The existing seven-field metadata route and default configuration remain unchanged.
4. Bind installation/tenant/target/project-or-environment scope to a private immutable target-trust entry with approved HTTPS origin, destination IPs, CA, server SPKI pin, namespace name/UID and configuration digest. No browser-supplied endpoint, namespace, credential or provider plan.
5. Validate normal TLS identity plus the pinned trust before transmitting authorization. After C, authenticate the exact namespace UID before workload reads and again after them. Missing/mismatched identity yields no disclosure. Read authorization is not permission to provision target RBAC.
6. Extend the existing in-process plan/dependency selection additively to bind the configuration digest and exact trusted scope at U/C/D. Keep the SEC snapshot digest separate so the broker still verifies its original binding. Configuration changes require stopped-API maintenance/restart; there is no live reload.
7. Add an optional disclosure deadline to PrivateReadBuffer. T09 sets it to the trusted capture time plus 15 seconds; D caps it with original/current grant, policy, session and SEC expiry. Existing constructors/default metadata behavior remain compatible.
8. Use bounded namespace/kind requests and sanitized new observation DTOs. Partial page coverage is explicit; missing/failed evidence is not healthy or an empty verified inventory.

## Explicit exception to the T08 managed-string rule

T08 says no managed secret strings. Ordinary .NET authorization header representations use strings; see [AuthenticationHeaderValue](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.headers.authenticationheadervalue).

This accepted ADR authorizes only the registered T09.1 transport, after C, to create temporary managed bearer-header representations from validated borrowed bytes. They may exist only in per-call request/transport state; never default headers, a singleton field, cache, URL, configuration, logs, diagnostics export, exception or DTO. Dispose requests/responses and clear owned mutable copies on every path. Keychain child/broker IPC remains raw bytes with its existing zeroing rules.

Managed strings and framework/TLS copies cannot be deterministically zeroed. This residual is explicitly accepted only for the local in-process pilot; it is not a perfect-erasure guarantee or a general exception for other consumers. Do not implement a string conversion under current ADR-0015 alone. The T08 contract records this exact limited supplement.

## Accepted TLS, network and operational residuals

The precise policy is in the contract. Use private CA trust, hostname/SAN and server SPKI pin, no insecure bypass, no redirects/proxies/cookies, no arbitrary DNS destination and no external certificate-download/OCSP/CRL requests. The local private-PKI pilot uses offline chain validation without revocation fetching, with certificate validity checks, a mandatory server-key pin and API-stopped trust maintenance. This does not establish online revocation or a generally production-ready TLS policy.

No application retry is added. Framework/TCP retransmission or transparent idempotent GET recovery can occur within the same deadline; application request counts are not a claim of exactly-once wire delivery. U/C is not instant revocation of already admitted provider packets. D still prevents unauthorized/stale output.

These residuals, the managed-header exception and the bounded namespace GET bootstrap are part of this accepted scope, not hidden implementation choices.

## Delivery slices and acceptance

- T09.1a: strict immutable installation trust configuration and additive plan/scope/disclosure-expiry compatibility.
- T09.1b: bounded INV read transport, exact namespace identity, RBAC and allowlisted projection against synthetic HTTPS.
- T09.1c: Host SEC consumer/route composition and actual U/C/D HTTPS interleavings; metadata compatibility.
- T09.1d: source-bound deterministic gates, new HTTP fixtures/OpenAPI, runbook and traceability. Codex Auto PR Review and human merge remain later PR boundaries.
- T09.1e: separately authorized real-target read validation, only after implementation/configuration and rights-owner approval. It is not authorized by accepting T09.1a–d.

Do not mark T09, OP-P0, OBS health or production readiness complete from fixture tests. Helm/remaining pilot observations retain separate scope/design gates.

## Approval and rollback

NukeGeng explicitly accepted this ADR and its exact contract on 2026-10-24: “Duyệt đúng phạm vi ADR-0016 và contract”. No extra AI review loop or retrospective T08 approval is requested. Architect returns after this bounded design; Developer implements approved slices.

Rollback disables workloads registration and preserves the existing metadata API, SEC/AUD authority history and completed checkpoints. No destructive Down, credential deletion, client/submodule modification or real infrastructure change is part of rollback.

## Exact-content acceptance evidence — 2026-10-24

Approved proposal SHA-256: ADR `f248c0a491642ddeb76e9bb41d0f539dd5562fae652a91f2e4a91ad1e82c58d5`; contract `2995b34cccc1e54eb70cf91601bbf9a9dcbf8dabcf7fcefc847f94f061def925`. Original bytes and owner decision are preserved privately in `.amr-local/approvals/adr-0016-t09/acceptance.json`. Acceptance annotations do not alter the substantive design. T09.1a–d is approved for implementation and synthetic fixtures; T09.1e, existing secrets and real clusters remain excluded. Design acceptance is not implementation or validation evidence.
