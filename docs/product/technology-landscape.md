---
id: PROD-TECH-LANDSCAPE
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# KORDRA — DevOps, CI/CD & Deployment Technology Landscape (A–F)

> **Source classification:** this is a *product-planning technology taxonomy* assembled for Kordra, **not** a claim that the CNCF defines exactly these 43 groups. Names and tool examples do not prove installation, implementation, licensing or architectural approval. The current product implementation is an OCI digest domain increment, not a deployed Control Plane.

## Product position / responsibility

Kordra is an infrastructure-agnostic, OSS-first **Release & Deployment Control Plane** above existing CI, artifact registries, Kubernetes/Helm and Docker Compose/SSH. Its initial vertical slice is **Connect authorized target → Discover/pin artifact → Create release → Approve frozen spec → Durable deploy → Verify observed state → Audit / authorized rollback**.

- **Build:** Kordra itself owns release intent, frozen approved specs, scoped policy, durable orchestration, operation state, verification, recovery and audit.
- **Integrate:** external systems keep their source-of-truth and controller ownership; Kordra invokes them through bounded adapters when explicitly supported.
- **Observe:** obtain read-only signals and correlate them to an operation; don't mistake observation privileges for mutation rights.
- **Defer:** exclude technologies with no validated user need. Examples below must not automatically create modules, runtime dependencies or provider credentials.

**Priorities:** P0 = minimum safe OSS Beta capability/invariant; P1 = early adopter integration; P2 = later operational/enterprise maturity; P3 = optional long-term extension. A mixed value such as P0/P2 means basic safety now and advanced features later. Priority **never** means a capability is already implemented.

P0 entries for alternative deployment targets are conditional on the first selected driver; they do not require both Docker and Kubernetes for the first Beta. The 43 groups remain a catalog, while [Beta acceptance](oss-beta-acceptance.md) and accepted decisions determine the actual release slice.

## A–F: full catalog (43 groups)

### A — Source Code, CI & Software Supply Chain

Source → CI → immutable artifact. Kordra consumes a proven build output and retains traceability, but does not reimplement Git, a CI engine or a registry.

| # | Technology group | Tools / standards (examples) | Function in ecosystem | Kordra approach | Priority | Boundary / initial action |
|---:|---|---|---|---|---|---|
| 1 | **Source Control Management (SCM)** | GitHub, GitLab, Bitbucket, Gitea | Repository, commit, tag, branch, source revision | Integrate | P1 | Map commit/ref to releases; Git is the source of truth |
| 2 | **Continuous Integration (CI)** | GitHub Actions, GitLab CI, Jenkins, Azure Pipelines | Build/test pipeline state and provenance | Integrate | P1 | Observe or receive authenticated CI events; not a CI runner |
| 3 | **Build & Packaging** | Docker Buildx, BuildKit, Kaniko, MSBuild, Maven, Gradle | Package tested source into deployable artifacts | Integrate/Observe | P1 | Do not rebuild artifacts during a release deployment |
| 4 | **Artifact Registry** | Docker Registry, Harbor, GHCR, Artifactory, Nexus | Image repository, digest, tags and metadata | Integrate | P0 | Bind registry + repository + digest; mutable tags cannot replace approved identity |
| 5 | **Automated Quality Testing** | xUnit, NUnit, Playwright, k6, SonarQube | Automated quality, performance and contract evidence | Integrate | P0/P1 | P0 tests for Kordra; P1 import external pipeline quality results |
| 6 | **Software Supply Chain Security** | Trivy, Syft, Cosign, Sigstore, SLSA | Vulnerability checks, SBOM, signing, provenance | Integrate | P1 | Digest validation is not signature/provenance verification; gate by policy later |

### B — Continuous Delivery, Deployment & GitOps

This is the central product wedge: govern the release, freeze the approved spec, dispatch under policy and verify the actual outcome. Direct deployment and GitOps must not create competing writers.

