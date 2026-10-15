---
id: MOD-SEC
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-SEC
---

# Connections & Secret References — module definition

**Module ID:** `MOD-SEC` · **Phase:** MVP · **Owner:** unassigned (assign before acceptance).

## Responsibility and boundary
Connections & Secret References owns: **ConnectionProfile, SecretReference, CredentialBinding, RotationStatus**. Không nhận ownership business của module khác. Giao tiếp bằng commands/events hoặc read contracts, không query trực tiếp internal storage của context khác.

## Inbound commands / exposed operations
BindSecretReference, TestConnection, RotateBinding.

## Outgoing facts / events
SecretBindingChanged, CredentialExpired.

## Authorized capabilities
credential.bind, target.manage. Quy tắc chi tiết: [permission catalog](../../contracts/permissions.md).

## Dependencies
infrastructure-inventory; audit-compliance. Các thay đổi contract của dependencies phải được review bởi hai module owners.

## Non-negotiable invariants
Không lưu plaintext secrets; keychain (local) hoặc secret manager (server), permission scoped.

## Failure / risk scenarios
Expired credential, leaked logs, secret reference points to wrong environment. Đây là test đầu vào bắt buộc, không chỉ ghi chú mô tả.

## Feature registry
- [FEAT-SEC-001 — Bind credentials safely to deployment targets](features/secret-binding.md)

## Implementation mapping
`source_paths: []` và `test_paths: []` có nghĩa **chưa đối chiếu repository thật**. Sau khi có code, bổ sung path, commit/test evidence ở mỗi feature, không điền đường dẫn dự đoán.

## Definition of done
Tuân thủ [DoD](../../engineering/definition-of-done.md), [module boundaries](../../engineering/module-boundaries.md), [architecture](../../architecture/bounded-contexts.md).
