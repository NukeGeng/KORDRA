---
id: DOC-README
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Tổng mục lục tài liệu

**Start here:** [Scope](../GOAL.md) · [Rules](../AGENTS.md) · [Workflow](../WORKFLOW.md).

| Cần tìm | Tài liệu chuẩn |
|---|---|
| Tầm nhìn / phạm vi | [`GOAL.md`](../GOAL.md), [`product/scope.md`](product/scope.md) |
| Operator Workstation (10 groups) | [`product/operator-workstation.md`](product/operator-workstation.md) |
| Private Mac pilot (P0–P4) | [`product/operator-pilot-plan.md`](product/operator-pilot-plan.md) |
| OSS Beta acceptance | [`product/oss-beta-acceptance.md`](product/oss-beta-acceptance.md) |
| Product positioning / users | [`product/positioning.md`](product/positioning.md), [`product/early-adopter-validation.md`](product/early-adopter-validation.md) |
| DevOps / CI/CD / deploy A–F (43 groups) | [`product/technology-landscape.md`](product/technology-landscape.md) |
| Commercialization / brand | [`product/commercialization.md`](product/commercialization.md), [`product/domain-strategy.md`](product/domain-strategy.md) |
| OSS release & public LAB governance | [`governance/oss-release-readiness.md`](governance/oss-release-readiness.md), [`governance/public-reference-data-policy.md`](governance/public-reference-data-policy.md) |
| Evidence lifecycle & AI tool boundary | [`engineering/status-evidence-policy.md`](engineering/status-evidence-policy.md), [`engineering/ai-tooling-boundary.md`](engineering/ai-tooling-boundary.md) |
| Kiến trúc / component | [`architecture/`](architecture/README.md) |
| Source tree / .NET solution structure | [Architecture proposal and repository audit](architecture/README.md), [ADR-0009](decisions/0009-source-architecture.md) |
| Product / frontend repository ownership | [Clients submodule workflow](engineering/clients-development.md), [accepted narrow ADR-0012](decisions/0012-clients-git-submodule.md) |
| Tại sao đã chọn X? | [`decisions/`](decisions/README.md) |
| Module nào sở hữu nghiệp vụ? | [`governance/module-feature-definition.md`](governance/module-feature-definition.md), [`features/`](features/README.md) |
| Thay đổi permission hoặc Super Admin | [`architecture/security-multitenancy.md`](architecture/security-multitenancy.md), [`contracts/permissions.md`](contracts/permissions.md) |
| Message/event/queue | [`architecture/messaging.md`](architecture/messaging.md), [`contracts/message-catalog.md`](contracts/message-catalog.md) |
| Bảo trì, nợ kỹ thuật | [`engineering/technical-debt.md`](engineering/technical-debt.md), [`governance/change-impact.md`](governance/change-impact.md) |
| AI cần review source | [`governance/ai-review-protocol.md`](governance/ai-review-protocol.md) |
| AI routing / native review / runtime evidence | [`engineering/ai-orchestration/`](engineering/ai-orchestration/README.md) |
| Triển khai / incident | [`operations/`](operations/README.md) |
| Local PostgreSQL và role permissions | [`operations/local-postgresql-setup.md`](operations/local-postgresql-setup.md), [ADR-0011](decisions/0011-local-postgresql-roles.md) |
| Test và nghiệm thu | [`engineering/testing-strategy.md`](engineering/testing-strategy.md), [`engineering/definition-of-done.md`](engineering/definition-of-done.md) |
| Dữ liệu train AI tùy chọn | [`optional-ml/`](optional-ml/README.md) |

### Ba câu hỏi trước khi tin một tài liệu
1. `status` có phải `accepted` / `verified` không?
2. `last_reviewed`, owner và evidence commit/test có tồn tại không?
3. Tài liệu có là **source-of-truth** của thay đổi hay chỉ giải thích/ghi chú?

Nhiều tài liệu vẫn là `proposed`; đọc trạng thái từng file. ADR-0008 được accepted trong phạm vi bootstrap hẹp, còn FEAT-AMR-001 có implementation evidence riêng. Không suy ra toàn bộ kiến trúc hay module đã được triển khai từ một trạng thái hoặc CI xanh. `docs/tooling/doccheck.py` kiểm tra cấu trúc, liên kết, ID, ownership; **không xác minh được ngữ nghĩa code**.

### Bộ kiểm soát thay đổi tự động
- [Requirement → code → docs → release](governance/requirement-to-release.md)
- [Architecture drift register](engineering/architecture-drift.md)
- [Feature change history](engineering/change-history.md)
- [Actor permission matrix](contracts/actor-permission-matrix.md)
- [Security regression test matrix](engineering/security-regression-cases.md)
- [Change guard](tooling/README.md)

### Thử nghiệm trên môi trường thật
- [Reference environments — registry](reference-environments/README.md)
- [LAB-01 testbed](reference-environments/lab-01/README.md)
- [LAB-01 acceptance cases](reference-environments/lab-01/experiments/README.md)
- [ADR-0007 — testbed ≠ product](decisions/0007-reference-testbed-not-product.md)

### Current local bootstrap and reserved folders

[ADR-0010](decisions/0010-local-readonly-bootstrap.md) is explicitly accepted for the scoped local API/inventory bootstrap; [owner evidence](features/infrastructure-inventory/features/register-target.md#coordinator-final-acceptance--2026-10-18) records local tests/review separately from pending remote CI. The [folder framework](engineering/repository-folder-framework.md) documents actual versus reserved paths; ADR-0009 remains proposed.

### Narrow authorization baseline

[ADR-0003](decisions/0003-tenant-authorization.md) is human-accepted for the bounded permission/trusted-resource-scope policy baseline. This does not accept IAM runtime, authentication/session/storage, the whole permission catalog or positive inventory access. See [current roadmap](product/roadmap.md) for remaining contract prerequisites.
