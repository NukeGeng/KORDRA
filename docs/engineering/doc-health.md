---
id: ENG-DOCHEALTH
status: proposed
owner: unassigned
last_reviewed: 2026-10-19
---

# Documentation health metrics and automated gates

## Doc CI
Run `python3 docs/tooling/doccheck.py`. It verifies local markdown links, frontmatter IDs/status/owner/date, uniqueness, feature-module mapping, registry and required sections, plus source/test mapping with optional strict mode. No external dependency.

## Suggested dashboards
- Docs with stale review date (critical 30d, core 60d, other 90d; risk-based, configurable).
- `% implemented features with source+test evidence`.
- `architecture drift issue count`, `unmapped code change count`, `orphan docs`, `ADR superseded but referenced`, `public contracts missing consumers`.
- `permission matrix gap count`, `unowned message count`, `high debt age`.

## Limits of automation
Checker cannot prove code behavior, correctness of policy/tenant isolation, event schema compatibility or truthfulness of evidence. Must use review, tests and runtime verification.

## PR integration
`.github/workflows/docs-ci.yml` currently runs `python3 docs/tooling/doccheck.py --strict-sources` plus source-to-feature changeguard on PRs and offline scope checks. Strict-source validation only checks implemented/verified feature mappings; it does not prove runtime behavior or that all proposed docs are approved.

Change guard CI bổ sung để chống **code changed, docs unchanged**: [requirement-to-release](../governance/requirement-to-release.md).

## Documentation Sync v1.5 integration — 2026-10-18

Source package: user-supplied `.temp/kordra-sync-v1.5`, with manifest baseline `2aa156f4e6e3afe3cc648186ac6a3242781dc71b`. Package README still starts with a v1.4 title, while its manifest and appended operator requirements identify v1.5; only this v1.5 package was integrated. No v1.4 pack was applied separately.

- The original checkout was on merged PR head `0192032` with pending README/brand edits. The script's `--check` correctly rejected its HEAD. Fetched `origin/main` matches the manifest; its source tree equals `0192032`'s tree. A new local branch `codex/docs-sync-v1-5` starts at the exact merge baseline and retains local edits.
- In a disposable clean Git clone at the exact baseline, the supplied script passed `--check` (14 expected Git blobs and 25 proposed files), rejected a dirty-tree fixture, then passed `--apply`. The resulting preview passed strict doccheck (169 Markdown files, 151 IDs, 21 module metadata entries), scopecheck (39 cases) and `git diff --check`. The 21 metadata entries comprise the existing 20 product modules plus the engineering ownership unit; no product module was added.
- The active checkout was integrated by reviewing and merging that validated preview, not by weakening the script's clean-tree/HEAD/blob guards. The current README banner/domain layout was retained; the owner's supplied `kordra.id.vn` selection was reconciled with the package's older domain assumption. Original GOAL outcome numbering and all 43 A–F catalog rows were preserved.
- All 12 added documents remain proposed. OP-01–OP-10 and OP-P0–OP-P4 describe requirements/sequencing, not implemented features. Feature seams, illustrative workflow states, first-driver choice and durable execution prerequisites are explicit; no stack or source-layout decision is accepted by import.
- Existing roadmap, feature evidence, debt and traceability now point to independently rechecked PR/merge-SHA CI facts. Provider automatic review remains unverified. The sole-owner merge policy is recorded separately from product/production authorization in [status evidence](status-evidence-policy.md).
- Product source/tests, AMR routing/native runtime/config/CI, accepted ADR-0008, proposed ADR-0009 and LAB-01 files are preserved. A narrow checker correction under FEAT-AMR-001 excludes reserved local draft/receipt directories; its regression coverage is described below. Review-only licensing and public LAB/history recommendations were not applied; no LICENSE, infrastructure operation or publication permission was inferred. Company authorization/IP, backup ownership, credential trust, first target and architecture/release decisions remain open in the owning product/governance documents.

The sync script was exercised as a local import tool and remains in the supplied ignored package; it was not added as a competing implementation under product or AMR source paths. Raw preservation and preview receipts remain private in `.amr-local/`. Documentation validation proves structure and mapping, not operational readiness or legal/ownership approval.

