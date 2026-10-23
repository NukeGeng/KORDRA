---
id: CTR-RESOURCE-POLICY-V1
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-22
---

# Resource Policy v1 — accepted bounded T06 design

**Accepted for the exact T06 scope on 2026-10-22, not implemented or verified by approval.** The maintainer approved this contract together with [ADR-0014](../decisions/0014-resource-policy-v1.md), as an addition to accepted [ADR-0012](../decisions/0012-operator-pilot-runtime-foundation.md) and [authority/admission v1](authority-admission-v1.md). Primary owner: [FEAT-IAM-001](../features/identity-access/features/authorize-resource.md); binding owner: [FEAT-PRJ-001](../features/project-environment/features/environment-catalog.md); target facts: [FEAT-INV-001](../features/infrastructure-inventory/features/register-target.md). It resolves the missing policy source, not the entire T06 implementation. Native model/effort for this authoring turn is unverified; this document is not evidence of an independent Sol review.

## 1. Scope, actors and decision predicate

Only authenticated User `target.read` at the exact project/environment scope is included. Existing IAM/TEN session, principal, membership, assignment, current RoleDefinition and provenance checks remain mandatory. Resource policy only restricts eligibility; it cannot create a permission, assignment, platform grant or provider capability.

Policy scope is not a new permission scope: `environment` requires the existing TEN grant at that exact environment ID; `project_without_environment` requires the existing TEN `project` grant at that project ID. Do not add `project_without_environment` to FoundationPermissions, inherit a project grant into an environment, or change the single-effective-assignment rule.

Permit requires all existing authority and ownership predicates, one current applicable policy binding, an active/effective current policy revision, a matching allow and no matching deny. Missing, ambiguous, stale, unknown or unavailable evidence never permits. Policy eligibility does not authorize catalog mutation, deployment, credential retrieval or production operations. Accepted `project.manage` / `target.manage` and other query callbacks keep their own contracts; do not run them through a fabricated `target.read` grant.

Policy provisioning is a trusted local-maintainer action, not an HTTP permission or a new role. It requires the existing installation identity, exclusive offline lease with API stopped, explicit approved manifest and owner handlers. No public policy API/UI, external policy engine, inheritance, arbitrary conditions, policy composition, authority cache or new service/database is introduced.

## 2. One canonical payload and applicability

Policy payload `schemaVersion: 1` has exact fields: `action`, `scopeKind`, `status`, `effectiveFromUtc`, nullable `expiresAtUtc`, and `rules`. `action` is exactly `target.read`; `scopeKind` is exactly `environment` or `project_without_environment`; `status` is exactly `active` or `inactive`. Tenant/project and policy identity are immutable owner metadata, not rules. Unknown or duplicate JSON properties, unrecognized enum values, extra fields and unsupported schema versions reject the manifest; malformed stored data denies evaluation.

Each rule has exactly `case` and `effect`. `effect` is `allow` or `deny`. An environment policy accepts cases `Development`, `Test`, `Staging`, `Production`; a project-without-environment policy accepts only `NoEnvironment`. Cases are exact ordinal values, never derived from names, labels or caller classification. No wildcard or implicit default is recognized. There is **no separate `allowed_classifications` field** and no extensible expression/operator bag.

Rules are an unordered set: sort by ordinal case then effect for canonical storage/hash; duplicate `(case,effect)` rejects, while allow and deny for the same case are both legal and deny wins. Require 1–8 rules for environment scope and 1–2 for project-without-environment scope; every rule must be valid even if it would not match this request. A deny-only policy is valid and allows nothing. No matching allow means deny. Unsupported conditions cannot be skipped as irrelevant rules.

Example of an explicit environment read constraint; identifiers and timestamps are supplied by the approved manifest/owner protocol, not inferred:

```json
{
  "schemaVersion": 1,
  "action": "target.read",
  "scopeKind": "environment",
  "status": "active",
  "effectiveFromUtc": "2026-10-22T00:00:00.000000Z",
  "expiresAtUtc": null,
  "rules": [
    {"case": "Development", "effect": "allow"},
    {"case": "Staging", "effect": "allow"},
    {"case": "Production", "effect": "deny"}
  ]
}
```

