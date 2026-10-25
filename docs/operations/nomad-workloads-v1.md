---
id: OPS-NOMAD-WORKLOADS-V1
status: implemented
owner: NukeGeng
last_reviewed: 2026-10-24
---

# Nomad workloads v1 — installation and validation

## Scope and ownership

FEAT-INV-001 owns read-only jobs/allocation facts, composed through existing IAM/SEC/AUD under accepted [ADR-0017](../decisions/0017-nomad-workload-observation.md) and [contract](../contracts/nomad-workloads-v1.md). This is a local Operator Pilot capability, not a deployment engine, full OP-P0, OBS health policy or production-readiness claim. Kubernetes T09.1 remains accepted/deferred; Helm is excluded.

The opt-in GET /api/v1/targets/{targetId}/nomad-workloads requires the existing HTTPS local session and current tenant/project/environment target.read authority. It accepts no query, body, namespace/region override or browser credential. The metadata route remains unchanged. A successful response may have partial collection coverage; observed empty, unavailable and partial are distinct. Nomad running/complete is not application health.

## Installation: stopped API and explicit private configuration

Follow [local authentication](../contracts/local-authentication-v1.md), [resource policy](resource-policy-v1.md) and [credential boundary](credential-boundary-v1.md) custody/provisioning. Hold the canonical installation lock with the API stopped; do not migrate databases at API startup. Reuse current installation identity, authoritative target ownership, current grant/policy and an active SEC binding for inventory.readonly.v1 / target.read. The application token must be an expiring Nomad client token scoped to the exact namespace with only list-jobs/read-job; never use the lab management token.

The existing private local-pilot.json has these optional additive fields; no environment/appsettings fallback exists:

| Field | Required when enabling Nomad |
|---|---|
| NomadTrustConfigurationFile | Absolute owner-private regular trust JSON file; no symlink path |
| NomadTrustConfigurationSha256 | Approved raw-file lowercase SHA-256, supplied out of band |
| CredentialExecutableFile | Approved installed Kordra.Api apphost path |
| CredentialExecutableSha256 | Exact approved apphost SHA-256 for native helper execution |
| CredentialKeychainLocators | Private map of nonempty locator UUID to absolute isolated Keychain path |

All five must be present together; credential fields without trust configuration fail startup. Absence leaves the Nomad endpoint unregistered. Installation/target trust and helper config are immutable for the boot; configuration change requires stopped-API maintenance/restart, which invalidates the old boot's sessions. Startup validates trust before route registration and does not import/create Keychain items.

Trust JSON has exact root schemaVersion=1, installationId, targets. Each entry has the exact tenantId, targetId, scopeKind, scopeId, apiOrigin, destinationIps, caCertificateDerBase64, serverSpkiSha256, region, namespaceName, namespaceCreateIndex, destinationKind keys specified in the contract. Obtain expectations through trusted operator custody, not first-seen provider values. Personal-loopback requires explicit 127.0.0.1 plus HTTPS port/CA/pin/hash; it is not a public bypass switch. Other private-network targets retain strict destination validation. A restored/rebuilt namespace needs re-enrollment; CreateIndex is not a globally unique cluster identity and server keys must not be reused across installations.

Create only a separately authorized new Keychain item under the installation service/reference identifiers with the designated executable ACL. Keep bytes out of tracked config, commands, logs and response fixtures. Keep offline binding receipts and approved artifact hashes under the existing runbook; no new credential HTTP API exists. Rotation/revoke uses SEC's existing offline revision/audit workflow. Record provider token expiry so SEC binding expiry cannot outlive it operationally; Nomad-side expiry/revoke still fails closed on use. No fallback to management credentials or automatic refresh.

## TLS and transport

