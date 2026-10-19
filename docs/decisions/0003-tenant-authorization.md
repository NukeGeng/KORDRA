---
id: ADR-0003
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-18
---

# ADR-0003 — Resource-scope permission model independent of role names

**Decision status: Accepted for the narrow policy baseline only (2026-10-18).** The repository maintainer explicitly selected “Chấp thuận đúng phạm vi hẹp ADR-0003 (khuyến nghị)” after the reviewed proposal, 66 passing synthetic policy cases, full gates and independent native Sol 6.1 High milestone review. Original proposal date: 2026-10-15. This records the human decision; model reviews did not grant it. Contract/module implementation owners remain unassigned.

## Context
The DevOps Control Plane must operate safely across tenants and heterogeneous targets while keeping the product maintainable over years.

## Considered options
Hard-coded Admin/SuperAdmin role checks; broad global admin.

## Accepted principle
Authorize using principal + permission + resource scope + context; platform super-admin not implicit tenant data access.

## Consequences / tradeoffs
Extensible roles and explicit cross-tenant support. Cost: more comprehensive policy tests, migration and UI admin flows.

## Acceptance criteria and evidence
- Completed: synthetic proof, actual source/test mapping and full local gates; no performance benchmark claim.
- Completed: review of the narrow no-runtime/no-data compatibility and reversal scope below. Runtime migration/rollback remains a prerequisite for later adoption.
- Completed: owner/module/contract/traceability documentation reconciliation and independent review before the explicit human acceptance.
- Runtime contracts, implementation ownership and authentication/storage decisions remain open; this acceptance cannot satisfy those prerequisites by itself.

## Scoped proposal prepared after synthetic proof — 2026-10-18

**Accepted by the maintainer on 2026-10-18; retained proposal scope is unchanged.** Acceptance covers the permission/scope baseline below only. It does not adopt fixture record types as production trust evidence, choose authentication/password/session/storage technology, approve a new project/package, open inventory ingress, or authorize company operations.

1. Authorize by verified principal + current membership/grant + exact permission + authoritative resource ownership and allowed scope. Resolve these through the owning contracts; caller tenant/scope is only a requested selection. Missing, unavailable or unverifiably current required authority denies.
2. Keep platform and tenant access separate. Role names and platform privileges confer no implicit tenant payload access; delegated support requires its own explicit scope, approval and expiry contract.
3. A grant cannot override explicit deny. Expiry is exclusive; revoked/expired session, membership or grant denies. Re-evaluate current authority before protected execution/disclosure; a prior permit is not reusable proof. Runtime freshness, atomicity, cache invalidation and durable revocation remain design prerequisites.
4. For the bounded `target.read` candidate, exact project/environment grants must match the authoritative target ownership chain. The current proof has no implicit project/environment conversion or inherited-grant model. Other actions, multiple-grant resolution, support paths and scope inheritance require explicit owner review before implementation.

### Evidence and limits

[FEAT-IAM-001/I1](../features/identity-access/features/authorize-resource.md#coordinator-acceptance--2026-10-18) supplies the reviewed predicate, actual mapped source, 66 locally passing candidate tests, full gates (141 tooling / 133 product), exact native High execution/design/post-review and contributor triage. This is correctness evidence for a fixture, with no benchmark or production-trust claim. Real credentials, sessions, authority providers, audit storage and infrastructure were not accessed. Feature/module status stays proposed.

### Compatibility, adoption and reversal

There are currently no product consumers, persisted permission bindings or public wire schemas for this proof. Acceptance of the conceptual baseline changes no runtime or data. A later bounded source increment must identify consumers and approved project/owner contracts before extracting any evaluator; do not register the test fixture as a product authorizer. Storage/authentication, data migration and rollback that cannot resurrect revoked authority must be reviewed separately before real data exists. Current reversal removes only the test-only proof/mapping; existing inventory ingress remains denied.

### Human and runtime prerequisites

The maintainer has accepted this exact narrow baseline after advisory architecture/security review; model reviews and tests did not accept it. Assign IAM/TEN/resource-owner/AUD responsibility before runtime, and review concrete authenticated-principal/session validity, membership/owner lookup, policy freshness/deny result, audit availability and identity/storage decisions. Local-account preference and the local PostgreSQL environment grant none of those decisions. Authentication needs a separate approved feature; no login, protected endpoint or product IAM implementation is authorized by this proposal.

### Human decision record — 2026-10-18

Approval matches the reviewed proposal SHA-256 `ed93f7e67572342aebccb887acab23fecace7873833f1b9b154cbfa37d5318fb`. Pre-acceptance milestone review: `native:01a14fe2-871b-78a1-ba2c-6ed3469ec6f8:01a14fe2-8794-7b92-a3eb-abe34ac82574`, observed `gpt-6.1-sol/high`, terminal review exit, stable source and no actionable findings. The explicit human answer above supplies decision authority; the private receipt retains that binding.

Accepted: permission + trusted resource scope, deny-by-default, exclusive expiry/revocation and platform/tenant separation within this document's bounded policy baseline. Not adopted: login/password/session/storage technology, IAM runtime, inventory API access, new projects/dependencies, audit persistence, real provider/cluster access, ADR-0009 or other proposed decisions. Feature/module status remains proposed. Contract proposals and reviews may proceed; runtime implementation still requires the concrete prerequisites above.