Applicability is exact, not a search for any permitting policy:

| Authoritative target selection | Required unique PRJ binding | Matching rule case |
|---|---|---|
| PRJ and INV agree on a concrete active environment | Same tenant/project, `environment`, exact environment ID | Current PRJ classification |
| PRJ and INV affirm resolved environment absence | Same tenant/project, `project_without_environment`, null environment ID | `NoEnvironment` |
| Missing/conflicting owner facts or failed environment lookup | None is acceptable | Deny |

There is no fallback from environment to project binding. For a legitimate environment-free target, classification is absent rather than Development: only `NoEnvironment` can match. For an environment-bound target, policy scope, exact binding and current owner classification must all agree. Exactly one current binding is required for `(tenant,project,scope,environment-selection,action)`; history is not another candidate. A shared policy may serve several environment bindings in its immutable tenant/project, but never another tenant/project or both scope kinds.

## 3. Persistence and constraints

Use the already accepted EF/Npgsql/PostgreSQL stack, schema `app`, existing owner contexts and migration histories. UUID identities are server-generated; revisions are positive signed 64-bit integers, checked for overflow. No live database/schema change is authorized by this design document.

### IAM-owned records

| Table | Columns and constraints |
|---|---|
| `iam_resource_policies` | `policy_id` UUID PK; nonempty `tenant_id`, `project_id`; immutable owner identity; unique `(tenant_id,project_id,policy_id)` |
| `iam_resource_policy_revisions` | PK `(policy_id,revision)`; FK `policy_id` to IAM policy identity; payload fields from section 2 except rules, `content_hash` exactly 32 bytes, `published_at_utc`; check schema/action/scope/status, positive revision, finite UTC times and `expires_at_utc > effective_from_utc` when present |
| `iam_resource_policy_rules` | PK `(policy_id,revision,case,effect)`; FK to exact IAM revision; fixed case/effect checks; owner validates scope/case combination and bounded complete rule count before publish and after load |
| `iam_resource_policy_heads` | `policy_id` PK; `current_revision > 0`; FK `(policy_id,current_revision)` to immutable revision; optimistic expected-current concurrency predicate |
| `iam_resource_policy_commands` | PK `(installation_id,manifest_id)`; canonical manifest hash, durable result references/revisions, canonical audit intent ID/hash; one immutable receipt per command |

Separate identity/revision/head records avoid circular required foreign keys: insert identity, revision/rules, then head in one transaction. There is no `MAX(revision)` reader or draft revision that becomes active through discovery. An identity without an exact head/revision/complete rule set is unusable. No cross-owner TEN/PRJ FK or SQL is added to IAM; trusted PRJ owner ports verify project identity before publication.

Revisions/rules/command receipts are append-only for application/provisioning handlers; no handler edits or deletes old content. The offline migration grants runtime SELECT only on the new IAM policy tables, explicitly revoking INSERT/UPDATE/DELETE/TRUNCATE inherited from generic future-table defaults. T06 v1 exposes no online policy writer. This is application/least-privilege enforcement against runtime callers, not tamper-proof storage against the trusted database owner. Any later online policy-write feature requires its own permission, grants, handler and audit design. Keep the existing AUD append-only grants. `content_hash` is SHA-256 of the canonical payload in section 2, with sorted rules and the timestamp encoding in section 6; identity/revision are separately bound into the command and U/D digests.

### PRJ-owned binding and history

`prj_policy_bindings` stores `binding_id` UUID PK; `tenant_id`, `project_id`, `action`, `scope_kind`, nullable `environment_id`, `policy_id`, `policy_revision`, and positive `binding_revision`. IDs/owner scope remain immutable; only a controlled rebind changes policy reference and increments binding revision. There is no delete/recreate reset of binding identity or revision.

Enforce a PRJ-local FK to `(tenant_id,project_id)` and, when present, `(tenant_id,project_id,environment_id)`. Check `action = target.read`, positive policy/binding revisions and that environment scope requires a non-null ID while project-without-environment requires null. Use **two partial unique indexes**: `(tenant_id,project_id,environment_id,action)` for environment scope, and `(tenant_id,project_id,action)` for project-without-environment. Ordinary nullable uniqueness alone is insufficient. Reject duplicate candidates defensively at read time even with those constraints.