| # | Technology group | Tools / standards (examples) | Function in ecosystem | Kordra approach | Priority | Boundary / initial action |
|---:|---|---|---|---|---|---|
| 7 | **Release Orchestration** | Octopus Deploy, Spinnaker, Harness (references) | Release intent, promotion, change approval | Build | P0 | Kordra owns the release/deployment coordination domain |
| 8 | **Deployment Packaging** | Helm, Kustomize, Kubernetes YAML, Docker Compose | Configuration and versioned deployment resources | Integrate | P0 | Use Helm/Compose through explicit drivers; do not reinvent package managers |
| 9 | **GitOps** | Argo CD, Flux | Git-versioned desired state, pull reconciliation | Integrate | P1 | Do not imperatively mutate a GitOps-owned deployment resource |
| 10 | **Progressive Delivery** | Argo Rollouts, Flagger | Canary, blue-green, traffic promotion | Integrate | P2 | Delegate traffic/rollout mechanics to existing controllers |
| 11 | **Deployment Strategies** | Rolling, Recreate, Blue-Green, Canary | Safe change strategies and promotion | Build policy / Integrate execution | P0/P2 | P0 basic rollout; P2 advanced progressive traffic strategies |
| 12 | **Verification & Rollback** | Helm revisions, K8s rollout/probes, application health | Observed result, health and explicit recovery | Build orchestration | P0 | Command ACK does not imply success; rollback requires its own permission |

### C — Infrastructure, Container & Cloud

Infrastructure remains the target/provider system of record. Kordra integrates existing API/driver surfaces under least privilege rather than acting as an unrestricted cluster/VM/cloud administrator.

| # | Technology group | Tools / standards (examples) | Function in ecosystem | Kordra approach | Priority | Boundary / initial action |
|---:|---|---|---|---|---|---|
| 13 | **Container Runtime** | Docker Engine, containerd, CRI-O, Podman | Container execution and inspection | Integrate | P0/P1 | P0 Docker target; other runtimes when evidence supports them |
| 14 | **Container Orchestration** | Kubernetes, K3s, RKE2, OpenShift | Cluster/workload API and rollout | Integrate | P0 | Never replace kube-apiserver, scheduler or native controllers |
| 15 | **Virtualization** | Proxmox VE, VMware vSphere, KVM | VM lifecycle and hosting substrate | Observe/Integrate | P2 | Discover only when useful; no VM power/delete capability by default |
| 16 | **Infrastructure as Code (IaC)** | Terraform, OpenTofu, Pulumi, Crossplane | Provisioning and infrastructure desired state | Integrate | P2 | May govern plan/apply approval, but not build another IaC engine |
| 17 | **Configuration Management** | Ansible, Puppet, Chef, Salt | Host-level configuration automation | Integrate | P2 | Invoke only explicitly supported and authorized automation |
| 18 | **Operating System / Cluster OS** | Talos Linux, Ubuntu Server, Rocky Linux | Nodes, OS health, updates | Observe/Integrate | P1/P2 | Inventory before advanced OS reboot/upgrade operations |
| 19 | **Cloud Infrastructure** | AWS, Azure, GCP, DigitalOcean, Hetzner | Provider resources and managed clusters | Integrate | P2 | Choose providers from adopter demand, not an all-cloud mandate |
| 20 | **Networking & Traffic Management** | Nginx, Traefik, Envoy, Cilium, Istio, cert-manager | Ingress, TLS, service routing, network policy | Observe/Integrate | P1/P2 | Observe preflight/status before any policy-backed routing changes |

### D — Operations, Observability & SRE

Correlate operational signals to release/target/operation rather than recreate every metrics, logging or incident platform. Internal Kordra messaging and customer-operated brokers are distinct domains.

| # | Technology group | Tools / standards (examples) | Function in ecosystem | Kordra approach | Priority | Boundary / initial action |
|---:|---|---|---|---|---|---|
| 21 | **Metrics & Monitoring** | Prometheus, Metrics Server, VictoriaMetrics | Performance, capacity and deployment health | Observe | P0/P1 | P0 basic probes; P1 integrate external metrics for verification |
| 22 | **Logs & Tracing** | Grafana Loki, Alloy, OpenTelemetry, Jaeger, Tempo | Execution logs, tracing and failure investigation | Observe | P0/P1 | P0 redacted execution logs; P1 external logs/tracing correlation |
| 23 | **Messaging & Event Streaming** | RabbitMQ, Kafka, NATS, VerneMQ, EMQX | Internal durability vs target business broker health | Build internal / Observe external | P0/P1 | Never consume/ACK/purge customer business queues for inspection |
| 24 | **Database Operations** | PostgreSQL, MySQL, CNPG, TimescaleDB | DB health, replication, migrations | Observe | P0/P2 | P0 own persistence integrity; P2 customer DB ops/monitoring |
| 25 | **Storage & Backup** | NFS, CSI, Ceph, MinIO, Velero, pgBackRest | Volume/backup/restore readiness | Observe/Integrate | P0/P2 | P0 Kordra state backup plan; P2 external storage administration |
| 26 | **Scaling & High Availability** | HPA, KEDA, PDB, Cluster Autoscaler | Capacity and replica availability | Observe | P1 | Do not override an HPA-owned replica count |
| 27 | **Alerting & Incident Management** | Alertmanager, Grafana Alerting, PagerDuty, Opsgenie | Release-linked alerts, incident context | Integrate | P1 | Correlate alerts with release/operation and operator runbooks |
| 28 | **Reliability & Disaster Recovery** | Backup controllers, restore workflows, DR runbooks | Failure handling, recovery and continuity | Build safeguards / Integrate | P0/P2 | P0 unknown-outcome recovery; P2 HA/DR drills and operational guarantees |

