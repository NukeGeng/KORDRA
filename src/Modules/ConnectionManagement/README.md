---
id: LAYOUT-SRC-MODULES-CONNECTIONMANAGEMENT
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-24
---

# ConnectionManagement — MOD-SEC local pilot implementation

This directory now contains the scoped T08 runtime and BCL Contracts under accepted [ADR-0015](../../../docs/decisions/0015-credential-boundary-preparation.md). It owns credential metadata/history/receipts and bounded read-only Keychain infrastructure. [FEAT-SEC-001](../../../docs/features/connection-secrets/features/secret-binding.md) maps actual source/tests and evidence. This does not accept the broader ADR-0009 source proposal or all proposed module capabilities. No credential HTTP endpoint or network provider consumer is enabled. Native fixture setup/mutating APIs live only in tests; installation activation and unsupported platforms are explicit in the [runbook](../../../docs/operations/credential-boundary-v1.md).
