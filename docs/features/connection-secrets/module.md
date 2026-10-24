---
id: MOD-SEC
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-24
module_id: MOD-SEC
---

# Connections & Secret References — module definition

**Module ID:** `MOD-SEC` · **Phase:** MVP · **Owner:** NukeGeng for the accepted bounded T08 pilot scope; broader MVP remains proposed.

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


## Accepted T08 scope and actual local source

[ADR-0015](../../decisions/0015-credential-boundary-preparation.md) and [Credential boundary v1](../../contracts/credential-boundary-v1.md) adopt the narrow offline/local Mac pilot. The [owning feature](features/secret-binding.md#t08-local-implementation--2026-10-24) now maps actual ConnectionManagement runtime/Contracts, offline Host composition and tests. Broad inbound commands/events and online catalog capabilities above remain proposals, not registered APIs or messaging. Gate receipts/native fixture evidence remain separate from design approval and real configuration.
