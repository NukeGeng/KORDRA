---
id: PROD-SCOPE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Phạm vi, phân kỳ và Non-goals

## Product boundaries
Quản lý *release intent*, artifact, approval, execution orchestration, drift observation, rollback orchestration, audit. Tích hợp công cụ sẵn có thông qua driver/adapters. Không sở hữu kube-apiserver, Jenkins build scheduling hay registry storage.

## Personas
Developer (yêu cầu release), Release Manager (duyệt), Operator (thực thi/incident), Tenant Admin (quản trị tổ chức), Platform Operator (quản trị nền tảng), Machine Agent (principal phục vụ execution). Các role là **role templates**, authorization dùng permission + scope.

## MVP boundaries
Helm/Kubernetes; Compose/SSH; đăng ký agent/kết nối; artifact pinned; môi trường; approval; workflow bền vững; audit; emergency recovery có policy.

## V1.5/V2
CI webhook, Jenkins/Actions, drift monitor, expanded notifications, quotas, centralized observability, policy-as-code sau ADR. Multi-company isolation thiết kế từ đầu, trải nghiệm quản trị enterprise triển khai theo giai đoạn.

## Out-of-scope initially
GitOps engine cạnh tranh Argo CD, full metrics backend, arbitrary shell automation, automatic DB schema rollback, ML training.
