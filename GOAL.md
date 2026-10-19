# GOAL — KORDRA DevOps Control Plane

> Documentation Sync v1.5 proposes product sequencing; importing these documents does not accept a release scope, architecture ADR, technology dependency or operational permission. The current implementation remains Foundation.

## Product vision
Build a trustworthy, infrastructure-agnostic **release and deployment control plane** for teams already using Git/CI, artifact registries and runtimes such as Helm/Kubernetes and Docker Compose/SSH. Kordra manages release intent, permission/approval, deployment execution, health verification, rollback and audit; it **does not replace** the Kubernetes Control Plane, Jenkins or Argo CD.

**Positioning:** *One Control Plane. Any Infrastructure.* This is product intent, not a claim that all providers are supported today.

## Product outcomes (existing GOAL references)
1. Một tổ chức tạo Project → Application → Environment → Target với ranh giới tenant rõ ràng.
2. Từ artifact immutable (`digest`/hash), người được cấp quyền tạo release và yêu cầu deploy.
3. Policy/approval kiểm soát mọi thay đổi production; phiên bản được duyệt không bị tráo sau phê duyệt.
4. Workflow bền vững, xử lý crash/retry/timeout, phát lệnh tới driver phù hợp, xác minh observed state.
5. Giám sát trạng thái, audit, phát hiện drift và phục hồi theo policy của target.
6. Mở rộng driver mới không sửa lõi domain ngoài contract và khả năng đã xác nhận.

These outcome numbers retain the existing feature/traceability references; the strategy stages below are separate planning labels.

## Product strategy (proposed)
1. **Development / Foundation:** prove a small immutable artifact domain, architecture/test governance and safe source structure.
2. **OSS Beta:** independently installable, tested sandbox flow: configure project/target → pin artifact → authorize/approve release → deploy → independently verify → rollback/audit. Implement one target driver fully, then a second with contract parity.
3. **Production-ready:** harden authentication/tenant isolation, durable recovery, least privilege, upgrade/backup, reliability evidence and operational documentation; label only after validation.
4. **Enterprise:** optional governance, SSO/compliance/managed support driven by user demand.
5. **Cloud:** managed multi-tenant control plane only after security, operational and commercial economics are proven.

## Non-negotiable safety principles
- Immutably bind approved artifact digest, target and deployment spec; no silent mutable tag substitution.
- Permission + resource scope; do not hardcode role names or grant platform admin implicit tenant access.
- Idempotent logical operations, durable state, explicit unknown-outcome reconciliation; ACK/command exit is not verified deployment success.
- Single-writer boundaries against an existing GitOps reconciler or controller.
- LAB-01 shared environment observe-only by default; tests with writes use approved isolated sandbox.
- Keep AMR optional tooling, separate from actual product runtime.

## Early private Operator Pilot (new priority, not shipped)
Before public OSS Beta, prioritize a **Mac-first operational console** that can reduce repetitive work on authorized infrastructure: environment profiles, read-only inventory and version discovery, review queue, controlled deploy → verify → audit/rollback. Keep existing safety invariants; shared LAB-01 stays observe-only unless separately authorized. Database/config backup and HPA/HA mutations are subsequent capabilities, **not** permission to operate company production. Long-running tasks require a worker/provider job independent of Mac sleep or app closure.

This pilot is a **validation channel for reusable Kordra Core**, not a separate employer-specific product or proof of Beta readiness. See [Operator Workstation requirements](docs/product/operator-workstation.md) and [Pilot sequencing](docs/product/operator-pilot-plan.md).

## OSS Beta acceptance (proposed)
The release must satisfy the executable tests and checklist in [OSS Beta acceptance](docs/product/oss-beta-acceptance.md). Feature completion must be backed by source, tests, review and reproducible evidence; published architecture specs alone do not count.

## Exclusions from first Beta
No new CI engine, Kubernetes scheduler, broad infrastructure automation, cloud SaaS billing, plugin marketplace, enterprise compliance suite, automatic database schema rollback, or production self-upgrade supervisor.

## Current verified product status
At `main@2aa156f4e6e3afe3cc648186ac6a3242781dc71b` (2026-10-17), the only product C# increment is `OCIImageDigest` and its tests. PR #1 is merged with passing GitHub docs/product checks. This does **not** establish a deployable API, backend, messaging topology, agent or complete artifact-registration feature. ADR-0008 is accepted narrowly; ADR-0009 remains proposed. This paragraph is a dated checkpoint, not a live status API.

Detailed milestone progress and dependencies remain in [product roadmap](docs/product/roadmap.md). The [A–F technology landscape (43 groups)](docs/product/technology-landscape.md) is a planning reference, **not** an implementation mandate; critical contracts remain governed by accepted decisions and feature specs.
