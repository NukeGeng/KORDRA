---
id: CTR-ACTOR-MATRIX
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Actor / permission / tenant scope / separation of duties

Ma trận này là **role template defaults** để review; quyền thực thi luôn do dynamic binding + policy quyết định. `A=allowed by template`, `C=conditional approval/step-up`, `—=no default permission`.

| Action | Developer (project) | Release Approver (env) | Operator (env) | Tenant Admin (org) | Platform Super Admin (platform) |
|---|---|---|---|---|---|
| Read own project release | A | A | A | A | — |
| Create release | A | — | — | — | — |
| Request deployment | A | — | A | — | — |
| Approve protected env | — | C | — | — | — |
| Execute deployment | — | — | C | — | — |
| Rollback deployment | — | — | C | — | — |
| Manage org membership | — | — | — | C | — |
| Configure platform flags | — | — | — | — | C |
| Create time-limited support grant | — | — | — | — | C |
| Read tenant credentials/plaintext | — | — | — | — | — |

### Resource scope and policy
- `A` vẫn cần đúng org/project/env và active grants; không có A không có nghĩa cấm vĩnh viễn nếu role template được cấu hình mở rộng.
- Production approval không tự cấp quyền execute. Người yêu cầu không tự duyệt theo SoD mặc định.
- Tenant Admin không tự quản được quyền platform; grant delegation ceiling và audit.
- Platform Super Admin không đọc release tenant theo mặc định. Hỗ trợ tenant yêu cầu **scoped support grant** có expiry, reason và second-party approval khi policy yêu cầu.

### Regression fixtures
User1(A and B), User2(A only), User3(platform only), Agent1(A/targetX), Agent2(B/targetY), Approver1(A/prod). Mỗi action chạy allow/deny trên các pair; test cả role template mới không ảnh hưởng quyền đã cấp.

### Add new role procedure
Create role template + permission data migration → binding rules → revise matrix → negative/positive tests across scope → audit → deploy. Không sửa if-role ở handler.
