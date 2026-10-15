---
id: PROD-UC
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Use-case catalog / Actor map

| ID | Actor | Mục tiêu | Module owner |
|---|---|---|---|
| UC-001 | Tenant Admin | Mời user, gán permission có scope | tenant-management |
| UC-002 | Developer | Đăng ký immutable artifact | artifact-management |
| UC-003 | Developer | Tạo release và yêu cầu deploy | release-management |
| UC-004 | Approver | Duyệt/reject cấu hình deployment đã pin | approval-governance |
| UC-005 | Operator | Deploy và quan sát rollout | deployment-management |
| UC-006 | Operator | Rollback về phiên bản đã xác thực | deployment-management |
| UC-007 | Platform Operator | Đăng ký/thu hồi agent | execution-agents |
| UC-008 | System | Xử lý message trùng, worker crash | messaging |
| UC-009 | System | Phát hiện drift mà không tranh chấp Argo CD | reconciliation |
| UC-010 | Platform Operator | Hỗ trợ tenant qua scoped elevation có audit | platform-admin |

Mỗi UC phải map ≥1 feature_id; khi bổ sung UC mới phải thêm module owner, permission scope và acceptance.
