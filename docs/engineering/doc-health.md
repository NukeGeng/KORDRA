---
id: ENG-DOCHEALTH
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
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
`.github/workflows/docs-ci.yml` checks docs links/schema and only strict source-path checks after real source is mapped. Introduce `--strict-sources` when code exists and statuses migrate to implemented/verified.

Change guard CI bổ sung để chống **code changed, docs unchanged**: [requirement-to-release](../governance/requirement-to-release.md).
