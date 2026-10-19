---
id: PROD-OPERATOR-PILOT
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Private-first Operator Pilot → OSS Beta reuse plan

> **Proposed product sequencing, not authorization to modify employer infrastructure.** Internal usage requires company consent, clean intellectual-property boundaries, approved credentials/data handling and separate, scoped change controls. Public repository documents the generic capability; private environment profiles/evidence are not committed. No claim of company deployment or successful pilot.

## Why this sequence?

A solo developer can use the same **generic Kordra release/deploy architecture** to reduce repetitive manual operations in an existing authorized environment, while preserving time and code budget for the public OSS product. Reuse generic domain/driver contracts; **never extract or publish employer-owned code, credentials or proprietary runbooks without permission**. A private pilot is not an OSS release or production-readiness certification.

## Private operator pilot increments (outcome-driven)

| Increment | Priority | User-visible outcome | Scope/acceptance | Safety boundary |
|---|---|---|---|---|
| **OP-P0 Read-only workstation** | First | macOS selects profile, verifies connection and displays environment/cluster/workload/registry read-only health; distinguishes network failure from target failure | Profile ID and trusted target identity; read-only capability, no credentials in logs; source/running version comparison including digest/checksum or Unknown | LAB-01 observe-only; use approved read credentials only |
| **OP-P1 Controlled release path** | Next | Version candidate → pending queue → frozen approval → deploy → actual verify → operation history → authorized rollback | One provider path on **disposable or explicitly approved isolated target**; timeout, duplicate, crash/restart and unknown outcome tests | No shared company production writes without explicit approvals |
| **OP-P2 Backup readiness** | After P1 | View last DB/config backup; perform a scoped **test** DB backup and restore rehearsal; show archive hash, retention and storage copies | Proven checksum + sandbox restore test + failure evidence; config vs data vs VM snapshots labeled separately | Production restore prohibited absent separate signed-off procedure |
| **OP-P3 Safe operational adjustments** | After hardening | HPA/replica plan/dry-run, validation of PDB/probes/storage/metrics; eventual guarded apply | Canary safety scenario and impact analysis; managed controllers' ownership respected | Proxmox/Talos worker create/join and HA redesign are later high-risk integrations |
| **OP-P4 Extended automation** | Demand-led | Visual workflow editor, advanced schedules, provider plugins, broader DB/broker/VM management | Real pilot user demand and accepted ADR/ownership | Must never bypass tenant/resource scope or employer data rights |

**Optional early quick win:** read-only backup history and broker/DB health may appear in P0 without a backup engine. Run-time backup/write capability stays P2 or later. Do not equate a UI button or successful shell exit with safe production operation.

## First OP-P0 target — owner preference recorded 2026-10-18

The owner selected **Kubernetes/Helm read-only** for the first pilot design. Start with bounded Kubernetes inventory/health and explicitly reviewed Helm release metadata where safely available. This is a design-scope preference, not a shipped adapter, RBAC grant, cluster credential, accepted SDK/storage/host choice or deploy permission. Exact resources, fields and operations require the inventory/security owner review; Helm data must not imply blanket access to Kubernetes Secrets, release values or manifests. Missing authorized evidence remains Unknown. Keep read collection separate from later install/upgrade/rollback capabilities.

The owner also selected **API local on Mac first** on 2026-10-18: read availability depends on the Mac/API running. This is a design preference; later long-running workflows still require independent hosted execution. The maintainer explicitly accepted [scoped bootstrap ADR-0010](../decisions/0010-local-readonly-bootstrap.md) on 2026-10-18. Bootstrap source is implemented and locally validated (67 product tests plus independent native High review); clean GitHub CI for this unpublished increment is still pending. Protected/real inventory reads remain outside its scope.

The owner selected **Kordra-managed local accounts** for the login direction; [FEAT-IAM-001/I0](../features/identity-access/features/authorize-resource.md) records proposed trust/authorization readiness. Authentication implementation, session/credential storage, target credential provider, trust bootstrap, first write-capable OP-P1/OSS Beta provider and later hosted execution choices remain unresolved. No company endpoint, kubeconfig or operational access was requested or used to record this preference.

## Architecture to avoid overbuilding

