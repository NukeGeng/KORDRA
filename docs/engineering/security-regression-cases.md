---
id: ENG-SEC-TEST
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Security regression matrix and tenancy attack cases

Mỗi fixture có `orgA`, `orgB`, `userAB`, `userA`, `platformAdmin`, `agentA`, `agentB`, `prodA`, `stagingA`.

| Test ID | Attack / scenario | Expected |
|---|---|---|
| SEC-001 | Body orgA nhưng resource thuộc orgB | DENY; no resource existence leak |
| SEC-002 | Query read model thiếu org filter | DENY, no B rows |
| SEC-003 | Async message mang `tenantId=orgB` giả | reject at trusted resource boundary |
| SEC-004 | Cache key không chứa tenant; user A sau B | isolation guaranteed |
| SEC-005 | WebSocket subscribe deployment orgB | DENY |
| SEC-006 | TenantAdmin cấp `platform.system.manage` | DENY, audited escalation |
| SEC-007 | PlatformSuperAdmin không có support grant đọc payload orgA | DENY |
| SEC-008 | Support grant expired trong workflow | suspend/re-evaluate per policy |
| SEC-009 | Owner approves own production release | DENY protected SoD |
| SEC-010 | Approved artifact digest changed before dispatch | DENY; new approval required |
| SEC-011 | AgentA targets B operation | DENY at gateway and agent |
| SEC-012 | Revoked agent reconnects with prior cert | DENY |
| SEC-013 | Replay old signed operation after new lease epoch | DENY |
| SEC-014 | Unauthorized DLQ replay | DENY and audit |
| SEC-015 | SSH driver accepts arbitrary shell string | DENY/validate typed plan |
| SEC-016 | Secret present in audit/log/response | redacted and test fails if leaked |
| SEC-017 | User belongs A+B, switches active org, stale token | scope re-evaluated |
| SEC-018 | New role template added with custom permissions | existing scopes unaffected |
| SEC-019 | Platform operator exports tenant audit outside grant | DENY |
| SEC-020 | Policy changes after approval | re-evaluate/fail as required |

Use tests as a living matrix; mark implemented only with test IDs, source paths and results. Quyết định deny/allow khi không chắc phải nghiêng về deny-by-default rồi review.
