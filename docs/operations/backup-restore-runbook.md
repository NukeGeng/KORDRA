---
id: OPS-BACKUP
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Runbook — PostgreSQL and audit backup / restoration

## Assets
PostgreSQL/Marten streams, workflow state, outbox/inbox, identity/role grants, secrets-provider configuration references, agent registrations, audit indexes, object storage evidence.

## Before production
Specify actual RPO/RTO, encryption, retention and regional policy; test PITR + object store restore in a separate environment. Document who can read backup (tenant/secret sensitive).

## Restore sequence (exercise)
Freeze write/dispatch; assess currently running side effects; restore DB to consistent snapshot; restore credentials/identities and audit/evidence; revalidate outbox/inbox and lease epochs; reconnect agents in safe-observe mode; compare actual targets before resuming mutations.

## Verification
Check tenant membership/resource bindings, expected workflow/event counts, audit integrity, representative release queries, and full end-to-end recovery test. Record elapsed RTO and potential data gap; backup existence alone does not prove recovery.
