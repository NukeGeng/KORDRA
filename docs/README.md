---
id: DOC-README
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Tổng mục lục tài liệu

**Start here:** [Scope](../GOAL.md) · [Rules](../AGENTS.md) · [Workflow](../WORKFLOW.md).

| Cần tìm | Tài liệu chuẩn |
|---|---|
| Tầm nhìn / phạm vi | [`GOAL.md`](../GOAL.md), [`product/scope.md`](product/scope.md) |
| Kiến trúc / component | [`architecture/`](architecture/README.md) |
| Source tree / .NET solution structure | [Architecture proposal and repository audit](architecture/README.md), [ADR-0009](decisions/0009-source-architecture.md) |
| Tại sao đã chọn X? | [`decisions/`](decisions/README.md) |
| Module nào sở hữu nghiệp vụ? | [`governance/module-feature-definition.md`](governance/module-feature-definition.md), [`features/`](features/README.md) |
| Thay đổi permission hoặc Super Admin | [`architecture/security-multitenancy.md`](architecture/security-multitenancy.md), [`contracts/permissions.md`](contracts/permissions.md) |
| Message/event/queue | [`architecture/messaging.md`](architecture/messaging.md), [`contracts/message-catalog.md`](contracts/message-catalog.md) |
| Bảo trì, nợ kỹ thuật | [`engineering/technical-debt.md`](engineering/technical-debt.md), [`governance/change-impact.md`](governance/change-impact.md) |
| AI cần review source | [`governance/ai-review-protocol.md`](governance/ai-review-protocol.md) |
| AI routing / native review / runtime evidence | [`engineering/ai-orchestration/`](engineering/ai-orchestration/README.md) |
| Triển khai / incident | [`operations/`](operations/README.md) |
| Test và nghiệm thu | [`engineering/testing-strategy.md`](engineering/testing-strategy.md), [`engineering/definition-of-done.md`](engineering/definition-of-done.md) |
| Dữ liệu train AI tùy chọn | [`optional-ml/`](optional-ml/README.md) |

### Ba câu hỏi trước khi tin một tài liệu
1. `status` có phải `accepted` / `verified` không?
2. `last_reviewed`, owner và evidence commit/test có tồn tại không?
3. Tài liệu có là **source-of-truth** của thay đổi hay chỉ giải thích/ghi chú?

Mọi file hiện tại mặc định `proposed` để tránh ngộ nhận đã được triển khai. `docs/tooling/doccheck.py` kiểm tra cấu trúc, liên kết, ID, ownership; **không xác minh được ngữ nghĩa code**.

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