- **Operator desktop:** Start with the **smallest usable Vue web dashboard on a Mac browser** if that saves solo-dev time; package with Tauri later if native Keychain/desktop integration is demonstrably needed. Either way, it is a client, not the executor or authority. Local-only read commands need explicit credentials and trusted target identity; they cannot claim durable remote jobs.
- **Shared Kordra Core:** ReleaseManagement, ApprovalGovernance, ProjectEnvironment, ArtifactManagement, DeploymentManagement, WorkflowOrchestration, authorization/audit, driver contracts. These are candidate reuse boundaries in the proposed design, **not an implemented reusable Control Plane**; the current local source is limited to the artifact digest increment and ADR-0010 deny-only API/inventory bootstrap.
- **Durable execution:** an independently hosted API/worker (or well-defined provider-owned remote Job with reliable status recovery) is needed for operations to continue after Mac closes. If service unreachable, UI must show `Unknown/Disconnected` and reconcile; never auto replay.
- **Adapters:** Reuse existing Helm/K8s provider operations, CI registry metadata and safe read-only Prometheus/Loki APIs; use a typed Docker Compose/SSH driver or provider-native DB backup *only* after approval. **No arbitrary shell script executor**; reuse proven provider primitives and restrict accepted operations/parameters to save engineering effort. Provider-specific implementation only after accepted design/security decision.
- **Private configuration:** any real host address, internal repo, tenant name, routing, backup destination, credential and customer workload mapping remains outside public OSS; use secret references and a rights-approved private store.

## Minimal version comparison contract


a. `sourceRef` (trusted CI commit/artifact), `artifactType` (`OCI`/`static-files`), `artifactIdentity` (registry/repository@digest or checksum), `targetRef` (trusted identity), `deployedIdentity`, `capturedAt` and `observedStatus`.

b. Display `NewCandidate / SameArtifact / Unknown / DriftDetected` when justified by trusted evidence. Do not claim candidate is newer from timestamps alone. A FE snapshot requires hash and provenance, not just source folder path.

c. Every approval binds the exact artifact, deployment settings/workflow revision and target UID. New file/image discovered while queued creates a *new* request, never overwrites the approved one.

## Verification + failure tests before operator rollout

1. Wrong cluster/context/namespace or wrong tenant cannot be used; permission-denied and MFA/expiry are handled without bypass.
2. Offline Mac vs unhealthy server differentiated; Mac restart shows **current remote operation**, not a duplicate run.
3. Timeout/ACK loss/duplicate click cannot trigger a second deployment without reconciliation/operation idempotency.
4. Bad artifact, rollout partial failure, verification fails, rollback conflicts, DB migration not safely reversible: manual recovery and audit are available.
5. Registry mutable tag updated between detection and approval: approved artifact remains pinned.
6. Backup error, incomplete upload, failed checksum, restore rehearsal failure remain separate negative states.
7. HPA scaling is workload Pod scaling; Talos/Proxmox worker provisioning requires distinct procedure and blast-radius review.
8. Operation evidence uses redaction and does not expose internal company topology in public CI logs/PRs.

## Relationship to the OSS roadmap

- **Private pilot is an earlier validation channel**, not a commitment to ship every operations feature in OSS Beta.
- OSS Beta release gate remains **one safe end-to-end release/deploy/verify/rollback slice**, independently installable on a disposable environment. See [Beta contract](oss-beta-acceptance.md).
- Database backup, HPA mutations, Proxmox worker automation and full workflow editing are **proposed later additions**, regardless of their attractiveness for one existing organization.
- Capabilities which require company-private logic, employer IP or high-risk cloud access must remain **private integrations with explicit rights**, not silently generalized into a public driver.
- Avoid mandatory AMR multi-agent orchestration; retain deterministic tests and a single-agent engineering workflow to control context/usage overhead.

## Explicit owner decisions

- `[NEEDS-DECISION]` Company authorization for any pilot use, operational writes, credential access and permission to reuse generic engineering work in OSS.
- `[NEEDS-DECISION]` Supported first deploy target, hosted worker/agent requirement and location of durable state.
- `[NEEDS-DECISION]` Backup domain ownership and restore provider(s). No new module before accepted ADR.
- `[NEEDS-DECISION]` Security policy for locally stored credentials, remote authorization and audit retention.

References: [Operator requirements](operator-workstation.md) · [Technology A–F](technology-landscape.md) · [Current product scope](scope.md) · [Feature owners](../features/README.md).
