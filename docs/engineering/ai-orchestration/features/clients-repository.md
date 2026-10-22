---
id: FEAT-CLIENTS-001
feature_id: FEAT-CLIENTS-001
module_id: ENG-TOOLING
status: implemented
owner: engineering-maintainers
phase: Foundation
last_reviewed: 2026-10-21
source_paths: [".gitmodules", "clients", "docs/tooling/submodulecheck.py", "docs/tooling/doccheck.py", "docs/tooling/amr.py", "docs/tooling/changeguard.py", ".github/workflows/docs-ci.yml"]
test_paths: ["docs/tooling/tests/test_submodule_boundary.py"]
---

# FEAT-CLIENTS-001 — Independent frontend repository in clients

## User story / outcome

The maintainer wants two independent development sessions with backend in KORDRA and frontend in the same root's `clients/`, backed by separate Git repositories. Engineering maintainers own this development topology, not a new product bounded context. [ADR-0013](../../../decisions/0013-clients-git-submodule.md) records the owner's explicit narrow approval.

## Pre-conditions

Product base is merged PR #7 at `63a7951f48b4441e880defaf315c3755ff79842b`. The tracked Clients tree contains four reserved READMEs and no application source. No Product or Website feature execution is resumed by this setup.

## Main flow

Extract clients-only history, publish the independent Clients repository, and register its reviewed bootstrap commit as a Product gitlink. Product and Clients have separate branches, indices, PRs, CI and working instructions. Product owns integration-pin updates after a Clients revision is available remotely. See the [operating guide](../../clients-development.md).

## Authorization / tenant-scope review

Actor: repository owner/developer. Scope: repository development setup only. Permission: the owner's explicit setup request; GitHub permissions remain account-managed. Tenant scope: not applicable; no business data, API authorization or infrastructure operation. Preserve AMR v4 role boundaries and human merge authority.

## Negative & recovery scenarios

Reject malformed/extra submodule paths and URLs, non-gitlink entries, unresolved index conflicts, missing or incorrect checked-out pins and dirty CI checkouts. Product must still detect invalid documentation outside Clients, including a similarly named nested folder. Independent frontend edits/commits do not change Product content identity until its staged pin changes. Parent source and .gitmodules edits still invalidate context. Standalone frontend links must not escape into a parent checkout. Preserve source history and local work during recovery; do not reset an active Clients session.

## Acceptance criteria

- The actual working path is `KORDRA/clients` and its `.git` file resolves to separate submodule metadata.
- Existing folder content and clients-only history are retained; no unrelated Product history is published in Clients.
- A standalone Clients clone validates without a Product checkout or provider credentials.
- A fresh recursive Product clone obtains the pinned Clients SHA and passes pin validation.
- Product checks work with an uninitialized submodule and do not consume independently edited frontend files.
- Each repository has deterministic CI and explicit ownership. Connector/Auto Review configuration is reported from observed evidence, never inferred from creating a repo.
- Product gates and meaningful boundary regressions pass. No UI/runtime or proposed ADR acceptance is inferred.

## Source & test traceability

The source/test paths above own the Git topology, deterministic boundary checks, Product discovery and context changes, co-owned with FEAT-AMR-001 where applicable. Clients owns its standalone checker/tests at its pinned commit. Original Product tree history remains in Git and an extraction receipt is retained privately. Validation results are recorded in the operating guide and change history after execution.

Shared-tooling reconciliation, 2026-10-21: the pending `docs/tooling/amr.py` changes implement FEAT-AMR-001's owner-declared output paths and opt-in deterministic gate-evidence reuse. This document records that co-owned file's impact; the indexed Clients gitlink, traversal exclusion and independent working-tree boundary retain their existing semantics. The maintainer authorized this traceability-only update alongside T03 validation. Product's pin check and the existing submodule-boundary regressions verify this boundary without building, testing or modifying Clients/Website.

Clients bootstrap commit: `387c56edc75df8332775e267e8cac50462b781dd`; its [repository-integrity CI](https://github.com/NukeGeng/KORDRA-Clients/actions/runs/39262407727) passed. Only the Git topology/scaffold is implemented. Product integration remains a feature-branch change pending CI/review and human merge; this status does not claim the setup has shipped on Product main or that a frontend application exists.

### Executed setup evidence — 2026-10-20

Full local Product gates passed: 168 tooling tests, 48 Artifact and 85 Inventory Bootstrap tests, scope 39/39, locked restore/zero-warning Release build, docs and nine-path ownership checks. Fresh recursive/standalone/uninitialized public HTTPS clone scenarios passed; the standalone client clone ran its six scaffold tests. The [operating guide](../../clients-development.md#setup-evidence) records exact pins and limitations. Native Clients Auto Review configuration is observed (enabled, PR-open trigger, exhaustive off); no test-only frontend review was triggered, so provider execution awaits a real Clients feature PR. Product integration and human merge remain separate.