`prj_policy_binding_history` is append-only, PK `(binding_id,binding_revision)`, storing exact binding snapshots, command identity and content hash in the same owner transaction. History is retained, not queried to select current authority. No FK from PRJ to IAM tables: IAM validates exact policy identity/current revision through its port. Runtime receives SELECT/INSERT on these two PRJ tables for the owner-controlled initial binding insertion described in section 8, explicitly revoking UPDATE/DELETE/TRUNCATE; replacement of an existing binding is offline-only. Infrastructure migrations explicitly preserve that least-privilege boundary. Each history row has a PRJ-local FK to its binding identity and the same immutable scope; it cannot exist for another binding owner.

PRJ's existing `EnvironmentRecord.PolicyRevision` becomes **legacy non-authoritative data**. Preserve every existing value; make the legacy column/property nullable with a check permitting null or a positive historical value. New explicit-binding environments leave it null rather than copying a second authority value or inventing a sentinel. The new binding is the sole current policy reference; PRJ owner DTOs expose it explicitly. Existing environment and target ownership revisions still participate in U/D alongside the separate binding revision. This compatible nullability migration must be covered by upgrade tests; do not leave a mandatory legacy column that prevents the new create handler from saving.

## 4. Current revision, publication and time

Creation allocates a policy ID once and publishes revision 1; subsequent publication requires exact expected head `n` and appends `n+1`, then advances the head in the same transaction. Policy tenant/project identity is immutable. Status, rules, scope or validity changes create a new revision, including disable, re-enable or change-and-change-back. A head never points back to a historical revision; exhaustion/overflow rejects rather than resetting it. A binding must reference exactly the current head: merely existing or historically valid is insufficient.

Effective time is inclusive at `effectiveFromUtc`, exclusive at `expiresAtUtc`. A publish command contains a concrete UTC effective time; there is no retry-dependent server default in its hash. Use the existing trusted nondecreasing UTC and clock-floor rules. Reject future-dated publication (`effectiveFromUtc > trusted publication time`); active publication must not already be expired at final commit validation. There is no scheduled activation/draft mechanism. Malformed/future-effective stored revisions nevertheless deny if encountered; later time cannot substitute for missing publication evidence. Inactive revisions always deny.

An exact successful command replay returns its historical result after verifying offline installation custody and the receipt hash, even if the current head/bindings have changed or the old payload has since expired. It neither republishes nor reauthorizes use. A fresh operation must satisfy current expected revisions, validity and explicit approval. Never run mutable current-state validation before looking up a trusted exact completed command receipt.

## 5. Owner ports and consumer direction

All results are BCL-only immutable owner DTOs, not caller permits. They require the caller to hold the existing shared admission lease; they do not recursively acquire it. Missing versus unavailable must remain distinguishable internally; neither permits. No port exposes another owner's DbContext, connection or table name.

| Producer / port responsibility | Exact result or input | Consumers |
|---|---|---|
| PRJ / resolve current policy binding | Explicit environment selection, binding ID/revision, tenant/project/environment, action, policy ID and exact policy revision | Host composition and IAM evaluator |
| IAM / read exact current resource policy | Tenant/project/policy ID and requested revision; verify identity/head/revision/rules/hash and return complete immutable snapshot with validity | IAM evaluator and offline binding validation |
| IAM / evaluate resource eligibility | Exact PRJ binding plus current owner classification/absence, action and trusted time | T06 U/D coordinator; cannot return grant authority |
| IAM / stage offline policy publication | Approved typed manifest, expected head and validated owner references; result contains staged policy ID/revision/hash | Host offline transaction composition |
| PRJ / stage exact bindings | Approved explicit selection and expected binding revisions, IAM-validated exact policy result | Host offline transaction composition |
| AUD / stage and confirm append | Existing mutation/U/D intent with canonical binding digest; no payload or credentials | Existing transaction/audit ports |

IAM evaluation reports explicit `allow`, `deny` or `unavailable`, safe reason, exact policy ID/revision/hash, binding ID/revision and validity. A generic `Complete=true` or a positive revision number is not evidence of allow: the trusted evaluator must validate the complete fixed schema and a matching allow with deny precedence. No positive production default or fixture allowlist is registered.