### E — DevSecOps, Security & Governance

These invariants cannot be postponed until Enterprise: scoped authentication/authorization, credential isolation and audit are needed for any authorized infrastructure side effect.

| # | Technology group | Tools / standards (examples) | Function in ecosystem | Kordra approach | Priority | Boundary / initial action |
|---:|---|---|---|---|---|---|
| 29 | **Identity & Access Management** | Keycloak, OIDC, OAuth2, LDAP, SAML | Authenticate principals and integrate identity providers | Build boundary / Integrate IdP | P0 | Trust signed identity; enterprise SSO features are later |
| 30 | **Authorization & Policy** | RBAC, ABAC, OPA, Cedar, Kyverno | Permission decisions, scope, separation of duties | Build | P0 | Deny by default; role labels do not authorize; platform ≠ tenant |
| 31 | **Secrets Management** | Vault, Kubernetes Secrets, cloud secret managers | Credential references, rotation and redaction | Integrate | P0 | Avoid plaintext secret persistence and broad cluster credentials |
| 32 | **Security Scanning** | Trivy, Semgrep, Checkov, OWASP ZAP | Scanning and security quality gates | Integrate | P0/P1 | P0 Kordra security checks; P1 consume external scan findings |
| 33 | **Workload Security** | Falco, NetworkPolicy, admission controls | Workload policy, runtime threat observation | Observe/Integrate | P2 | Do not override cluster admission or policy ownership |
| 34 | **Audit & Compliance** | Audit ledger, evidence log, policy reporting | Record actor, resource, decision and outcome | Build | P0 | Audit and traceability; do not claim certification without evidence |
| 35 | **Software Identity & Trust** | mTLS, SPIFFE/SPIRE, X.509, workload identity | Trust Control Plane, agents and workers | Build boundary / Integrate | P0/P1 | Mandatory trust boundary when remote agent exists; framework choice by ADR |

### F — Platform Engineering & Control Plane Architecture

These are primarily Kordra-owned capabilities. Add one tested vertical slice at a time; the 20 documented modules are a conceptual design, not a requirement to create 20 projects or services.

| # | Technology group | Tools / standards (examples) | Function in ecosystem | Kordra approach | Priority | Boundary / initial action |
|---:|---|---|---|---|---|---|
| 36 | **Workflow Orchestration** | Wolverine Saga, Temporal (reference), state machines | Durable long-running execution and recovery | Build | P0 | Handle retries, timeout, duplicate delivery, unknown outcome |
| 37 | **Distributed Execution** | Agents, gRPC, HTTPS, mTLS | Execute in private networks with bounded authority | Build | P1 | Use only when needed; first validate isolated driver path |
| 38 | **State Reconciliation** | Kubernetes controller pattern, desired/observed state | Verification and drift detection | Build | P0/P1 | P0 deployment verification; P1 ongoing drift observation, one writer |
| 39 | **Infrastructure Inventory** | Provider APIs, discovery controllers | Target/workload/environment/capability inventory | Build / Integrate | P0 | Minimal scoped target registration; never hardcode LAB-01 data |
| 40 | **Release Governance** | Approvals, target leases, change windows, policy | Reviewable, authorized deployment intent | Build | P0 | Frozen approved spec, expiry/revocation and audit evidence |
| 41 | **Developer Platform / IDP** | Backstage, Port, developer portals | Developer self-service and golden paths | Build selective / Integrate | P2 | Not a whole IDP/portal for OSS Beta |
| 42 | **Extensibility & Integrations** | Driver SDK, typed capability contracts, webhooks | Providers/driver evolution without changing domain | Build | P0/P1 | P0 typed driver boundary; P1 extension SDK/marketplace |
| 43 | **Platform Lifecycle** | Independent supervisor, health verifier, recovery CLI | Upgrade and recover Kordra itself | Design safeguards | P3 | Avoid self-upgrade circular dependency; independent recovery owner |

## Mac-first Operator Pilot crosswalk (priority overlay)

