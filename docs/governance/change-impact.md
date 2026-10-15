---
id: GOV-IMPACT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Change Impact Matrix và chống lệch kiến trúc

| Thay đổi | Bắt buộc đọc | Bắt buộc cập nhật | Kiểm thử tối thiểu |
|---|---|---|---|
| Thêm role / permission | Security architecture, permission catalog | identity/tenant/platform-admin feature, permissions contract, ADR nếu đổi scope | deny, allow, cross-tenant, privilege escalation |
| Thêm message | Messaging, owner module | message-catalog, producer+consumer feature, schema version | duplicate, ordering, failure, compatibility |
| Thay workflow | Workflow, state machine | deployment/workflow specs, runbooks, ADR nếu invariant đổi | crash/restart, timeout, unknown outcome |
| Thêm deployment driver | Execution, driver contract | driver feature, capability matrix, rollback runbook | conformance, idempotency, unsafe operation |
| Thay tenant isolation | Security, data | boundary ADR, migrations, API/message contracts | isolation, horizontal/vertical escalation |
| Thay data schema | Data architecture | entity owner, migration/runbook, compatibility | upgrade/downgrade/rollback scenarios |
| Thay secrets | Security, connection feature | ops rotation, incident plan | redaction, revocation, expiry |
| Sửa production bug | Feature + incident | incident, debt, test regression, runbook nếu có | reproduction + regression |
| Model/dataset (optional) | ML lineage & policy | dataset registry/model card | leakage/bias/reproducibility |

## Architecture drift report
`DRIFT-ID | date | expected (doc/ADR) | actual (code/infra evidence) | scope | severity | owner | decision | deadline | status`.
Không sửa tài liệu để hợp thức hóa code sai mà thiếu review. Nếu code đúng nhưng baseline lạc hậu, lập ADR superseding hoặc update accepted spec bằng review.
