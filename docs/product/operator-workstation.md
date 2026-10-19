---
id: PROD-OPERATOR-WORKSTATION
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# KORDRA — macOS Operator Workstation & multi-environment operations

> **Design proposal, NOT a shipped feature or a license to operate production.** This is a generic product specification inspired by the need for a Mac-based operator console before OSS Beta. Company-specific addresses, workload identifiers, broker details, credentials, security findings and actual runbooks belong outside public GitHub and require rights-owner approval. [Private pilot plan](operator-pilot-plan.md) defines non-production validation.

## 1. User outcome, product boundaries and deployment model

The first client can be a lightweight Vue web dashboard used on a Mac; native Tauri wrapping is optional, not an OSS Beta prerequisite. An authorized engineer on a Mac can choose the correct organization/environment/target; inspect target health and source-to-running version differences; request a pinned release; see a review queue; run an approved, idempotent deployment; verify outcome; inspect audit; and recover safely. **Mac UI is a client, not the system of record or background worker.**

```text
MacBook — Tauri/Vue Client (candidate, not implemented)
    │  API/SignalR over authenticated TLS
    ▼
Kordra API + durable ControlWorker (on independently running server)
    │  permission, approval, spec hash, idempotency, history, audit
    ├── Registry/CI adapters (read-only first)
    ├── Kubernetes/Helm driver or approved remote execution service
    ├── Docker Compose/SSH driver (isolated target first)
    └── Provider-native Jobs/Backup/Monitoring APIs
                   │
                   ▼
       Sandbox target / approved infrastructure
```

If there is *only* a local Mac process, closing the laptop **stops local scans and jobs**. A Kubernetes Job or other provider-owned remote task may continue independently, but continued orchestration/recovery and correct operator history require a reachable durable backend. Never claim Mac sleep/close survivability without actual process and restart tests.

## 2. Functional inventory — 10 groups

| Code | Group | Required behavior / data | Candidate owning context(s) | Private pilot priority | Later scope |
|---|---|---|---|---|---|
| OP-01 | **Environment & connection profiles** | Company/project, dev/staging/prod, source/target system, registry, Git/CI, Kubernetes context, SSH and credential reference; active target shown constantly | ProjectEnvironment, ConnectionSecrets, InfrastructureInventory | **P0-read** | Team-managed profiles, secrets rotation |
| OP-02 | **System overview** | Nodes, Pods, services, workloads, PVC, CPU/RAM/disk, HPA, brokers/DB health, last backup *when discoverable*; differentiate connection timeout vs observed outage | InfrastructureInventory, Observability | **P0-read** | Unified incident correlation |
| OP-03 | **Version discovery** | Compare running FE/API/worker build and source candidate, source commit, snapshot checksum, registry/repository@digest; report unknown identity | ArtifactManagement, ReleaseManagement, CIIntegrations | **P0-read** | Provider webhooks and provenance |
| OP-04 | **Release queue & approval** | Pending/rejected/approved requests, proposed diff, pinned artifact/spec, explicit reviewer, expiry, target scope and version change protection | ReleaseManagement, ApprovalGovernance, Messaging | **P1-write in sandbox** | More policy, SoD, schedules |
| OP-05 | **Deployment workflows** | Versioned plan: preflight → stage artifact → optional safe backup → deploy → wait Ready → HTTP/checksum verification → success or recovery; bounded retries/timeouts and human pause | WorkflowOrchestration, DeploymentManagement | **P1 minimal template** | Drag-drop editor, branching DSL |
| OP-06 | **Deployment management** | Helm/Kubernetes first target candidate; Compose/SSH when approved; streamed step status, redacted logs, dedupe/lease, history, rollback | DeploymentManagement, DeploymentDrivers, ExecutionAgents | **P1 isolated** | Wider provider support |
| OP-07 | **HA/HPA & capacity** | View replica/HPA/PDB/probes/placement, validate scaling ownership and capacity; distinguish VM workers from app replicas | InfrastructureInventory, Observability, DeploymentManagement | **P0-read** | **P3 controlled writes** |
| OP-08 | **Database/data & configuration backup** | Separate DB backup, configuration restore point, VM snapshot; checksum, encryption decision, retention, S3/Drive copy, restore verification | **[NEEDS-DECISION]** backup data owner; WorkflowOrchestration for governed job, provider adapter for execution | **P2 sandbox backup** | Scheduled/HA restore ops |
| OP-09 | **Operations diagnostics** | Service logs, metrics, Kubernetes events, Grafana/Loki/Prometheus deep links; operation correlation without a replacement monitoring backend | Observability, InfrastructureInventory | **P0-read** | Alerts and SLO trends |
| OP-10 | **Secrets, permission & audit** | Credential refs and platform/local key store, scoped permission, dry-run, warning/confirmation for risk, actor/target/spec history, redacted output | IdentityAccess, ConnectionSecrets, AuditCompliance | **P0 invariants** | SSO/policy engine/compliance |

