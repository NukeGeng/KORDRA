---
id: EXP-LAB-ORDER
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Recommended execution order — zero disturbance first

## Sprint E0 — report-only discovery (P0)
1. Validate identity/profile boundary with `LAB-INV-003`; then cluster/VM inventory `LAB-INV-001/002`.
2. Read source/release metadata `LAB-CI-001`, `LAB-ART-001/003`, `LAB-DEP-001`.
3. Observe message topology `LAB-MSG-001/003/004`. 13 queue mapping will remain BLOCKED until full inventory rows are imported.
4. Inspect data and operations `LAB-DATA-001..005`, `LAB-HA-001/003`, `LAB-OBS-001..003/005`.
5. Collect gaps vs current Python/SQLite tool `LAB-LEG-001`, route defects and updated source evidence.

**Gate:** 0 mutating API calls and read permissions only; unknowns documented, not marked PASS.

## Sprint E1 — synthetic integration (P1)
1. Set up dedicated workloads / disposable VM with quota, test registry and isolated credentials.
2. Complete `LAB-ART-002`, `LAB-DEP-002/003/004/005`, `LAB-HA-002`, `LAB-OBS-004`.
3. Complete synthetic MQTT `LAB-MSG-002` and all authorization tests `LAB-SEC-001..004`.

**Gate:** version/digest/spec consistency, approve+lease+audit, revert verified.

## Sprint E2 — controlled resilience (P2)
1. Clone broker, stateful fixture, worker and agent; never inject on real business cluster.
2. Complete `LAB-MSG-005/006/007`, `LAB-DATA-006`, `LAB-AGT-001`.
3. Observe message duplication/reconnect/unknown outcomes and capture time-to-recover metric.

**Gate:** reproducible results, workload isolation, all fail conditions triaged.

## Sprint E3 — adoption review (P3, optional)
Request project owner, security and operations sign-off on evidence; decide whether a **single reversible application release** can enter a scheduled pilot. P3 is not a generic permission to change stateful infrastructure.

## Working definition of MVP validated
At least one end-to-end Helm sandbox release, one Docker Compose disposable-VM release, ACL-scoped inventory, outbox/inbox/worker recovery, role/tenant negatives and observability traces all linked to evidence. Everything else can be capability `read_only`, `unsupported` or `pending`, never a fabricated production promise.
