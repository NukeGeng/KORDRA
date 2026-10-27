---
id: CTR-INDEX
status: proposed
owner: unassigned
last_reviewed: 2026-10-25
---

# Public contracts and compatibility

## Delivery contracts and current adoption status

- [Local authentication v1](local-authentication-v1.md) — accepted for Foundation T01–T07; FEAT-IAM-002 browser/session contract and numeric bounds.
- [Authority/admission v1](authority-admission-v1.md) — accepted for Foundation T01–T07; owner facts, management DTOs, single-process ordering and AUD intent/idempotency.
- [Resource Policy v1](resource-policy-v1.md) — accepted exact T06 supplement under ADR-0014 on 2026-10-22; scoped local implementation is mapped by IAM/PRJ owner specs, with [HTTP fixtures](fixtures/resource-policy-v1.http.json), [source-bound OpenAPI snapshot](openapi/resource-policy-v1.openapi.json) and [runbook](../operations/resource-policy-v1.md). Local tests, hosted review and real configuration/provisioning remain separate evidence.
- [Credential boundary v1](credential-boundary-v1.md) — accepted exact T08 local Mac Keychain/SEC metadata and U/C/D supplement under ADR-0015 on 2026-10-23; isolated synthetic fixture separately authorized; local T08 implementation and source-bound verification are recorded in [FEAT-SEC-001](../features/connection-secrets/features/secret-binding.md#t08-local-implementation--2026-10-24).
- [Kubernetes workloads v1](kubernetes-workloads-v1.md) — accepted T09.1a–d under ADR-0016; implementation from #17 reached main b70def1 through #18 on 2026-10-25. Live Kubernetes T09.1e remains unverified and independently gated.
- [Nomad workloads v1](nomad-workloads-v1.md) — accepted T09.2a–d under ADR-0017, implemented on main through #16; the separate personal-lab proof does not authorize another registry/target.
- [Artifact registration v1](artifact-registration-v1.md) — accepted T10.1a–f under ADR-0018: project-scoped digest-first catalog, bounded OCI/Docker manifest/config checks, typed SEC seam and transactional receipts. Implemented and merged through PR #19 at main bc91c2a; exact reviewed head 5969786 and current evidence are recorded in traceability. Live registry qualification remains separate.
- Artifact registration [HTTP fixture](fixtures/artifact-registration-v1.http.json), [source-bound OpenAPI](openapi/artifact-registration-v1.openapi.json) and [local runbook](../operations/artifact-registration-v1.md) describe the implemented T10 surface; live provider readiness is not implied.
- [Client API handoff v1](client-api-handoff-v1.md) — proposed independently owned clients integration, schemas and UI states.
- [Release and approval v1](release-approval-v1.md) — proposed exact T11.0–T11.3 release-intent policy, immutable revisions and scoped queue/decisions; ADR-0005 acceptance pending, no execution authority.
- [Release/execution v1](release-execution-v1.md) — future execution-plan/durable delivery/worker/verification proposal; not a T11 worker grant.

The delivery design and ADR-0012 distinguish accepted runtime contracts from proposed future contracts. Acceptance alone is not implementation; dated owner source/tests and exact PR/CI evidence supply that distinction. No contract grants permission to operate a target.

Ở đây lưu chuẩn được nhiều module dùng chung, không thay thế feature specs của owner. [Permissions](permissions.md), [Message catalog](message-catalog.md), [Message envelope](message-envelope.md), [Driver contract](driver-contract.md), [Agent protocol](agent-protocol.md), [API guidelines](api-guidelines.md), [Error codes](error-codes.md).

Mọi public API/message phải có owner, schema version, request/response/error semantics, tenant scope, idempotency, consumers và compatibility policy. Mọi thay đổi phá vỡ consumer phải có migration/rollout và ADR nếu sửa invariant.

## Scoped in-process inventory seam — ADR-0010

[FEAT-INV-001/I1](../features/infrastructure-inventory/features/register-target.md) owns `IInventoryReader` and immutable `InventoryReadResult` / `InventoryResource` in `src/Modules/InfrastructureInventory/Contracts/`. Adopted consumers are the own-module unavailable reader, API DI composition and bootstrap tests. A positive reader exists only in tests; HTTP inventory requests never invoke it and always return empty 403. There is no adopted positive HTTP inventory schema, event, provider protocol or external consumer migration. This BCL-only in-process interface is not a versioned wire contract; future HTTP/message adoption needs explicit consumer/schema/compatibility review.

## Nomad workload accepted design — 2026-10-24

[Nomad workloads v1](nomad-workloads-v1.md) is the accepted T09.2a–d contract under [ADR-0017](../decisions/0017-nomad-workload-observation.md). Personal lab setup is authorized; the exact Product design was accepted by NukeGeng on 2026-10-24; implementation remains pending. Accepted Kubernetes T09.1 is preserved.

- [Kubernetes workloads v1 OpenAPI](openapi/kubernetes-workloads-v1.openapi.json) and [HTTP fixture](fixtures/kubernetes-workloads-v1.http.json) — implemented bounded T09.1; synthetic validation, no live-cluster qualification.

## T10 exact acceptance — 2026-10-25

NukeGeng accepted ADR-0018 / Artifact registration v1 for T10.1a–f, superseding the proposal status in the earlier index entry. New project authority, ART storage and typed SEC design are approved for implementation; no runtime/source/test/live-registry evidence is inferred. T10.1g and later release/worker decisions remain gated.

## T11 exact owner acceptance — 2026-10-27

NukeGeng accepted ADR-0005, Release and approval v1 and FEAT-APR-002/REL-001/APR-001 for T11.0–T11.3. This supersedes pending-approval wording in the earlier T11 proposal, not historical evidence. Exact pre-annotation hashes and private acceptance receipt are recorded in ADR-0005. T11 runtime implementation and validation remain in progress; no deployment, real database/secret/target operation or merge is authorized by this design acceptance. The user explicitly selected gpt-6.1-terra/xhigh for T11; actual turn metadata and native model catalog confirm this profile.