**Priority** is for a *private operator pilot*; it does **not** override the [OSS Beta roadmap](scope.md), turn the above into implemented product features, or authorize access to shared resources. Each implemented capability must be registered under an existing owning feature or an explicitly accepted ADR; **do not create a new Backup module by inference**.

### Existing feature seams (candidate mapping, not implementation scope)

| Requirements | Existing feature candidates | Boundary before implementation |
|---|---|---|
| OP-01/02 profiles and inventory | FEAT-PRJ-001, FEAT-INV-001, FEAT-SEC-001, FEAT-OBS-001 | Trusted target/tenant identity and credential references; no unapproved write capability. |
| OP-03 version discovery | FEAT-ART-001, FEAT-REL-001, FEAT-CI-001 | Only ART/I1 digest validation exists; registry/source/running comparisons remain proposed. |
| OP-04/05/06 controlled execution | FEAT-REL-001, FEAT-APR-001, FEAT-WFL-001, FEAT-MSG-001, FEAT-DEP-001, FEAT-DRV-001, FEAT-AGT-001 | Each task needs one primary feature owner and reviewed consumer contracts; no new project or runtime state machine is adopted here. |
| OP-07/09 capacity and diagnostics | FEAT-INV-001, FEAT-OBS-001; FEAT-DEP-001 only for an approved change scope | Existing deploy/rollback specs do not implicitly authorize HPA/VM administration; extend the owning feature or decide ownership first. |
| OP-08 backup and restore | Domain owner **[NEEDS-DECISION]**; FEAT-WFL-001 may coordinate an approved provider job | Workflow orchestration does not own backup data, retention or restore authorization by inference. |
| OP-10 identity, secrets and audit | FEAT-IAM-001, FEAT-SEC-001, FEAT-AUD-001 | Permissions, scope, trusted credentials and audit remain mandatory, including in a solo pilot. |

Resolve these identifiers through the existing [20-module feature registry](../features/README.md). OP requirement IDs are not new registered features or evidence of source implementation; update the selected owning feature before coding.

## 3. OP-01 profile and OP-02 system overview

- Separate **organization, project, environment and target** IDs. Profile names are labels, not authorization. Fetch actual identity/namespace/cluster UID before showing an operation as runnable.
- Distinguish connection results: `Reachable / AuthDenied / TimedOut / NotConfigured / ObservedUnhealthy / Unknown`. An unreachable laptop network is **not** proof that the server or workload is down.
- Read-only inspection by default. A credential stored locally in macOS Keychain is **not** automatically available to server workers; remote execution requires a separate authorized secret reference/trust path. Kubernetes Secret is not a general replacement for a desktop credential vault.
- Show explicit production indicators and a target identity summary before each risky action. Never trust a user-provided `tenantId` or cluster alias as permission evidence.

## 4. OP-03 release discovery and OP-04 approval queue

```text
read source / registry / running deployment
→ compare OCI digest or independently validated checksum
→ candidate (version unknown is allowed as UNKNOWN)
→ release intent (immutable target + source + digest + workflow version)
→ requested approval → approve/reject (policy, expiry, separation of duties)
→ recheck rights + immutable spec at dispatch
```

- An image tag such as `latest`, a mutable branch, a local file timestamp, and a dashboard display name are **not authoritative release identities**.
- If source changes while request waits, create a **new** release candidate and approval. Never rewrite the already approved spec.
- FE file/snapshot packages require checksum, origin, deploy directory/target and artifact version, with safe promotion/rollback handling.
- Queue is an *operations queue* (approval, deployment, backup, scheduled verification), not the customer's RabbitMQ business queue.

## 5. OP-05/06 deployment workflow and state contract

**Editable plan, but immutable execution revision**. A workflow definition changes by version; a deployment run stores its resolved immutable `workflowVersion`, ordered `stepIds`, `artifactIdentity`, `targetIdentity`, config hash, approver/policy and correlation/operation IDs. A WYSIWYG editor is **not** required for first pilot.

The lifecycle below illustrates intended behavior only. It does not adopt a new runtime state machine or supersede the owning feature and contracts; acceptance and executable transition/failure tests are required before implementation.

```text
Draft → PendingApproval → Approved → Queued → Preflight
      → Executing → Verifying → Succeeded
                                 └→ Failed / UnknownOutcome
Failed/UnknownOutcome → Inspected → AuthorizedRollback/ManualRecovery
                              → VerifiedRecovered or Escalated
```