Each invocation uses four sequential bounded HTTPS GETs: exact Namespace, jobs page, allocations page, exact Namespace again. No discovery, detail reads, pagination follow-up, writes, logs, variables, exec or streaming. Each request has at most one second within the existing five-second U provider budget/ten-second invocation budget. HTTP/1.1 exact, fresh checked connection per request, 16-KiB response headers, 256-KiB body, 1-MiB total, JSON depth 32 and 25 items per collection are enforced. A next-page token marks partial and is never returned or followed.

Both initial TLS validation and callback chain rebuilding disable certificate downloads/online revocation, require the configured private CA/serverAuth/hostname/current validity/SPKI pin and TLS 1.2/1.3. Redirects, proxies, cookies, decompression and ambient credentials are disabled. Managed X-Nomad-Token representations exist only in per-request post-C state; framework copies cannot be perfectly erased. Do not enable instrumentation exporting HTTP headers/body/errors. These explicit pilot residuals do not establish a general production TLS/secret-isolation guarantee.

U/C/D rechecks exact current owner scope and config digest, retaining SEC's original binding digest. Result expiry is trusted capture+15 seconds and cannot extend session/grant/policy/SEC expiry. Revocation, stale/invalid provider evidence, audit failure or unresolved helper outcome withholds output. Unknown helper/commit outcomes fence shared admission; investigate using the existing credential/admission runbook, never replay effects or restart to conceal unresolved state.

## Deterministic and personal-lab validation

[HTTP fixtures](../contracts/fixtures/nomad-workloads-v1/http.json), [synthetic empty response](../contracts/fixtures/nomad-workloads-v1/complete-empty.json) and [OpenAPI](../contracts/openapi/nomad-workloads-v1.openapi.json) own the exact disclosure schema/source pins. Trust/transport/projection tests use synthetic TLS; actual-owner HTTP tests use isolated kordra_test facts and the normal broker/admission/audit classes. They cover wrong tenant/scope/permission, revocation during provider read, freshness expiry during D, missing/unknown C audit, unknown helper outcome, query overrides, TLS/destination/protocol/size failures, duplicate JSON and secret canaries. No independent AI review is run locally; PR review remains Codex Auto PR Review.

The separately recorded personal case T09-NOMAD-LOCAL-002 uses only the [new local Nomad lab](local-nomad-lab.md). Its preflight binds the owner, exact VM/TLS/namespace identity and new read-only token scope. Set KORDRA_NOMAD_PERSONAL_LAB_CASE=T09-NOMAD-LOCAL-002 only after that exact recorded authorization/preflight; it is not a general infrastructure permission flag. The optional Mac operational test copies only that newly created lab token to a new UUID Keychain, binds synthetic test-owner facts, verifies actual owner HTTPS → U → native Keychain child → C → live Nomad → D → response, then deletes the store and synthetic rows. The existing private token file remains owner-managed; no permanent installation binding or company access is activated.

Normal CI has no personal VM/credential and reports this optional operational case skipped; it does not substitute a fake live result. Local full validation explicitly enables the case and native isolated Keychain fixture, using privately loaded test locators. Product deterministic tests remain independently executable without model/provider credentials. Private evidence is under .amr-local/t09/implementation/ and .amr-local/t09/fixtures/; no token or private key is published.

## Failure and rollback

403 means current authority/configuration did not allow the target; 503/empty preheader abort means unavailable or an unresolved protected operation. A partial 200 is not a complete inventory. Diagnose only sanitized stage/status/evidence; never paste provider errors or tokens. Check active binding/revision/expiry, trust file hash, namespace CreateIndex, certificate pin/time and provider ACL while preserving custody. Do not expand read capabilities or disable verification to bypass a failure.

Disable Nomad registration by removing the entire optional configuration group under stopped-API custody and restart. Preserve SEC/AUD history and accepted Kubernetes design. Stop/delete only the named personal VM if separately intended; do not stop Website/Docker/PostgreSQL or delete unrelated Keychains. Removing the endpoint does not revoke the provider token; manage its expiry/revocation deliberately in the owned lab/provider.
