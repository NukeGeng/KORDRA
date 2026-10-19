---
id: GOV-OSS-RELEASE
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# OSS release governance and pre-public-beta checks

## Required pre-release decisions
- Confirm authorship, rights from employment/internship agreements, and permission to publish any adapted infrastructure/test data.
- Explicitly select LICENSE and document the OSS obligations (e.g., MIT vs Apache-2.0 vs AGPL-3.0) with legal review as necessary. No license is selected by this document.
- Add `CONTRIBUTING.md`, a SECURITY disclosure process, support expectations, and an appropriate DCO/CLA policy after deciding contribution rights.
- Verify SBOM/dependency license and vulnerability posture, reproducible builds, release artifacts and signing strategy as justified.
- Publish supported targets, upgrade/migration constraints, install/test instructions and known limitations honestly.

## Product safety baseline
- Minimal authorization/tenant boundaries, secrets hygiene, immutable release binding, idempotency and unknown-outcome recovery.
- Non-production installation test, end-to-end sandbox deploy/rollback and relevant regression gates.
- Production readiness is a **separate** gate; do not claim 24/7 operational support from green CI alone.

## Approval
Open-source publication and public beta label require a conscious maintainer decision. Passing `doccheck.py` verifies documentation structure, not commercial IP rights, code quality or release readiness.
