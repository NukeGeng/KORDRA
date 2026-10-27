---
id: ADR-0005
status: accepted
owner: NukeGeng
last_reviewed: 2026-10-27
---

# ADR-0005 — Immutable release intent and scoped approval, T11

**Accepted exact T11 decision by NukeGeng on 2026-10-27; implementation remains pending.** This expands the unaccepted 2026-10-15 freeze-spec sketch, preserving its goal of preventing artifact/spec substitution. No accepted ADR or implemented behavior is replaced. Normative rules are in [Release and approval v1](../contracts/release-approval-v1.md); [release/execution v1](../contracts/release-execution-v1.md) remains a later worker/deployment proposal.

## Baseline and problem

Main `bc91c2a66abcdb2156363f5fdbd3dc7f89046e67` contains owner-merged [PR #19](https://github.com/NukeGeng/KORDRA/pull/19), whose head `5969786f910081fa4df2c0ce808f5e8c551b7b88` passed docs-integrity/artifact-domain/clients-pin and one automatic Codex review with no findings. That proves T10's checked source, not T11 or live provider qualification. REL/APR folders currently contain only reserved READMEs. Existing grants do not recognize release actions; Resource Policy v1 applies only to target.read.

The older cross-owner proposal mixed a release review with future driver/configuration/health and execution approval that are not adopted. Freezing missing fields as valid defaults would allow later substitution. ART proves bounded manifest/config hashes, with unverified layers/signatures/manual provenance. T11 must produce a useful immutable release/approval queue without misrepresenting either fact.

## Alternatives

| Option | Assessment |
|---|---|
| Approve a tag or mutable configuration and resolve later | Reject: approved content can change. |
| Call a T11 grant deployment approval while leaving future execution fields null | Reject: hidden fields could be added without the human decision covering them. |
| Adopt all worker/provider/health contracts now | Defer to T12/T13; not necessary for a durable review queue and would silently expand unapproved architecture. |
| Defer all release/approval work until a real registry/driver exists | Reject for the pilot: prevents testing useful owner-scoped intent and approval behavior. |
| Freeze a closed release-intent-v1, distinguish it from execution authority and gate later execution separately | Selected proposal; explicit product limitation, not a claim that approval executes deployment. |
| Introduce new event store, workflow service or policy engine | Defer: reuse the proven local foundation and exact existing dependencies for this owner scope. |

## Proposed decisions for exact acceptance

1. Adopt only the linked contract's T11.0–T11.3: FEAT-APR-002 offline approval-policy foundation, FEAT-REL-001 draft/freeze/retire and FEAT-APR-001 approval queue/decisions. Proposed custodian for these slices and shared owner seams is NukeGeng; existing module ownership beyond them is unchanged.
2. Freeze server-resolved tenant/project/application/environment/registered target, immutable ART facts, exact APR policy/binding and pilot qualification. Store exact recursively ordinal canonical UTF-8 bytes plus domain-separated SHA-256; no mutable tags, client authority/hash, borrowed provenance or unreviewed configuration bag.
3. Adopt release-intent-v1 as the approval subject and the exact release.* permissions/scopes in the contract. T11 grants expose executionAuthorized=false and cannot satisfy deployment.approve. Execution driver/remote identity/configuration/secret/health fields require a separately versioned plan and new approval under later adopted contracts. No worker/driver/message/dispatch is added.
4. The first qualification is explicit Development/Test pilot use of T10's manifest/config hash evidence and unattested provenance only under a permitting policy. Staging/Production planning drafts are possible; freezing/approval requires future qualification. Do not relabel this as signed provenance, pull verification or production permission.
5. APR owns a separate versioned business policy and exact environment bindings. It restricts IAM/TEN permission, not creates authority. Keep Resource Policy v1 target.read and PRJ's existing read bindings unchanged. Policy publication is stopped, hash-approved, audited and idempotent with no automatic default/backfill.
6. Default policy recommendation is independent requester/approver, pending lifetime 86,400 seconds and grant lifetime 1,800 seconds, but all values must be explicit. self_allowed is an explicit Development/Test-only alternative, requires current distinct request/approve capabilities even for one principal, and cannot bypass organizational production approval. Grant is capped by original pending/assignment/policy expiry and the next fixed UTC blocked window. No quorum, recurrence or emergency override in v1.
7. Release revisions transition Draft → Frozen → Retired; exact CAS protects edits. Existing Frozen bytes never change. Each exact release revision has at most one approval request ever; terminal rejection/revocation/expiry/invalidation requires a new reviewed revision for another request. New revisions never silently redirect or inherit prior approval. Explicit retirement/revocation handles obsolete revisions.
8. Approval decisions are immutable append-only facts. The recorded head and fresh effective state are distinct; expiry/current owner, role, membership, grant, policy or retirement changes invalidate eligibility without pretending a scheduler recorded a new decision. Original requester/approver sessions are required for their HTTP actions, not durable worker tokens. Logout/restart does not erase decisions; current persisted authority and original deadlines remain required.
9. Extend the accepted one-process gate, shared capacity, owner ports, trusted clock/floor, enlisted AUD and caller-bound D handoff. Every mutation/receipt/decision/floor/audit commits once on the same connection. Pre-commit failure does not poison the host; actual unknown commit fences, followed only by stopped exact-receipt reconciliation. No automatic replay, rollback-to-old-head or new grant in recovery.
10. For REL/APR only, reuse locked EF Core 10.0.11 and Npgsql EF 10.0.3 in new owner runtime projects; pure Domain and BCL-only Contracts. Add additive app.rel_* / app.apr_* migrations, guarded runtime/admin/test locators and least-privilege SQL protections. No startup DDL, new package/version, Marten, Wolverine, service, frontend work or accepted source-layout refactor. Host composes neutral owner Contracts rather than REL/APR runtime cycles or target.read permits.
11. Deliver complete owner features in dependency order: APR policy → REL → APR decisions; each has source/tests/docs/gates, feature commit/PR, default Codex Auto PR Review and human merge. No repeated Sol GOAL/source review. Design approval itself executes no schema, policy provisioning, target, secret or production action.

## Migration, compatibility and rollback

The linked contract specifies exact owner tables, locators, wire/ETag/error rules, retention and staged implementation. Test additive migrations, SQL/tenant constraints, nonempty existing owner data, partial failure and non-destructive rollback in isolated kordra_test. Existing T01–T10 APIs, source paths, ART hashes and credential behavior remain compatible. Disable new routes on rollback; preserve immutable subject/decision/policy/audit history and monotonic heads/floor. No real installation DDL or privilege change is authorized by design acceptance.

The older module references and release/execution sketch are updated to link to the exact proposal, not silently accepted. Future deployment/request/override/message/worker decisions stay proposed. Missing future execution prerequisites cannot be implemented as an AlwaysAllow adapter or inherited T11 permit.

## Acceptance and residuals

NukeGeng accepted ADR-0005, Release and approval v1 and the three owner features for exact T11.0–T11.3 scope, explicitly including the release-intent/execution distinction, pilot-only qualification, scoped permissions, SoD/TTL/windows and persistence/admission choices. Frozen pre-annotation hashes and approval are recorded below.

Implementation completion requires R11-01–16 in the contract: independent canonical vectors; current owner/tenant/permission cases; exact preview/CAS; SoD/expiry/revocation; concurrent decisions/retirement; true commit ACK loss; stopped recovery; actual HTTPS; isolated migrations/ACLs; unchanged T01–T10; deterministic gates and hosted review. None has run for T11 yet.

Residuals remain explicit: trusted local maintainer/DBA/OS, one API process, bounded non-retractable response handoff, historical registry verification, unverified provenance/layers/signatures, no execution plan/dispatch and no distributed revocation guarantee. A different rollout topology or real infrastructure use needs later design and operational authority.

## Exact human approval — 2026-10-27

NukeGeng approved ADR-0005, Release and approval v1 and FEAT-APR-002/REL-001/APR-001 for T11.0–T11.3: “ok duyệt, thực hiện T11 với terra xhigh”. Approval includes release-intent without execution authority, Development/Test pilot, explicit SoD/TTL/windows and exact permission/persistence/admission scope. Pre-annotation ADR SHA-256 `b10884383a99d71e0449a04f4c36966ce5768f64cfe4539b7011f2497279b95f`; contract SHA-256 `76efea31a146bd64cf9d97d71cd1d3fc32dac02ba2e6017446f8012043d9d04a`; frozen proposal bundle SHA-256 `cf0c631a3efc795406680cc9f076e9e9ee0f2379a253a977b307ededd0e7a307`. Original bytes and complete approval receipt are retained privately under `.amr-local/t11-design/proposal/` and `.amr-local/approvals/adr-0005-t11/`. Status/acceptance annotations do not change the approved technical content. This is not evidence of implementation, tests, PR review, merge or infrastructure authorization.
