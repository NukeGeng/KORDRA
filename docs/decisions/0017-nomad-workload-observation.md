---
id: ADR-0017
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-24
---

# ADR-0017 — Nomad workload observation, T09.2

## Context and authority

The owner reports that the company is moving from Kubernetes to Nomad and has no company credential/network access available. The owner authorized a new personal lab with 1 vCPU, 2 GiB memory and 15 GiB disk and requested T09 continuation. That operational setup authorization is distinct from acceptance of this Product provider contract.

[ADR-0016](0016-kubernetes-workload-observation.md) remains accepted for Kubernetes T09.1. Its Namespace UID, paths and projection cannot be relabeled as Nomad. Prioritize accepted T09.2 Nomad while retaining T09.1 as an approved deferred adapter. Keep accepted IAM/SEC/AUD ownership and existing metadata behavior.

## Alternatives and accepted decision

- Continue Kubernetes first: permitted by ADR-0016, but less aligned with the newly reported operational need.
- Reuse the Kubernetes contract unchanged: rejected; identity, ACLs, pagination and output semantics differ.
- Add a bounded Nomad read consumer in existing INV/Host projects: selected decision. No SDK, CLI subprocess, new project, persistence table, worker, generic provider framework or production deployment engine.

Implement [Nomad workloads v1](../contracts/nomad-workloads-v1.md): one explicitly configured namespace/region per target; bounded job and allocation summaries; separate opt-in HTTP route; private immutable trust/configuration; existing target.read, SEC credential borrowing and U/C/D. Preserve independent config digest, exact trusted scope and private disclosure expiry as already designed in ADR-0016, implementing the compatible additions once.

Nomad identity uses exact TLS origin/private CA/server key pin plus configured namespace Name and CreateIndex, checked before and after reads. A namespace name alone, region or returned index is not a global cluster identity. Require dedicated trust keys per installation and re-enrollment after a restore/rebuild; an administrator reusing a key and namespace index can defeat this identity assumption. The owner explicitly accepted this bounded continuity residual with the exact design on 2026-10-24.

Extend the managed-header exception only to the registered T09.2 post-C transport. Use temporary per-request X-Nomad-Token; never default headers, logs, DTOs or retained fields. Keep Keychain IPC raw bytes and zero owned mutable copies; managed/framework copies are not perfectly erasable. Retain private-PKI offline validation with no online revocation/downloads, mandatory hostname/pin/validity checks and stopped-API trust maintenance.

Production destinations retain strict explicit IP allowlisting and disallow loopback/link-local/metadata addresses. Add a separately explicit personal-loopback trust mode only for an owner-approved local installation: exact 127.0.0.1 address and configured HTTPS port, CA and pin, no DNS/redirect/proxy/fallback. This is not a browser flag or a general TLS/SSRF bypass. Direct lab access is not Product conformance evidence.

## Delivery and acceptance

1. T09.2a: strict Nomad trust parser, canonical vectors and compatible admission configuration/scope/freshness additions.
2. T09.2b: bounded INV transport/projection against synthetic TLS and N09 acceptance failures.
3. T09.2c: registered Host consumer, workloads endpoint and actual-owner U/C/D interleavings.
4. T09.2d: fixtures/OpenAPI/runbook, source mappings, required deterministic gates, feature PR and normal Codex Auto PR Review.
5. T09.2e: separately recorded integration against this personal lab only, using its new read-only credential through an explicitly provisioned Keychain binding. No company access follows from any of these slices.

## Approval, risks and rollback

NukeGeng accepted ADR-0017 with the exact Nomad contract for T09.2a–d on 2026-10-24: “Duyệt ADR-0017 và contract đúng phạm vi T09.2a–d”. The accepted scope includes namespace CreateIndex limitations, the Nomad-specific managed-header supplement, offline PKI and personal-loopback mode. Personal lab creation is already authorized and does not require this Product decision. Existing Keychain items/company infrastructure remain excluded. T09.2e needs its exact new lab binding/test scope recorded before use; no secret auto-import.

Rollback disables only Nomad route registration and preserves metadata, accepted Kubernetes design, authority history and checkpoints. Stopping/deleting the named personal VM is a separate owner operation; no unrelated service/container cleanup. Do not declare T09/OP-P0 or production readiness complete from fixtures or lab API probes.

## Exact-content acceptance evidence — 2026-10-24

Pre-annotation approved SHA-256: ADR `0df28f98b4fe4905230e04f5befb1166390ce9731f80f3ef2ab5679594eb8608`; contract `0274fadbdb245223635507c12c63100f4e57d82aca9e4dfb1b2be634eb6d96a6`. Original bytes, hashes and the owner decision are preserved privately in `.amr-local/approvals/adr-0017-t09/acceptance.json`. Only status/acceptance annotations were added after approval. T09.2a–d may proceed without repeat design approval or an additional Sol review. Runtime implementation and source-bound Product validation remain pending; lab setup evidence is separate.