### Local import discovery regression

On the actual checkout, the original checker recursively scanned the supplied `.temp` pack and private `.amr-local` preview copies, producing 286 spurious metadata/link/duplicate errors across 217 Markdown files. A focused regression reproduced duplicate-ID/broken-link failures from local drafts before the correction. `doccheck.py` now prunes `.git`, `.temp` and `.amr-local` without following directory symlinks. It deliberately does not use `.gitignore` as a validation exemption: untracked or ignored canonical docs and other hidden documentation remain checked. Existing source/test mapping is FEAT-AMR-001; no new dependency, product contract or architecture is introduced.

### Import-only validation checkpoint

`python3 docs/tooling/amr.py gates --base 2aa156f4e6e3afe3cc648186ac6a3242781dc71b` passed on the integrated working tree with `source_stable=true` and `local_status=passed`. This is local evidence; no new PR or remote CI result is inferred.

| Check | Actual result |
|---|---|
| Strict documentation check | 169 Markdown files, 151 IDs, 21 module metadata entries; zero errors/warnings |
| Scope registry | 39 cases, 39 unique IDs; zero errors |
| AMR/tooling unit suite | 114 tests passed, including four documentation-discovery regressions |
| Locked .NET restore / Release build | Passed; zero build warnings/errors |
| Product tests | 48 passed; zero failed/skipped |
| Working-tree source ownership | Two changed source/test paths, zero mapping problems |
| Catalog and preservation assertions | All 43 original A–F rows, 10 OP groups, 5 pilot increments, 20 product modules and 12 proposed additions; product/runtime/contracts/ADRs/LAB and unrelated user assets preserved |
| `git diff --check` | Passed |

The discovery regression failed before the source correction and all four focused cases passed afterward. These results validate the documentation/tooling increment, not an operator console, deployment/backup integration or production permission.

Independent local Code Review was scoped to `docs/tooling/doccheck.py` and `docs/tooling/tests/test_amr.py`. Codex CLI completed with exit 0, advertised `gpt-6-luna` / `max` in its runtime header per the user's preference, and reported no actionable regressions. SHA-256 identities for both reviewed files matched before/after review. This is a completed advisory review of the checker patch, not provider automatic PR review, native desktop coordinator model evidence, or acceptance of the proposed product documents. No collaboration sub-agents were spawned.

## Post-bootstrap and folder-framework validation — 2026-10-18

The import table above is historical. Actual ADR-0010 bootstrap completion has 114 passing tooling tests, 48 Artifact + 19 bootstrap tests, locked restore, zero-warning Release build, scope/mapping gates and stable independent native GPT-6.1 Sol High review with contributor triage. See [current feature evidence](../features/infrastructure-inventory/features/register-target.md#coordinator-final-acceptance--2026-10-18). Shared-root ownership fixture now tests both Artifact and Inventory ownership without changing changeguard semantics.

Only after that acceptance, 45 proposed folder READMEs and [the framework guide](repository-folder-framework.md) were added; strict documentation validation passed with **216 Markdown files, 198 tracked IDs, 21 module metadata entries, zero errors/warnings**. Actual project graph remains six projects; API source hashes match the recorded actual-Program smoke. New-folder presence is not implementation evidence; new remote CI and provider automatic review remain unverified. The IAM local-account choice is a design preference, not an approved authentication/storage runtime.

## AMR v4 and current full-gate result — 2026-10-19

After the synthetic IAM policy proof and AMR v4 migration, `python3 docs/tooling/amr.py gates --base 2aa156f4e6e3afe3cc648186ac6a3242781dc71b` passed on the complete local worktree with `source_stable=true`. Strict docs checked 220 Markdown files, 202 tracked IDs and 21 module entries with zero errors/warnings; scopecheck passed all 39 cases; 146 AMR/tooling tests passed; locked .NET restore and Release build passed with zero warnings/errors; product tests passed 48 Artifact and 85 bootstrap cases; source ownership mapped 27 changed paths with zero problems. This is current local verification, not remote CI, PR review or merge evidence.
