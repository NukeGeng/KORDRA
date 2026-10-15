---
id: ARCH-EXECUTION
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Execution Plane — Agent / Gateway / Driver

## Trust separation
Control Plane decides *what/where/when/allowed*. Agent performs only **signed/authorized scoped operations**. Driver translates a typed plan into Helm, Compose, SSH or provider API; never receives unrestricted administrative API by default.

## Agent lifecycle
`PendingEnrollment → Active → Degraded/Offline → Revoked/Retired`. Enrollment requires short-lived enrollment token, cryptographic identity, target binding, least privilege; rotation/revocation and audit are mandatory. Agent initiates outbound secure connection; offline must not block Control Plane APIs.

## Operation contract
`operationId`, `tenantId`, `targetId`, `approvedSpecHash`, `capabilities`, `leaseEpoch`, `deadline`, `traceId`; signed/verified at trust boundary. Agent persists operation journal locally before execution and replays result after reconnection.

## Driver interface conceptual
`DescribeCapabilities → ValidateTarget → Plan → Execute → Observe → Rollback(if supported)`. Each driver documents auth permissions, convergence signals, cancellation, retry safety, and rollback constraints. Helm upgrade and Docker Compose up are NOT assumed semantically equivalent.

## Worker / gateway failure
- Agent sends `Accepted`, `Started`, `Progress`, `Finished` with monotonically increasing sequence numbers per operation.
- Duplicate/reordered report → deduplicate by operation ID + sequence, ignore stale epoch.
- Timeout or offline → `Unknown`, fetch operation journal and target observed state; do not blind reissue.
- If operator must force recovery, record reason and new operation ID/version; prior operation fenced.

## Network
Agent outbound TLS/mTLS or delegated identity; no open arbitrary inbound SSH from browser. Secret retrieval done at runtime through scoped provider; never forward raw secrets through UI event stream.
