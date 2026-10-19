---
id: PROD-VALIDATION
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Real-user validation — OSS Beta to adoption

Kordra is a solo-built OSS-first product hypothesis. External testers are not required to become core maintainers.

## Pre-Beta discovery
Interview engineers operating existing CI/CD with multiple deployment mechanisms. Record current workflow, downtime risks, authority for production operations and install constraints. Capture evidence and anonymous summaries with consent.

## Adoption funnel
Discover → Read docs → First install → Connect sandbox target → First successful deploy → First verified rollback → Return use → Recommend/adopt in team.

## Metrics (do not manufacture numbers)
- Setup completion rate and time-to-first-sandbox-deploy.
- Number of independent installs and first successful deployments.
- Repeat deployment use over a defined observation period.
- Failure/recovery and security findings, support time per new user.
- Issues from independent users, quality of reproducible bug reports.
- Willingness to pay for support/governance/hosted operation (separately measured).

## Community distribution
GitHub README, accurate demo GIF/video, technical write-ups, DEV/Hashnode, relevant DevOps communities. Disclose affiliation when sharing; avoid fabricated endorsements, spam and unverifiable claims. SEO targets practical problems and tested integrations, not an inflated feature catalog.

## Data policy
Usage telemetry is opt-in, documented, minimally scoped, with no collection of customer kubeconfig, secrets, message payloads or business data. Do not claim active installations from GitHub stars or clone counts alone.

## Go/no-go
After beta, prioritize based on repeated use and severity of user blockers before implementing non-core roadmap items.
