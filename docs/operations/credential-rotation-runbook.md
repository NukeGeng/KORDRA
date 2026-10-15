---
id: OPS-ROTATE
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — Credentials and agent certificate rotation

1. Identify target, owner tenant, provider, scopes, active operation leases and expiry.
2. Issue new least-privilege credential/certificate (never store plaintext in docs).
3. Test in isolated channel; bind new `secretRef` with version and audit.
4. Drain or fence operations using old credential where required; revoke old grant/cert.
5. Verify new executions use new reference, rejected old credentials cannot recover privilege.
6. Update incident/debt if credential was leaked; check log redaction and audit export.