Host consumes IAM and PRJ Contracts, resolves their snapshots, and passes neutral selections to IAM's evaluator. IAM runtime does not reference PRJ runtime; PRJ runtime does not reference IAM runtime. Prefer producer-owned DTOs mapped by Host over a circular Contracts graph. Reuse existing AUD transaction infrastructure for enlistment; a new orchestration service is unnecessary.

## 6. Offline manifest, transaction and recovery

The typed manifest has exactly these fields; unrecognized fields reject:

| Field | Exact meaning |
|---|---|
| `schemaVersion`, `manifestId`, `installationId` | Version 1, nonempty command UUID, exact trusted installation UUID |
| `databaseIdentity` | `{databaseName, provisioningRole}` checked against the trusted selected target; not a connection string, host override or proof of custody |
| `tenantId`, `projectId` | Same nonempty UUID owner selections for this whole command, verified through owner ports |
| `operation` | `create`, `publish`, or `bind` |
| `policyId`, `expectedCurrentRevision` | For create: null/0; for publish or bind: existing UUID/positive expected IAM head |
| `payload` | Exact section 2 payload for create/publish; null for bind |
| `bindings` | Explicit array, 0–50 entries for create/publish and 1–50 for bind; each entry is exactly `{bindingId, scopeKind, environmentId, expectedBindingRevision}` |

Binding creation requires null bindingId and expected revision 0; update requires the exact existing binding ID and positive expected revision. Scope/environment shape follows section 3. All entries point to this command's policy (newly published revision or the exact current head for bind); no wildcard or independently selected policy per entry exists. Reject duplicate scope/environment selectors and duplicate non-null binding IDs. `bind` changes no IAM head/content and allows a controlled association to an already published current policy; it cannot revive an expired/inactive policy. `create` allocates owner IDs once; retry resolves the persisted result instead of generating new IDs. An existing policy identity cannot be silently moved between tenants/projects. IDs and expected revisions in this offline manifest are selections/preconditions, not authority from an HTTP caller.

The existing offline local-maintainer custody is required on every invocation; do not invent a logged-in user, tenant grant or role name for provisioning. Retain approval/manifest hash and installation-bound operator evidence privately. Policy/binding mutation AUD records carry verified tenant/project/resource references and the existing offline actor convention; no tenant-visible credential or OS account secret is stored.

Canonical manifest bytes are UTF-8 compact JSON with property order exactly as the field table above (schemaVersion, manifestId, installationId separately), nested databaseName then provisioningRole, payload fields in section 2 order, and binding fields in their declared order. Use lowercase hyphenated UUIDs, decimal integers without alternate numeric forms, exact ordinal enums and six-digit-fraction UTC timestamps ending Z at PostgreSQL microsecond precision. Sort rules by ordinal case then effect; sort bindings by ordinal scopeKind then environment UUID, with null before UUID. Reject sub-microsecond input instead of losing information on storage. Null is explicit, not omitted. SHA-256 covers every declared field; transport paths/private approval locators are outside this payload. Validate supplied approval against this exact hash. Publish golden canonicalization vectors before implementation completion; never hash noncanonical reserialized arbitrary JSON.

Execution order:

