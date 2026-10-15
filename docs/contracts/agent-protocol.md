---
id: CTR-AGENT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Agent communication protocol & security envelope

## Session lifecycle
`Enroll → Attest identity → Connect(outbound) → Heartbeat → Receive command → Journal → Execute → Report → Reconnect/Replay → Revoke`. Transport: authenticated TLS (mTLS recommended), version negotiation, certificate/key rotation, connectivity health.

## Commands
`DispatchOperation` includes `operationId`, `tenant/target binding`, `approvedSpecHash`, `leaseEpoch`, `deadline`, `allowedActions`, `policyVersion`, `signature/token binding`. Agent validates token, scope, expiry, replay and capabilities before execution; local journal first.

## Responses
`Accepted`, `Rejected(reasonCode)`, `Started(sequence)`, `Progress(sequence)`, `Finished(result+evidence)`, `Heartbeat(status,version,capacity)`. Sequence monotonic per operation; gateway deduplicates; `Finished` means driver reported completion, not necessarily verified business health.

## Reconnect
Agent replays unacknowledged operation receipts; gateway never blindly sends duplicate side effect when result missing. If lease epoch stale, agent refuses further mutation. Offline agent transitions operations to `Unknown/WaitingForObservation` following workflow policy.

## Security controls
No arbitrary command shell over agent gateway, no user session token to agent, no credentials in progress logs. Limit target scope; signed command expiry/revocation; audit enrollment and command execution.
