---
id: OPS-AGENT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — Agent offline or disconnected during operation

1. Identify `agentId`, tenant/target, last heartbeat, active `operationId` and lease epoch.
2. Stop sending new mutation commands; mark transport disconnected and operation `Unknown` if outcome uncertain.
3. Check local agent journal on reconnect, compare operation sequence/receipt signatures.
4. Observe target actual digest/revision/readiness independently where safe.
5. Reconcile to verified outcome or escalate to operator; force re-execute only with fenced prior operation, new approval if policy demands.
6. Investigate certificate expiry/revocation, network policy, gateway saturation; do not expose private credentials in diagnostic logs.
7. Audit resolution with reason and evidence. Test split brain before restoring normal dispatch.
