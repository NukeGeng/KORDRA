---
id: CTR-RELEASE-EXECUTION-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-20
---

# Release, approval and execution v1 — cross-owner proposal

This defines milestone behavior for ART/REL/APR/MSG/WFL/DEP/DRV/OBS/AUD. It is not an implemented API/state machine or acceptance of ADR-0002/0004/0005/0006. Owner feature sections link here for shared binding/compatibility; each owner retains its business transitions. The [Luna handoff](../engineering/luna-implementation-handoff.md) lists adoption gates before dependent source. Later worker-authority/persistence and provider details need exact approval and conformance evidence; the single-process OP-P0 gate is never reused as a distributed guarantee.

## Immutable v1 bindings and input limits

Proposed frozen release includes `schemaVersion=1`, tenant/project/application/environment/target IDs, target-binding revision, immutable artifact digest plus repository/platform/content provenance, driver/capability version, pinned chart/package digest, safe configuration digest/reference revisions, desired-state writer mode, health-policy revision and policy revision. No plaintext secret or mutable tag is part of the authoritative artifact identity; secret material never appears in API/message/audit payloads. A binding change requires a new revision or rejected preflight, never silent retargeting.

REL owns canonical UTF-8 spec bytes and SHA-256 spec hash. Serialization rules must be explicit before implementation: ordinal object-key ordering, schema-declared array order, no duplicate keys or unrecognized fields, no floating-point/nonfinite values, UTC timestamps in one representation, and distinct absent/null semantics. Only schema-declared fields enter the hash; tests include byte-exact golden vectors, Unicode, key reordering and semantically meaningful changes. Backend computes the authoritative hash; client-side previews cannot supply it. Use the existing ART digest implementation without weakening its content checks.

Initial limits proposed for validation: immutable spec at most 64 KiB; safe command body at most 128 KiB; IDs bounded to 128 characters and schema-specific allowlists; no arbitrary CLI/shell, executable template or unreviewed chart hook. Actual chart/rendered-resource allowlist, CLI/SDK/version pins and hook behavior require a driver adoption decision before live execution.

## Owner state transitions

| Owner | Proposed state / transition | Invariant |
|---|---|---|
| ART | Verified immutable record; retirement marks availability but never changes identity | Same project/repository/platform/digest registration is idempotent; tags resolve once to digest, mismatches reject |
| REL | Draft → Frozen; superseding revision creates a new identity; retire does not rewrite bytes | An approved frozen revision cannot be PATCHed; another resource/spec uses another revision/hash |
| APR | Pending → Granted or Rejected; Granted → Revoked or Expired | Approval binds requester, approver, exact release/spec/target, action, policy/definition revisions and exclusive expiry; no resurrection |
| WFL | Pending → Preflight → AwaitingDispatch → Dispatched → Observing → Completed/Failed; any ambiguous dispatched stage → Unknown → Reconciling | Persist before sending; a workflow result is not independently the deployment outcome |
| DEP | Requested → Running → Verifying → Verified/Failed; ambiguous effect → Unknown | Only observed evidence under the health policy yields Verified; safe pre-effect cancellation differs from unknown partial execution |
| Rollback / DEP | New requested operation referencing prior verified immutable revision | Fresh `deployment.rollback`, scope/policy/approval where required, current target ownership and capability; no database/schema rollback implied |

Transitions are adopted only with owner ADR/contracts and executable transition tests. Terminal records are immutable facts; further action creates a new operation or reconciliation record. Status cannot move Unknown to Verified just because a worker reconnects or a command returned zero.

## API and permission proposal

| Operation | Owner / route candidate | Permission and behavior |
|---|---|---|
| Register artifact | ART `POST /api/v1/projects/{project}/artifacts` | `artifact.register` project; immutable digest/provenance verification and idempotency |
| Freeze release | REL `POST /api/v1/projects/{project}/releases` | `release.create` project; resolve actual tenant/artifact/target chain |
| Request deployment | DEP `POST /api/v1/environments/{environment}/deployments` | `deployment.request` environment; durable intent, 202 + operationId/status reference |
| Grant/reject/revoke approval | APR `/api/v1/deployments/{operation}/approvals` | `deployment.approve` exact environment plus policy; requester cannot self-approve when SoD applies |
| Read status | DEP `GET /api/v1/deployments/{operation}` | Scoped catalog-approved read permission and trusted resource owner; sanitized status/history |
| Request rollback | DEP `POST /api/v1/deployments/{operation}/rollback` | Separate `deployment.rollback`; a new operation, never blind retry |