- Reject double-dispatch with unique `operationId` and target conflict guard/lease. A timeout/agent disconnect yields `UnknownOutcome` until the real target is inspected; **do not automatically rerun a side effect**.
- Preflight: trusted target UID/context, namespace, release name, effective capability/permission, digest/checksum, rollout ownership (GitOps/HPA), resource/storage/metrics prerequisites, active conflicts, provider-specific backup requirements.
- Post-deploy: observed readiness, HTTP contract, checksum or probes with finite timeout. Kubernetes **Ready** is not sufficient if the application-level health gate fails.
- For managed production resources, no write, backup/restore, credential rotation or load-affecting experiment until a separately authorized operational change request exists. LAB-01 remains observe-only by default.
- Rollback is an **independent** request with actor/permission/target/previous artifact/steps and preflight; it does not automatically revert database migrations or PV data.

## 6. OP-07: three different capacity functions

| Distinct scope | Behavior | Mandatory caution |
|---|---|---|
| **Kubernetes HPA** | Scale application Pod replicas (min/max, CPU target, behavior and stabilization). | Do not change worker VM count. Avoid writing a fixed replica count against an active HPA. |
| **Workload HA** | Replica distribution/anti-affinity, PDB, graceful shutdown, readiness, storage topology and component-specific quorum. | `replicas > 1` is **not** proof of HA. RabbitMQ/CNPG/cache/SignalR each have distinct requirements. |
| **Cluster worker management** | Provision VM, Talos machine config, join node, verify capacity and failure domains. | Proxmox management is separate high-risk infrastructure automation; several VMs on one physical host are not host-level HA. |

Initially **observe**, then propose **dry-run/plan**, only later allow scoped writes with capacity/risk checks and approved rollback plans.

## 7. OP-08: backup and restore taxonomy (do not conflate)

| Backup type | What it contains | What it does **not** guarantee |
|---|---|---|
| **Database/data backup** | Consistent provider-native DB backup/archive, timestamp, source identity, checksum, retention, encryption policy, storage copies | Restore works until tested; database rollback is not application rollback |
| **Configuration recovery point** | Helm chart/version/values, manifests, image digests, Talos/config references, service/routes and restore order | Exported YAML is not DB/PVC/application data backup |
| **VM or disk snapshot** | Provider-specific VM/disk state under a verified snapshot/consistency procedure | Logical DB consistency without quiesce/backup-aware tooling |

For database backup: `Select approved environment/database → Preflight rights/storage/consistency → Confirm destination + encryption + retention → Run provider-native job → checksum → record success → optional second storage copy → **restore rehearsal on isolated test target**`.

- `BackupCreated` vs `BackupVerifiedRestore` are **different states**. A checksum proves file integrity, not recoverability.
- Explicit S3-compatible/Drive destination, access policy, key management, failure retry and data-residency review; do not assume every provider supports direct Drive uploads.
- Never restore over production without higher-impact authorization and an approved downtime/data-loss plan.
- The owner of backup domain data and retention across providers remains `[NEEDS-DECISION]`, requiring architecture review. First private pilot may **observe existing provider-native backup jobs** and only use authorized isolated/test backup execution.

## 8. OP-09/10: diagnostic safety and evidence

- Link known release/deployment IDs to external Prometheus/Loki/Grafana; don't build new metrics/log stores.
- Store least-privilege credential references, not plaintext kubeconfig/tokens in docs/event payloads or execution logs.
- Every write operation captures: `actorRef`, `resourceRef`, `environmentId`, `targetId`, `operationId`, `correlationId`, approved spec hash, preflight result, step outcome, observed after-state and reviewer evidence.
- Make risk flags visible: `observe-only`, `pending-approval`, `destructive/high-impact`, `unknown-outcome`. No automatic green status on missing telemetry.

## 9. Two queues, two owners

1. **Kordra operation/approval queue:** durable job state with target scoping, idempotency, concurrency control, retries, dead-letter/unknown-outcome path and audit. This is product-owned.
2. **Customer broker queues/topics:** external RabbitMQ/VerneMQ metrics including ready, unacked, consumers and rates are *observations*. Purge, delete, requeue, consume/ACK and publish device topics are separate denied-by-default capabilities, out of initial pilot.

## 10. Crosswalk to A–F landscape and formal decisions

- **A:** OP-03 source discovery/CI metadata/artifact identity (groups 1–6).
- **B:** OP-04/05/06 release, packaging, approval, deploy and rollback (7–12).
- **C:** OP-01/02/07 infrastructure inventory/OS/scaling targets (13–20).
- **D:** OP-02/08/09 metrics, messaging, DB, storage, HA, backup and alerts (21–28).
- **E:** OP-01/04/08/10 IAM, secrets, policies, audit/trust (29–35).
- **F:** OP-04/05/06/10 workflow, agent, state, inventory, governance and driver contracts (36–43).

See [Technology Landscape A–F](technology-landscape.md), [pilot plan](operator-pilot-plan.md), [Beta acceptance](oss-beta-acceptance.md), [architecture](../architecture/README.md), [permissions](../contracts/permissions.md) and [LAB-01 safety](../reference-environments/lab-01/experiments/safety-and-change-control.md).
