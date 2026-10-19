---
id: PROD-OSS-BETA
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Kordra OSS Beta — Product contract and exit gates

## Scope (proposed, not shipped)
A first-time operator can install a local/sandbox Kordra instance, configure a **non-production** target, pin a release artifact, authorize an operation, deploy, observe health and execute a controlled rollback with linked audit history.

Candidate target adapters: Helm/Kubernetes and Docker Compose over SSH. Implement and verify **one** adapter end-to-end first; the second follows via identical contract tests. No commitment to universal driver parity before capability negotiation.

## Required vertical slice
1. Owner creates an organization/project/environment/target under trusted scope.
2. Registers artifact identity by immutable digest (and registry/repository identity where applicable).
3. Creates a release spec whose artifact/target/configuration cannot be silently changed after approval.
4. Authorizes or approves a deployment within minimal permission policy; production-grade approval separation remains a non-bypassable invariant.
5. Orchestrates execute → verify observed state with timeout and unknown-outcome behavior.
6. Captures actor, resource, spec hash, operation ID and audit outcome; exposes deployment history and rollback.

## Acceptance gates
| Gate | Evidence required |
|---|---|
| Install & onboarding | Clean sandbox install and reproducible Quick Start on a second machine/environment |
| Deploy & verify | Independent observed health; not merely command exit/ACK |
| Rollback & failure | Bad image, timeout/agent disconnect, duplicate dispatch, unknown outcome, rollback conflict |
| Security | Wrong tenant, missing permission, expired approval, separation-of-duties where applicable |
| Release integrity | No latest-tag substitution, immutable spec/hash/target binding |
| Source governance | Feature source+test mapping, accepted ADRs for implemented choices, current docs and passing CI |
| Distribution | License approved, contribution/security policies, clear supported-platform matrix and limitations |

## Explicitly NOT required for Beta
Full CI builder, managed cloud, enterprise support elevation, automatic DB schema rollback, arbitrary host command execution, comprehensive monitoring platform, broad infrastructure lifecycle automation, full self-upgrade supervisor or plugin marketplace.

## Relationship to the earlier private operator pilot
The [private Mac operator pilot](operator-pilot-plan.md) validates reusable capabilities and real operator experience but **does not count as OSS Beta**. Production/company deployments, database restore, HPA edits, workflow visual editor and Proxmox/Talos worker provisioning are not automatic Beta requirements. A Beta claim requires independent installation and observed deployment safety on an approved disposable environment, regardless of earlier internal use.

## Test environment safety
LAB-01 shared production-like infrastructure stays observe-only. Use isolated sandbox/mock target for write tests. Beta is not permission to deploy onto a customer's production resources.

## Exit decision
Owner approves Beta only after real install/test artifacts and safety evidence; document failure cases and product limitations. A checklist written in docs is not acceptance proof.
