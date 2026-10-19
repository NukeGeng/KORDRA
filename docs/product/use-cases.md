---
id: PROD-UC
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
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

## Private Mac Operator Pilot and OSS Beta journeys (proposed, not implemented)

OP-01…OP-10 are **pilot requirements**, not new implemented Use Cases or module registrations. Map each selected increment to an approved owning feature before implementation: [operator requirements](operator-workstation.md) and [private pilot](operator-pilot-plan.md).

## OSS Beta journey (proposed, not implemented)

Scoped target registration → immutable artifact discovery (UC-002) → release request (UC-003) → frozen-spec approval (UC-004) → observed deployment verification (UC-005) → separately authorized rollback (UC-006). These existing use cases form the [Beta acceptance](oss-beta-acceptance.md) scenario; digest tests alone do not prove them implemented.

Mỗi UC phải map ≥1 feature_id; khi bổ sung UC mới phải thêm module owner, permission scope và acceptance.
