---
id: PROD-DOMAIN
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Brand, domains and documentation publishing

## Positioning
Kordra is an independent product brand. The project owner supplied **[kordra.id.vn](https://kordra.id.vn)** as the public project domain on 2026-10-17; README and brand links use that selection. This records the owner's choice, not independent verification of registration, DNS, TLS, hosting or a live product. Any future global domain is contingent on availability, budget and a separate owner decision.

## Hosting plan
A static landing page, documentation and public GitHub repository can serve development and beta without paying for a managed production control plane.

## Domain independence
No hard-coded domain in product code, OAuth redirects, webhook targets, documentation generation or API clients. Use configurable `PUBLIC_BASE_URL`, `DOCS_URL`, `APP_URL` and verified redirect allowlists. Keep stable relative links in docs wherever practical.

## Migration if a new domain is acquired
Stage new origin, HTTPS, sitemaps, canonical/hreflang where relevant, 301/308 mapping from each old URL to a corresponding new URL, Search Console verification and long-lived redirects. Recheck OAuth, webhook allowlists and email SPF/DKIM when applicable. Domain changes do not prove SEO rank or revenue outcomes.

## Cost discipline
Do not treat a domain purchase or paid hosting as an OSS beta prerequisite. Document registration renewal terms and whether the chosen TLD can be held by the future legal entity.