Every mutation uses `Idempotency-Key` bound to authenticated subject, tenant, action, route resource and canonical **nonsecret** request identity. Same key/body returns the same persisted logical result; changed identity gives conflict. Retain dedup through the operation lifetime and recovery window; retention duration must be finalized with durable delivery adoption before purge. Auth/approval are re-evaluated for new effects; an old idempotent response cannot become a new dispatch permission. Proposed errors extend the existing safe error catalog without exposing foreign resource existence.

## Durable dispatch and independent worker

WFL owns step/process progress; DEP owns operation outcome; MSG owns durable transport/inbox/outbox mechanics. State change plus outgoing dispatch intent commit atomically; inbox dedup and result handling commit before ACK. Original command envelope carries existing message metadata plus operation ID, immutable intent hash, exact target/binding, authorization/approval references, deadline and epoch. No bearer session token is forwarded to a worker. Catalog exact event/command owner and consumers before publication; candidates in feature docs are not adopted topics.

Worker execution uses a separately authenticated service identity and a narrowly delegated operation authority; it must validate current action/resource/approval at admission and after delayed delivery. The operation cannot borrow the UI session or infer authority from an old queued request. This cross-process authority protocol, identities and effective-revocation semantics require a concrete approved milestone design before worker wiring; OP-P0's local lock proves none of them.

Target operations have one persisted lease owner/epoch and one writer mode. Epochs and intent must be checked by the executor and receipts; a stale worker cannot be declared fenced solely because the DB epoch increased. If the provider cannot reject stale external work, an expired lease **does not authorize a concurrent replacement writer**. Mark uncertainty, isolate/observe and reconcile the old execution first. Helm CLI exit or broker ACK is not provider fencing or deployment verification.

On timeout after possible dispatch, do not resend Execute or automatically Rollback. Observe the exact target/operation markers and journal; known not-started may be retried with the same immutable operation identity under fresh admission, known completed may proceed to verification, and unresolved remains Unknown for explicit recovery. Cancellation after possible external effects records cancellation intent and observed outcome; it cannot claim the effects disappeared.

## Verification, rollback and safety

OBS resolves current rollout/health evidence from the trusted target with timestamps, coverage, workload/image digest and health-policy revision. Artifact/runtime digest mismatch fails verification; stale/incomplete/unavailable observation is Unknown, not healthy. Allowed health probes require fixed target-bound destinations, TLS and finite deadlines; no arbitrary URLs/SSRF fallback. DEP alone records Verified when all policy requirements are evidenced.

Rollback compares current observed state with the expected operation, ensures controller ownership, validates prior pinned artifact/chart/config/secret-reference compatibility and rechecks permission/approval. Unsupported rollback is explicit. Database restore, arbitrary helm hooks and destructive production actions need separate approved procedure. Initial writes use only disposable/explicitly approved isolated targets. Agent enrollment is deferred when the worker can safely reach that target; later Agent identity/protocol adoption has its own gate.

## Conformance matrix and evidence

Required tests: tag substitution; cross-tenant artifact/target; changed canonical spec; requester self-approval; expiry/revoke before dispatch; duplicate request/delivery/result; state/outbox crash on both sides of commit; ACK loss; competing/stale executor; provider partition/unknown outcome; Mac close/worker restart; stale/foreign health observation; unsupported rollback; rollback conflict; forbidden credential/shell/secret output. Exercise real isolated durability and provider conformance at milestone gates. Fake driver success alone cannot pass OP-P1 or OSS Beta.

Additive v1 fields require old/new consumer fixtures; breaking wire/state changes need versioned rollout and migration. No production schema, operation, queue or target is changed by this design.
