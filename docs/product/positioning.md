---
id: PROD-POSITIONING
status: proposed
owner: unassigned
last_reviewed: 2026-10-18
---

# Kordra — Product positioning and initial market hypothesis

## Core positioning
**Kordra is a self-hosted, infrastructure-agnostic release and deployment control plane for small engineering teams with mixed deployment targets.** Connect existing build systems and deployment runtimes instead of rebuilding CI or Kubernetes.

Value proposition: **Artifact → Release → Policy/Approval → Deployment → Verification → Rollback → Audit** across supported target adapters.

Tagline candidate: *One Control Plane. Any Infrastructure.* This is marketing positioning, not a statement that all infrastructures are currently supported.

## Initial customer hypothesis
- Primary: small DevOps teams/startups managing both Kubernetes/Helm and Docker Compose/SSH, with fragmented deployment scripts.
- Secondary: self-hosted application operators seeking traceable releases and safe rollback.
- Explicit non-claim: no validated market sizing, paying customers, retention or deployment productivity improvement at this time.

## Differentiation to validate
- Coexists with GitHub/GitLab/Jenkins pipelines and artifact registries.
- One approval and audit history across target types with immutable deployment specs.
- Conservative recovery when execution outcome is unknown; does not report worker ACK as deployment success.

## Competitor/alternative framing
Evaluate GitLab CI, Argo CD, Flux, Portainer, Octopus Deploy, manual SSH/Helm scripts. Avoid presenting Kordra as an equivalent replacement until supported capability and operations evidence exist.

## Validation questions
1. Which current release steps are manual and why?
2. What failures caused downtime or difficult rollback?
3. Would the user adopt a standalone self-hosted release coordinator without replacing CI?
4. Which integration must exist for a first successful deploy?
5. Which capabilities are a payment/support trigger versus expected basic product safety?

Sources within repository: [scope](scope.md), [goal](../../GOAL.md), [use cases](use-cases.md). This page is product hypothesis; accepted feature specs and contracts take precedence on implementation details.
