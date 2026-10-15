---
id: OPS-MSG
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — Backlog, poison message and DLQ redrive

## Detection
Queue depth, oldest-message age, outbox relay age, failure rate, `messageId/correlationId`, consumer version, broker health.

## Triage
Classify transient provider timeout vs schema incompatibility vs tenant/authorization failure. Schema/security errors must be quarantined, not endlessly retried. Check if external deployment side effect may have already occurred.

## Controlled recovery
1. Preserve DLQ message ID, sanitized envelope, original attempts, failure reason and target.
2. Ensure consumer version/schema fixed and tenant/policy are still valid.
3. For side-effect commands, **observe target + operation journal** before replay; duplicate dedup protection is not proof of exactly-once physical mutation.
4. Redrive explicitly with scoped admin approval, dedicated audit and rate limit.
5. Verify stuck queue cleared, no duplicate deployment, record post-incident follow-up.

## Never
Bulk replay all DLQ messages without categorization, replay expired production approval, delete queue to suppress incident, or expose secrets in diagnostics.
