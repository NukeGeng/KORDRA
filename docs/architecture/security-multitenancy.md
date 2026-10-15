---
id: ARCH-SECURITY
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Security, authorization, actors & multi-tenancy

## Scope model
`platform` (operator nền tảng) ≠ `organization/tenant` (khách hàng) ≠ `project` ≠ `environment` ≠ `target`. Scope tree để kiểm tra quyền, **không đồng nghĩa cấp mọi quyền xuống con**.

- Principal = User / ServiceAccount / Agent identity.
- Membership = principal thuộc org (user có thể thuộc nhiều org).
- RoleTemplate = tập permission đề xuất, có version; RoleBinding = role/permission cấp tại phạm vi rõ ràng.
- PolicyDecision = subject + action + resource + scope + contextual conditions → Permit/Deny + reasons.
- `platform` roles không mặc định đọc được secret/data tenant. Bất kỳ hỗ trợ cross-tenant nào phải có explicit delegated grant / break-glass, TTL, reason, approval và audit; không implicit superuser xuyên tenant.

## Authorization pipeline
1. Xác thực principal; token issuer/audience/expiry/signature và service identity.
2. Resolve org from trusted membership / session and target resource; body/header tenantId **không đủ để authorize**.
3. Load resource ownership từ authoritative store; verify tenant boundary.
4. Policy evaluate permission key and **resource-specific scope**. Deny by default, deny overrides allow.
5. Step-up authentication + SoD cho production/destructive/support-elevation.
6. Log decision an toàn (không log secrets), correlate request/deployment.
7. Worker/agent re-check signed operation context + policy version/expiry tại execution boundary, tránh TOCTOU.

## Add a new role safely
Tạo role template data-driven, không sửa code `if RoleName`; permission catalog phải mở rộng được. Migration an toàn với bindings cũ; test grant/revoke + cross-scope + existing role regression. Platform Super Admin là scope `platform`, không là người quản trị mọi tenant by default. Xem [permissions](../contracts/permissions.md).

## Multi-tenant data
- MVP: shared PostgreSQL + `tenant_id` on tenant-owned entities, tenant-aware repository/query, DB RLS (nếu chọn phải có ADR). Global entities không gắn tenant phải phân loại rõ.
- Mọi query, async message, cache key, blob path và agent dispatch chứa tenant context đã xác thực.
- Không dùng duy nhất `tenantId` do client gửi. Foreign key/resource association cần đảm bảo không trỏ chéo tenant.
- Test cross-tenant leak cho API, projection, event handler, websocket group, logs, exports, admin support.

## External Kubernetes access
Credential per-target least privilege, namespace restrictions khi có thể; namespace KHÔNG bảo đảm isolation toàn diện. Không yêu cầu `cluster-admin` mặc định; đặc biệt cẩn trọng secret, exec, pods, role bindings.

## Open design decisions
`[NEEDS-DECISION]`: chọn ABAC/PBAC engine, database-level isolation (app-only vs RLS), identity provider, support elevation approval policy, tenant billing model. Không tự xem các lựa chọn này là finalized.

Chi tiết ma trận role/action: [Actor Permission Matrix](../contracts/actor-permission-matrix.md) và bộ kiểm thử [SEC-001–SEC-020](../engineering/security-regression-cases.md).
