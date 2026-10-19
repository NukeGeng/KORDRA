---
id: GOV-PUBLIC-REFERENCE
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Public reference-environment publication policy

**LAB-01 is a test reference, not a license to publish an employer/customer infrastructure inventory.** Existing public documents require owner authorization review independently of whether credentials are present.

## Shareable only when approved
Generalized component categories, synthetic network addresses, fabricated workload names, generic topology diagrams, simulated queue catalogs and reproducible sandbox instructions without operational weaknesses attributable to a real organization.

## Keep private unless explicit authorized release
Real hostnames/IPs, Kubernetes node and namespace identities, service endpoints, tenant and customer identifiers, queue names with business semantics, actual payloads, production configuration, operational security gaps, detailed HA/backup state, monitoring routes and privileged credentials.

## Separation
- `docs/reference-environments/` in public OSS: synthetic example and safe testing methodology.
- Private inventory: access-controlled external file store owned by the organization, not a hidden folder in a public Git repository.
- If permission to publish is unclear: remove/neutralize sensitive operational detail in future public revisions and obtain an owner review; consider history exposure separately.

## Historical commits
Removing a file from HEAD does not remove old blob references and forks. Before any rewrite or coordinated deletion, obtain authorization and follow a formal incident response plan; rotate compromised secrets if present. This sync pack does not rewrite history or make company infrastructure changes.

## Test safety
LAB-01 remains observe-only by default. Never run deployment, failover, destructive backup tests or business message consumption/ACK without explicit environment and change approval.