1. Validate shape/size (128 KiB maximum), installation/database identity, approved manifest hash and held exclusive offline lease with API stopped. Acquire the same technical writer gate in the offline composition; absence of the OS lease cannot be replaced by that process-local gate.
2. Look up `(installationId,manifestId)` through IAM's command-receipt handler. Exact hash replay returns the committed result after checking its linked durable AUD receipt; changed hash conflicts. Missing/contradictory audit evidence is unknown and blocks mutation, not permission to replay. A receipt lookup does not make protected data available.
3. Open one transaction using existing technical enlistment infrastructure. IAM/PRJ handlers validate current policy head and exact expected bindings; owner ports validate active tenant/project/environment and classification/absence without cross-owner SQL. The common offline lease excludes other conforming processes. Do not consume a target lease or call a provider.
4. For create/publish stage the immutable IAM revision/rules and head; bind leaves that head unchanged. Stage selected PRJ binding updates/history, exact command receipt and one durable AUD mutation intent bound to the canonical manifest/result digest. Persist the IAM trusted clock floor through its owner handler in this same audited transaction, including a bind-only command. All owned writes enlist in the **same connection/transaction**; no independent SaveChanges commit. For creation IDs are included in the persisted result, not regenerated on successful retry.
5. Before commit recheck offline custody, trusted time and current preconditions. Every changed binding increments its binding revision; an explicitly targeted binding pointing to an unchanged head may still increment its revision to record the approved rebind. Keep PRJ target/environment ownership revisions separate; do not increment INV state when target ownership did not change.
6. Confirm state plus AUD commit before returning success. Known setup/rollback failures are unavailable without poisoning the shared gate. A genuinely unresolved commit closes new admission, returns unknown and is reconciled by exact command/AUD lookup under renewed offline custody; never blindly repeat publication or manufacture a success receipt. Unresolved commit work must unwind before releasing its fence, following admission v1.

Publishing a shared policy **does not auto-upgrade every binding**. Only listed bindings move; all unlisted bindings pinned to an older head become deny immediately at the atomic commit. The maintainer must see that impact before approving the manifest; a dry-run reports affected binding counts/references through PRJ, privately, without writes or an approval receipt. Limit returned references to 50 with total/truncation indication; more detail requires another bounded inspection, not a hidden unbounded payload. Publishing a revision with an explicit empty binding list can intentionally disable every old binding. There is no list wildcard. Successful results contain the exact policy ID/revision and each selected binding ID/revision; invalid/conflict/denied/unavailable/unknown never masquerade as success. Multi-policy atomic batches, online replacement and automated scheduling are out of scope; independent manifests have independent commits.

## 7. U/D evidence and writer discipline

At each U/D boundary resolve current subject/TEN grant/RoleDefinition, PRJ+INV ownership, the unique binding and the current IAM policy. Bound facts include policy ID/revision/content hash, binding ID/revision, exact action, tenant/project/environment selection/classification, target/association/owner revisions and the existing subject/session/grant provenance binding. Earliest expiry includes policy expiry and all existing authority expiries. No new authority cache, grace interval or shortened comparison is allowed.

U intent commits before synchronous typed adapter admission under the read gate. D reacquires the gate, evaluates current policy and compares the whole canonical binding to U; any mismatch discards the private result even if today's policy would independently allow a fresh request. Change-and-change-back cannot match because policy/binding revisions never reset. There is no automatic retry. D intent is distinct and result-digest/size-bound; the existing caller-bound HTTP handoff, bounded flush/abort, writer preference and numeric limits remain unchanged. Audit commit alone is not D.

Use the existing AUD intent schemaVersion 1 and its nonsecret canonical binding digest to include the new facts; keep the IAM RoleDefinition revision and resource-policy revision distinct in the digest. Encode the SHA-256 in the existing bounded SafeReason convention, without a second payload store; for offline publication use the technical audit label `iam.resource-policy` (not a permission grant), project scope and verified resource references. Do not reinterpret or overwrite historical `policyRevision` values in old AUD records. No new event/broker or public audit query is added. If implementation discovers the existing AUD DTO cannot carry the agreed digest/result binding, stop for an explicit additive compatibility amendment rather than silently changing audit semantics.

The T06 writer registry must identify policy-head publication/disable, binding change, and all existing invalidating writers. V1 policy/binding writes are offline-only and require API shutdown; tests still prove held-gate mutations and U/D invalidation using instrumented owner handlers. Such tests do not authorize an online policy endpoint. No authority writer may bypass the installation lease or the admission gate.

## 8. Compatibility, rollout and rollback

This is the accepted resource-policy supplement, not a change in the existing admission v1 protocol. Implementation must add IAM policy tables and PRJ binding/history tables through separate owner migrations, but ship their consuming composition together behind closed positive ingress. No new package, runtime architecture or modification of completed T01–T05 checkpoints is needed.

Affected existing consumers and required migration:

