---
id: FEAT-ART-001
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
module_id: MOD-ART
feature_id: FEAT-ART-001
phase: MVP
source_paths: ["src/Modules/ArtifactManagement/Domain/", "Kordra.slnx", "global.json", ".github/workflows/product-ci.yml"]
test_paths: ["tests/ArtifactManagement.Domain.Tests/"]
---

# FEAT-ART-001 — Discover and pin immutable deployable artifacts

## User story / outcome
**Actor:** Developer / CI Provider. **Goal:** Discover and pin immutable deployable artifacts. **Primary owner:** `MOD-ART`. **Phase:** MVP.

## Pre-conditions
Project and registry reference configured; artifact metadata passes validation.

### First bounded increment — I1, local digest validation
Owner module `MOD-ART`; implementation custodian engineering-maintainers pending assignment of the product owner. Goal mapping: GOAL outcome 2 → roadmap Foundation F0-ART-I1 → UC-002 → FEAT-ART-001/I1. This is a validation increment under [ADR-0008](../../../decisions/0008-domain-bootstrap-validation.md), not adoption of a production architecture. Existing module/feature status stays proposed; complete registry-backed registration is not delivered by I1.

I1 actor: local module caller (future Developer/CI application handler). Scope: pure artifact content identity, no tenant data access. Permission: none for a pure value operation; future registration requires `artifact.register` on a trusted resource scope. Source: module-local domain library only; tests: isolated test project below. No side effects, credentials, domain messages or shared kernel.

## Main flow
Receive signed provider signal → resolve OCI digest → validate registry/platform/policy → create artifact record with source revision → notify release catalog.

## Alternatives / exceptional flow
Tag points to a new digest: register distinct artifact, never mutate prior approved identity.

## Messages and integration contracts
`artifact.discovered.v1; artifact.registered.v1`. Message names are candidates; verify owner/consumer in [message catalog](../../../contracts/message-catalog.md) before code. Stable envelope, retry and idempotency obey [messaging architecture](../../../architecture/messaging.md).

## Authorization / tenant-scope review
- Principal verified, permission key from [catalog](../../../contracts/permissions.md) and resource owner resolved from trusted store.
- The `organization_id`/scope is validated on read, write, event handler and any background job.
- Audit sensitive decisions with `actorRef`, `resourceRef`, `correlationId`, policy version.

## Negative & recovery scenarios
Digest mismatch, unsigned webhook, nonexistent registry digest, unsupported architecture.
- Generic tests: duplicate request/message, authorization revoked mid-flight, resource from another tenant, crash/restart, stale state/event, partial external failure where applicable.

I1 tests reject null/empty, tags, whitespace, uppercase algorithm/hex, wrong digest length, unsupported algorithms, extra separators, invalid ASCII/Unicode and embedded newlines. Content mismatch must return false, including one-byte changes and wrong length. Equivalent values compare/hash consistently; values cannot be mutated or created through public constructors. Repeated and concurrent verification leaves identity unchanged. Verification errors never include caller-provided text in exception messages.

Wrong tenant, missing permission, expired approval, duplicate delivery, worker crash and unknown agent outcome are **not applicable to this pure value operation**: there is no subject/resource lookup, approval, delivery, worker, persistence or dispatch. They remain mandatory for later registration/release increments; no I1 result proves those properties.

## Acceptance criteria
Approved artifact hash remains unchanged from release through operation; mutation produces new identity.
- No hard-coded role name; can add role templates without rewriting handler authorization.
- Linked tests and source commit evidenced before marking `implemented` / `verified`.

I1 acceptance: Given a supported lowercase OCI digest, parsing preserves its identity exactly; unsupported/invalid input cannot construct a value. Given the original raw bytes and declared byte count, verification succeeds only if size and hash match. Changing content produces a different identity and cannot modify an existing object. `TryParse` is nonthrowing for invalid strings and clears its output. `Parse` throws a safe argument/format error without echoing untrusted input. SHA-256 and SHA-512 are supported; other algorithms fail closed with no substitution. Verification does not imply provenance or registry existence. [Local contract v1](../../../contracts/artifact-identity.md).

## Proposed technical tasks (not implementation evidence)
- Define command/query/API shape; validate domain invariants in owner module.
- Implement contract integration + tenant-safe query; write unit, contract, integration and failure tests.
- Add tracing/audit and update technical-debt / runbook if unresolved risk.

## Source & test traceability
- I1 source/test paths above exist in this branch. The first increment is implemented and locally validated; the feature remains proposed/partial because its registration/integration flow and architecture acceptance are incomplete.
- Actual local validation: .NET SDK 10.0.400; locked restore succeeded; Release build produced zero warnings/errors; `dotnet test Kordra.slnx --configuration Release --no-build --no-restore --logger trx` passed **48/48**, zero skipped; `dotnet format Kordra.slnx --verify-no-changes --no-restore` passed.
- Existing AMR gates passed strict documentation validation, 39 offline LAB registry cases, **47 Python tests**, locked product restore, product build and all 48 product tests with a stable source identity. These are local checks, not infrastructure/registry/tenant-isolation evidence.
- Independent native review: `codex exec ... review --uncommitted` completed with exit 0 and a terminal `turn.completed` event, reporting no actionable defects. Full write-producing Python/.NET checks were blocked in the reviewer's read-only sandbox; the separate AMR gate run executed them. Raw receipts remain private under `.amr-local/artifact-native-review*`; no provider automatic review is inferred.
- I1 implementation and regression tests are committed in [b20090b186c5c5a7b76622df5bb6b877675e32eb](https://github.com/NukeGeng/KORDRA/commit/b20090b186c5c5a7b76622df5bb6b877675e32eb). Current head and remote CI must be checked in the PR/Git history for branch `feat/artifact-identity-foundation`; no unobserved CI outcome is claimed by this note. No registry, tenant-policy, durable messaging or driver integration is delivered.
- Open decisions: `[NEEDS-DECISION]` ADR-0008 acceptance; product module owner; ADR-0001–0007 and registration dependencies remain proposed.