The complete 43 groups above remain the **long-term landscape**, not a demand to implement them. The [Operator Workstation spec](operator-workstation.md) maps the near-term private pilot to ten practical capabilities: profiles, system overview, version discovery, release approval queue, workflow steps, deployment history, HA/HPA read-only, backup readiness, diagnostics and secrets/audit. [Operator pilot sequencing](operator-pilot-plan.md) separates **read-only P0**, **sandbox deploy P1**, **backup rehearsal P2**, and **high-risk HPA/VM/HA edits P3+**. These *pilot priorities* do not replace the A–F product priorities or broaden the OSS Beta exit criteria.

Mac UI is an operations **client**; durable remote execution requires a hosted worker or provider-owned job with reconcilable status. Never treat customer RabbitMQ work queues as Kordra command queues.

## Practical end-to-end integration examples

**Direct deployment** (Beta candidate):

```text
GitLab/GitHub → CI build/test → OCI registry/repository@sha256:...
  → Kordra create release → approval bound to spec hash, target, actor and policy
  → durable operation + idempotency + lease → Helm/K8s or Compose/SSH driver
  → observed health verification → audit → authorized rollback/manual recovery
```

**GitOps-controlled target** (future): Kordra can approve a desired-state Git change, then allow Argo CD/Flux to reconcile and report sync/health. It **must not** also mutate that GitOps-owned resource directly; establish single-writer ownership and drift behavior.

**Messaging ownership:** Wolverine/PostgreSQL durable inbox/outbox and optional dedicated broker are internal Kordra transport choices subject to ADRs. External RabbitMQ, Kafka, VerneMQ/MQTT brokers belong to customer infrastructure and should be observed without consuming/ACKing/purging production messages. Broker metrics are not deployment commands.

## Reference environment mapping (reported, not verified)

| Domain | LAB-01 technology reported previously | Current Kordra responsibility |
|---|---|---|
| A | GitLab, Docker Buildx, registries | Read-only source/artifact discovery |
| B | Helm/Kubernetes, Docker Compose in separate local setup | Disposable target integration and rollout health; shared cluster observe-only |
| C | Proxmox, Talos Linux, Kubernetes/containerd | Scoped inventory; no VM/OS cluster mutation by default |
| D | Prometheus, Alloy/Loki/Grafana, RabbitMQ, VerneMQ, CNPG, Valkey, NFS | External status and release-correlated observations, no business payload consumption |
| E | Proposed IAM, permission, credential controls | Cross-tenant/target protection before any write operation |
| F | Proposed agent, driver, workflow and reconciliation architecture | No implemented Control Plane runtime at the reviewed baseline |

**Publication boundary:** LAB-01 inventory is *user-reported*, not live operational evidence or authority to manage company resources. Do not copy security observations, identifying hostnames, credentials, customer queue names or other sensitive topology into public product modules. Review existing published material and Git history with its rights owner separately.

## Technology selection gates

1. Identify the real user problem, owner bounded context, feature ID, priority and acceptance test.
2. Choose **Build / Integrate / Observe / Defer**; explain the value compared with an existing provider.
3. Verify target ownership, tenant/resource permission, secret and transport trust, data protection and blast radius.
4. For external side effects design idempotency, durable command/outbox, retry/timeout, unknown outcome, target lease/fencing, immutable approved spec, audit and recovery.
5. Run destructive/failure scenarios only on disposable, authorized targets. LAB-01 shared resources are **observe-only** by default.
6. Require an accepted ADR for architectural dependency/contract changes and update feature/spec/tests/traceability in the same PR.

## Stage mapping and authoritative sources

- **P0 Beta:** release/approval, artifact digest, scoped target and trusted identity, durable deploy, observed verification, audit and authorized rollback; first validate one complete driver path before claiming multiple targets.
- **P1:** additional SCM/CI/GitOps drivers, deployment-correlated metrics/logs, more capabilities driven by early adopters.
- **P2:** deeper IaC/cloud/VM/infrastructure operations and enterprise policy, HA/DR and cross-organization functionality when justified.
- **P3:** managed commercial cloud, marketplace, self-upgrade and broader platform lifecycle.

This document is **reference only**, not the source of truth for module behavior or whether code is implemented. See [scope](scope.md), [OSS Beta acceptance](oss-beta-acceptance.md), [roadmap](roadmap.md), [bounded contexts](../architecture/bounded-contexts.md), [messaging](../architecture/messaging.md), [driver contracts](../contracts/driver-contract.md), [source ADR-0009 (proposed)](../decisions/0009-source-architecture.md), [feature registry](../features/README.md) and [LAB-01 safety](../reference-environments/lab-01/README.md).