| Existing shape / consumer | Required controlled transition |
|---|---|
| `EnvironmentRecord.PolicyRevision`, `CatalogCommand`/`CatalogFact` and `ProjectOwnership` in PRJ Contracts | Preserve old stored data/history; add explicit typed policy binding result for the new path. Old integer-only association is not usable authority. Do not map it to a guessed policy ID. |
| `IEnvironmentPolicyReader.IsApplicableAsync(..., classification, policyRevision)` and `ProjectCatalogStore` / `CatalogMutationCoordinator` | Replace new-path validation with exact policy ID/current revision/scope owner validation. Update all composition and fixtures together; legacy shape has no positive runtime fallback. T06 environment creation uses an explicit typed policy reference and PRJ binding handler in its audited transaction. |
| `TargetAuthority`, `ResourcePolicyAuthority`, `IResourcePolicyReader` and `ProtectedReadAdmission` (unfinished local T06) | Carry exact policy and binding identity/revision/hash, matching allow result and complete facts; remove acceptance based only on booleans/revision. Update U/D digest and all fixtures before registration. These files are not proof that the new contract is implemented. |
| `AuthorityCommitment`, existing AUD canonicalization and output proof | Bind the extra facts in the agreed digest without modifying historical receipts or treating an old U intent as D. |
| ProjectEnvironment, IdentityAccess and architecture tests; Host/DbMigrator composition | Update all actual callers, migration/model/grant assertions and negative architecture checks in the owning slices. No cross-owner runtime project reference or direct SQL shortcut. |
| Proposed client handoff / future wire environment-create shape | Use POST `/api/v2/projects/{project}/environments` with the exact versioned request below. Update canonical Product fixtures before endpoint enablement. Keep the old integer-only create route unregistered; no active positive client exists to silently break. Other adopted v1 routes are unaffected. Never write Clients source from Product. |

The affected create request has exactly `{schemaVersion:2, correlationId:<UUID>, name:<1..128 characters>, classification:<classification enum>, resourcePolicy:{policyId:<UUID>, revision:<positive int64>}}`. Reject unknown/duplicate fields, other schema versions and authoritative tenant/creator/binding revision input. Retain accepted same-origin/CSRF, Idempotency-Key and management-permission checks. On success return 201 with schemaVersion 2/correlationId, server-generated environment ID/project reference/name/classification/status/revision, and exact `policyBinding` identity/revision/policy ID/policy revision; ETag represents the environment revision. Errors carry only safe machine status/correlation, never policy content or foreign-resource existence; use the established auth/forbidden/invalid/conflict/unavailable categories. Its full synthetic success/error fixtures and OpenAPI must agree with the implemented owner DTOs before registration. This versioned change is accepted only for this T06 supplement; the historical v1 request remains preserved and must not receive a positive compatibility fallback.

For online environment/target catalog setup, the approved policy must already exist in IAM and match the current head; ordinary catalog permissions cannot publish/advance policy heads. Creation may add its explicit PRJ binding under existing catalog management authority, shared writer gate and state+AUD transaction. Persist the trusted IAM clock floor in that audited transaction through the IAM owner, not cross-owner SQL. Offline-only in sections 3/6 refers to publishing policies and replacing existing bindings; this initial owner-controlled binding insertion must be enumerated separately in the writer registry. Existing environment classification/policy replacement remains rejected online. Validate the active/effective current policy's owner, complete schema and scope applicability for the declared classification/absence; do not require a read-allow outcome to authorize catalog creation. An explicit deny or no matching read-allow can intentionally block later reads without removing the catalog object. No synthetic allowlist or catalog-to-read permission promotion is permitted.

Upgrade sequence: migrate additive tables offline; keep old environments/targets intact; use a separately approved explicit manifest to establish real bindings; validate exact row identities/grants/current policies; run U/D/HTTP proofs; only then register the adopted positive read path. No automatic Allow backfill, `MAX(revision)` lookup, default policy or broad test-data migration. Existing integer-only catalog creation fixtures remain historical tests until updated to explicit references; they cannot be used as positive-runtime acceptance evidence.

Rollback disables positive ingress and leaves additive tables, receipts and AUD history intact. Do not drop policy history, rewind a head/binding revision, restore an old grant or reset checkpoints. Recovery of expired/inactive/incorrect policies is a new approved offline revision/binding transaction, not a live SQL edit. No real `kordra` migration/provisioning is authorized by design approval; isolated synthetic `kordra_test` implementation tests retain their existing authorization boundary.

