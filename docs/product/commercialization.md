---
id: PROD-COMMERCIALIZATION
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Commercialization strategy — OSS-first hypotheses

## North star
Build a trustworthy, independently usable community release control plane before paid editions. Current goal is **solo development through OSS Beta**. No enterprise feature obligation is part of the current beta implementation unless required for core safety.

## Candidate phases
1. Community: useful OSS core, reproducible installation, adapters, deploy/verify/rollback, security baseline and audit.
2. Services: paid setup, integrations and support only after permission to commercialize and verified user interest.
3. Enterprise self-hosted: organization policy, SSO, compliance/retention and SLA as distinct value; not artificial removal of minimum safety.
4. Cloud: managed multi-tenant control plane when security isolation, operational support and economics are validated.

## Architecture guardrails
- Community must remain independently useful and secure.
- Optional enterprise extensions may depend on public contracts; the core does not depend on proprietary feature modules.
- No speculative billing, metering, entitlement or subscription code in current beta unless justified by concrete demand and ADR.
- Public license choice, contribution rights, brand/trademark, and employment/company IP are prior approval gates.

## Commercial validation
Paying customer evidence, pain frequency, alternative cost, budget owner, supportability and infrastructure permissions should determine pricing. No revenue projection or timeframe is assumed.

## Not a legal conclusion
Repository is currently public but lacks a root LICENSE at the verified baseline. Commercialization, dual licensing and outside contributions must be reviewed based on actual IP rights and the chosen license; previously granted OSS rights cannot generally be assumed revocable.
