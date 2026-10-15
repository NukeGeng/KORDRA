---
id: PROD-ROADMAP
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Delivery roadmap & architecture maturity

| Stage | Deliverable | Exit criteria |
|---|---|---|
| 0 Foundation | Accepted ADR, auth scope, workflow/message contracts, docs CI | Baseline approved, threat review complete |
| 1 MVP core | Organization/tenant, environments, releases, approval, Helm/Compose | End-to-end happy path + wrong-tenant/restart/duplicate tests |
| 2 Distributed execution | Agent gateway, lease, durable workflow, operator recovery | Agent disconnect + unknown outcome + failover verified |
| 3 CI & ops | Webhooks, CI integration, drift dashboard, alerting | Verified webhook signatures, traceability, on-call runbook |
| 4 Enterprise | Tenant quota, platform admin elevation, HA/DR, policy | Security review, drills, SLO measurement |

Không ước lượng ngày công khi chưa biết team/capacity; mỗi stage có thể chia task riêng. Chỉ được ghi "Done" khi Definition of Done thỏa.

## Current verified inventory and continuation
Baseline inspected: `d0eff75951d1d587b1d9689c189a8a62bae18d58`, clean `main` before branch `feat/artifact-identity-foundation`. AMR Python tooling exists with 43 passing tests and a successful initial GitHub docs CI run. There was no product source, host, persistence or application test project. All architecture ADRs and product feature specs were proposed, not accepted. The installed SDK is .NET 10.0.400; this observation is not an architectural approval.

Current milestone: **Stage 0 Foundation**. Active task: **F0-ART-I1 / FEAT-ART-001/I1**, local artifact digest validation; implementation and local validation complete (48 .NET tests, 47 Python tests, format/build/docs gates, independent native review), with remote CI and human ADR/owner acceptance tracked through the PR. Product runtime readiness: **not ready**. ADR-0008 is a reviewable bootstrap proposal; other stack/policy choices remain open. Never infer a completed module from a scalar domain library.

| Module | Feature | Actual product implementation |
|---|---|---|
| MOD-IAM | FEAT-IAM-001 | Not implemented; identity provider and scoped policy decisions open |
| MOD-TEN | FEAT-TEN-001 | Not implemented; depends on identity/authorization/audit |
| MOD-PAD | FEAT-PAD-001 | Not implemented; scoped support/elevation policy open |
| MOD-PRJ | FEAT-PRJ-001 | Not implemented; trusted tenant ownership prerequisite |
| MOD-INV | FEAT-INV-001 | Not implemented; target registration/capability policies open |
| MOD-SEC | FEAT-SEC-001 | Not implemented; secret provider binding and access policy open |
| MOD-ART | FEAT-ART-001 | I1 locally validated: 48 tests/build/format/native review; registry-backed registration not implemented |
| MOD-REL | FEAT-REL-001 | Not implemented; artifact/catalog/approval dependencies unmet |
| MOD-APR | FEAT-APR-001 | Not implemented; policy/SoD/expiry/spec binding prerequisites unmet |
| MOD-MSG | FEAT-MSG-001 | Not implemented; durable transport/storage ADR open |
| MOD-WFL | FEAT-WFL-001 | Not implemented; message/lease/recovery contracts not validated |
| MOD-DEP | FEAT-DEP-001 | Not implemented; workflow/approval/driver dependencies unmet |
| MOD-AGT | FEAT-AGT-001 | Not implemented; enrollment trust/target scope decisions open |
| MOD-DRV | FEAT-DRV-001 | Not implemented; approved-operation/capability contract validation pending |
| MOD-OBS | FEAT-OBS-001 | Not implemented; observation trust/freshness contracts pending |
| MOD-REC | FEAT-REC-001 | Not implemented; inventory/observations/single-writer mode required |
| MOD-AUD | FEAT-AUD-001 | Not implemented; append-only storage/retention decision open |
| MOD-CI | FEAT-CI-001 | Not implemented; provider identity/signature/dedup contracts pending |
| MOD-NOT | FEAT-NOT-001 | Not implemented; authorized event-driven notification prerequisites unmet |
| MOD-SCH | FEAT-SCH-001 | Not implemented; durable timer/timezone/change-window policy pending |

## Dependency graph and implementation order
The module summaries contain cycles (IAM ↔ TEN; ART ↔ CI; REL ↔ APR). They describe runtime collaboration, not permission to introduce cyclic project references. Resolve task-level contract seams with owners/accepted ADRs before implementing cross-module wiring. This is tracked in DRIFT-BOOT-001; no dependency direction was silently changed.

| Order / milestone task | Prerequisites | Implementation / validation exit |
|---|---|---|
| F0-A — repository/source/docs audit | Existing Git revision and governance | Inventory above, drift/debt and scope identified |
| F0-ART-I1 — immutable digest domain | Existing OCI invariant; isolated validation scope | Build, known hash vectors, malformed input, content/size mismatch, immutability, concurrency and module-boundary tests; independent review; proposed ADR |
| F0-B — accept development baseline | Human ADR-0008 decision; owner assignment | Approved .NET/testing bootstrap; no implied acceptance of all proposed ADRs |
| F0-C — identity and audit contract seams | ADR-0003, identity provider, scope/deny/expiry and audit decisions | Tenant/platform isolation, revocation and scoped authorization contracts and tests agreed |
| M1-A — IAM/TEN then project/environment/target/secret references | F0-C, persistence/credential decisions | Trusted ownership and negative access tests before registration or API exposure |
| M1-B — artifact registration and signed CI intake | M1-A, registry adapter/SSRF/credential policy, signed webhook contract | Registry metadata/content verified; immutable tenant-owned record; dedup/audit; no mutable-tag substitution |
| M1-C — release and approval | M1-B, approved frozen-spec/SoD/policy contract | Exact artifact/spec/target binding; expiry/revocation/self-approval tests |
| M2-A — durable messaging and workflow | ADR-0002, execution/lease/state contracts | Inbox/outbox, restart, duplicate, order, fencing and unknown-outcome tests |
| M2-B — agents, driver and deployment | M1-C + M2-A, ADR-0006, approved enrollment/target ownership | Scoped fake-target end-to-end rollout; verify observed state; no production operation |
| M3 — observation/reconciliation/CI/notifications/scheduler | Trusted scoped state and durable workflow; ADR-0004 | Freshness, single-writer, signed provider, recovery, timezone and permission regressions |
| M4 — operational/enterprise hardening | Measured product behavior, explicit authorized test environment | HA/DR/backup restore, support elevation and on-call evidence; separate human operation approvals |

Continuation state lives here and in each owning feature/traceability entry, not a second task-management system. Before the next session, check Git revision, dirty paths, exact feature evidence and current review/CI status. Next action after I1 validation: human decision on ADR-0008 and identity/persistence prerequisites; do not auto-accept them or move to production.