## 9. Required acceptance evidence

These are planned checks, not passing results. Count actual adapter calls and HTTP protected output, not just return status. Reuse existing valid proof for unchanged code; add targeted cases for the changed contracts.

| Cases | Required assertion |
|---|---|
| Exact IAM/TEN grant + matching resource allow; missing grant + allow; grant + matching deny; deny-only/no matching allow | Only the first permits; policy cannot enlarge grant authority |
| Wrong tenant/project/environment/action; missing or duplicate binding; inactive parents; owner conflict | No U admission or protected output; DB constraints and owner reads agree |
| PRJ/INV resolved absence + explicit `NoEnvironment` allow; lookup error; environment-only rule; fallback candidate | Only the first can permit; no fake classification/fallback |
| Head missing; nonexistent/noncurrent revision; future/inactive/expired policy; invalid/unknown/duplicate payload fields or rules | Deny; reject malformed publication; preserve exclusive expiry at U and D |
| Active revision expires after U audit or after D audit; trusted-clock rollback/restart | No late admission; preserve clock-floor and gate-close semantics |
| Shared head update with selected bindings and with an explicit empty list | Only listed bindings move; unlisted stale bindings deny; current head comes from IAM head, not highest stored row |
| Policy or binding change and change-back between U/D; different policy with same revision; different binding with same content | Old private result discarded with zero protected output; full identity/revision binding required |
| Writer queued before D; D held before writer; real HTTP partial failure/cancellation and deadline | Preserve accepted ordering, bounded completion and no retry/early output |
| Failure after IAM save/before PRJ save, after PRJ save/before AUD commit, known rollback, ambiguous commit/ACK loss | No partial owner activation; unresolved commit fences admission; exact receipt reconciliation never repeats side effects |
| Exact completed manifest replay after later policy expiry/head change; changed payload/subject installation/expected binding | Historical result only for exact trusted replay; changed hash/installation conflicts or rejects; no regrant |
| Concurrent head/binding expected-revision mismatch; revision overflow; duplicate manifest operations | One permitted commit or safe conflict, never duplicate IDs/history/audit or revision reset |
| PostgreSQL nullable-scope uniqueness/FKs, immutable policy/runtime grants and additive migration over old T05 records | Invalid links reject; runtime cannot edit policy; all old data/history retained but unresolved policy denies |
| Canonicalization golden vectors and malformed stored hash/rules | Stable SHA-256 across parser/storage; corruption denies |
| Source boundaries / production DI / writer registry | No cross-owner SQL/context, circular runtime reference, production fixture policy/provider, or unregistered invalidating writer |

Run targeted unit/integration/actual HTTP tests in the owner slices, then required Product gates at the implementation/PR boundary. Document deterministic evidence separately from design approval, native Codex PR review and human merge. No extra AI review is required to finalize this proposal.

## 10. Bounded Developer handoff and approval boundary

The maintainer accepted ADR-0014 and this contract on 2026-10-22. The Developer may implement the following approved slices:

1. IAM slice: schema/head/rules/receipt, canonicalization and evaluator/owner-port tests using existing dependencies. Preserve T03 clock/session and T04 RoleDefinition behavior.
2. PRJ slice: unique bindings/history and explicit-reference Contracts, additive compatibility tests and owner validation. Preserve T05 resources and both-owner target association.
3. Host offline slice: installation-bound manifest and same-transaction IAM/PRJ/AUD composition, dry-run impact, replay/unknown/rollback evidence; no live provisioning or public policy-write route.
4. T06 integration: exact current owner facts and policy/binding in U/D, writer registry, actual caller-bound HTTP proof, updated versioned Product fixtures, then applicable gates. Keep positive ingress closed until evidence passes; T07 fake provider remains test-only.

This is the end of the bounded design task. The recorded approval adopts the design and synthetic-test implementation scope, not completed code, native model selection, test execution, provider access, production readiness, PR review or merge. Preserve existing completed checkpoints and historical approval hashes; no replay or new continuous Sol GOAL loop.
