---
id: ENG-AMR-REQUEST
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
---

# KORDRA AMR v2.1 — Quality-First AI Engineering & Native Review

> **Status:** Proposed implementation specification  
> **Scope:** Developer tooling and AI-assisted engineering for Kordra; **not** a feature of the public DevOps Control Plane.  
> **Priority:** Correctness > Architecture Integrity > Security > Reliability > Maintainability > Development Efficiency > Token Optimization.

## 1. Mission

Implement or revise the existing **Auto Model Router (AMR)** to select an appropriate model for coding work, preserve engineering context, run verifiable quality gates, and integrate **OpenAI Codex Auto-review** and **Codex Code Review** where supported.

The goal is **accepted, maintainable software** rather than maximizing the share of inexpensive-model calls. Do not use a 65/30/5 allocation, forced round-robin switching, or model switching on every workflow stage.

## 2. Critical distinctions

| Capability | Boundary and responsibility | Important limitation |
|---|---|---|
| Model Routing | Choose an available model and reasoning effort for each task/stage or subagent | `AGENTS.md` alone does not guarantee a real model switch |
| Codex Auto-review | Review eligible sandbox-boundary approval requests by the coding agent | Not a code reviewer; not an authorization to act on production |
| Codex Code Review | Independently review GitHub PRs and, where supported, GitLab MRs | Findings are advisory; CI, branch protection and human approval remain needed |
| Engineering Gates | Compiler, tests, security, architecture, contracts, documentation validation | Must execute tools; reasoning cannot be substituted for test evidence |
| Human Change Approval | Approval to operate on production or sensitive resources | Not delegated to Auto-review, model router, or PR code reviewer |

## 3. Mandatory first step — discover runtime support

Before modifying the repository:

1. Read the existing `AGENTS.md`, `GOAL.md`, `WORKFLOW.md`, `docs/README.md`, relevant architecture/ADR/feature docs and AI configuration. Preserve them.
2. Determine the **actual** runtime: Codex CLI, IDE extension, desktop app, Agents API/SDK, third-party coding agent, or other.
3. Inspect installed version, supported model identifiers, reasoning efforts, subagent definitions, and per-stage overrides.
4. Check actual availability and authorization for the intended Luna / Sol profiles. Treat display names in this document as **intent profiles**, not validated API identifiers.
5. Determine whether automatic model invocation, actual model choice, usage accounting, review status and PR/MR trigger can be inspected.
6. Review repository provider: GitHub PR vs GitLab MR; their native integrations and rollout/eligibility may differ.
7. Produce a small **capability matrix**: supported / requires user setup / unsupported, with evidence. Do not fabricate capabilities.

Use native runtime capabilities when available. Build a lightweight orchestrator only if capability gaps genuinely prevent the expected workflow. For an independent API/SDK-based harness, do **not** assume it automatically inherits Codex Auto-review.

## 4. Model profiles — Quality First

| Profile | Intended model | When to choose |
|---|---|---|
| ROUTINE | GPT-6 Luna High | Narrow, clearly specified tasks with known ownership and low risk: CRUD, DTO, mapping, formatting, straightforward tests and docs |
| ENGINEERING (default) | GPT-6.1 Sol Medium | Source comprehension; feature/module ownership; dependencies; integration; feature design and implementation; Marten/Wolverine; messaging; saga; troubleshooting; relevant code review |
| CRITICAL | GPT-6.1 Sol High | High-risk design, diagnostics and independent review: auth, tenancy, platform privileges, secrets, state integrity, concurrency, idempotency, rollback, production operations |

The default for **substantive source reading** is ENGINEERING, not ROUTINE. Mechanical file search or symbol enumeration may use deterministic tools/Luna.

A critical feature can use Sol High for design and independent review, while Sol Medium handles bounded implementation. Do not force this split when it loses context or introduces risk.

The router selects the **smallest sufficient capability for quality**, not the cheapest model regardless of consequences.

## 5. Dynamic risk classification

Assess using evidence, not only the agent's self-reported confidence:

- Bounded contexts affected; feature owner and documentation freshness.
- Source uncertainty, business rules, dependency and event flow complexity.
- Public API/message contract migration and compatibility.
- Authentication, authorization, roles, tenant/platform boundary and privilege changes.
- Distributed coordination, retries, deduplication, idempotency, leases and fencing.
- Database migrations, data integrity, rollback and irreversibility.
- Secrets, registry credentials, SSH, agent privilege, remote execution.
- Production blast radius and LAB-01 scope.
- Test coverage, negative cases, external integration constraints.

Classify ROUTINE / STANDARD / CRITICAL with rationale; **reclassify while working** if new evidence appears. Use a simple heuristic first; add scoring only when test data justifies it.

## 6. Adaptive execution workflow

```text
TASK_RECEIVED
 -> DISCOVER_RUNTIME_CAPABILITIES (once, recheck if changed)
 -> CLASSIFY_AND_RESOLVE_CONTEXT
 -> SOURCE_COMPREHENSION_AND_IMPACT_ASSESSMENT
 -> PLAN_IF_NEEDED
 -> IMPLEMENT
 -> RUN_DETERMINISTIC_QUALITY_GATES
 -> LOCAL_RISK_BASED_REVIEW (when useful/required)
 -> CREATE_OR_UPDATE_PR_OR_MR
 -> NATIVE_CODE_REVIEW (if connected and enabled)
 -> TRIAGE_FINDINGS -> FIX_AND_RETEST_IF_NEEDED
 -> RECONCILE_DOCS_AND_EVIDENCE
 -> HUMAN_APPROVAL_WHERE_REQUIRED
 -> COMPLETE
```

Rules:

- Skip unnecessary stages for isolated low-risk tasks, **never** mandatory applicable quality gates.
- Sol Medium may keep the same session from analysis through implementation; avoid needless handoffs.
- Use independent review for CRITICAL changes. Do not count the same agent's self-check as independent review.
- Run tests using actual tools, not model assertions.
- Changes are **not complete** while required checks fail, unresolved blocking findings remain, or required human authorization has not been obtained.

## 7. Escalation and failure handling

1. On a failed check, collect concrete failure evidence (command, output, file/line, relevant contract).
2. Try a focused repair by the current capable agent for an obvious local issue.
3. After up to **two evidence-backed attempts at the same root cause**, escalate to Sol Medium or High as the risk demands.
4. Immediately escalate relevant security, multi-tenant isolation, privilege escalation, state corruption, race condition or irreversible data-loss concerns.
5. Stop after bounded retries; report blocker and remaining risk. Never loop indefinitely.
6. Never mark a finding resolved just because code changed; rerun its reproduction/validation.

## 8. Context continuity and source of truth

Hand off a **structured summary**, not an unfiltered full chat transcript:

- Task ID and objective; selected profile/model if verifiable.
- Git SHA, branch, changed paths and affected feature/module.
- Source/tests/docs consulted, and whether docs are current or potentially stale.
- Domain invariants, contracts, permission/tenant boundaries and ADR decisions.
- Decisions made and the supporting evidence.
- Implemented work, exact test commands/results, unresolved findings and next action.

Source and passing tests are implementation evidence; feature docs/ADRs capture approved intent. When they conflict, investigate and record drift — do not blindly override one with the other. Invalidate or revalidate snapshots when source/contracts change.

## 9. OpenAI Codex Auto-review — sandbox approvals

Treat this as a **separate safety boundary**, not code quality review.

- If the actual local Codex runtime supports it, and the user/organization permits it, enable its native review mode via the documented permission control or valid local configuration.
- An eligible example for supported CLI installs is `approval_policy = "on-request"` and `approvals_reviewer = "auto_review"`; **validate supported keys in the installed runtime before writing them**.
- Auto-review handles requests that otherwise require approval to cross the configured sandbox boundary; it does not enlarge the sandbox or grant company-environment privileges.
- Maintain strict scopes for host, environment, tool, writable files, network endpoints and commands.
- Production deployment, rollback, secrets access, deleting resources, database mutation, cluster administration, or high-impact infrastructure changes must retain explicit authorized human approval.
- LAB-01 is **read-only by default**; sandbox mutation requires clearly approved test scope.
- Do not weaken sandbox controls or broadly autoapprove commands to reduce prompts.
- Log the approval decision where the runtime supports it. Do not claim native Auto-review is active without verifying a real eligible approval request.

## 10. OpenAI Codex Code Review — pull/merge requests

Treat native Code Review as an **independent review channel**; it is not governed by the AMR model-profile allocation.

- For GitHub, verify that Codex Code Review is connected and repository **automatic reviews** are enabled. A review can also be requested manually where supported (for example `@codex review`).
- For GitLab, determine the currently supported MR integration and trigger; **do not assume parity** with GitHub or turn on an unverified integration.
- Review PR/MR diffs plus relevant code context, repository-scoped `AGENTS.md` guidance and CI status.
- Retrieve findings, associate them with the relevant feature/module and affected source/test lines, and triage severity, validity and impact.
- A coding agent may propose fixes, but **findings are not automatically accepted blindly**. Validate each actionable claim against the current revision.
- After fixes, rerun checks and request/observe a new review where supported. Prevent infinite fix/review loops.
- Never claim Code Review passed because no review ran, no comment appeared, or the integration failed.
- Repository branch protection, required CI checks, CODEOWNERS and human approval remain authoritative merge gates.

### Repository-specific review focus

Keep short, consequential rules in root or appropriate module `AGENTS.md`. The most important for Kordra are:

1. No cross-tenant data access or platform super-admin privilege leakage; explicit resource-scoped permission checks.
2. No duplicate side effects from retry/ACK loss; safe idempotency and reconciliation before retry.
3. No breaking API, event, schema or driver contract changes without migration and tests.
4. No unauthorized production action; artifact digest and approved spec immutable at dispatch.
5. No secret disclosure in code, logs, events, screenshots or reports.
6. No unowned feature changes; docs, tests and ADRs must remain in sync.

Formatting, lint and mechanical validation belong in CI, not long review prompts.

## 11. Engineering quality gates

**Code:** build, lint, static checks, existing conventions, dependency hygiene.

**Architecture:** module ownership, bounded contexts, dependency direction, domain invariants, ADR and contract compatibility.

**Security:** identity, role/permission scope, tenant isolation, secrets, validation, audit and least privilege.

**Reliability:** durable workflow transitions, transactional inbox/outbox, retries, idempotency, timeouts, leases/fencing, recovery, rollback and observations.

**Tests:** targeted unit/integration/contract/regression tests, negative cases, race and failure cases where appropriate.

**Docs:** source-to-feature mapping, module definitions, message contracts, operational runbooks and technical debt register.

Treat missing test infrastructure as a documented blocker or exception requiring approval, **never** as a silently passing gate.

## 12. Integration into Kordra docs and architecture

Maintain current repository organization; do not create another competing policy set. Suggested paths if absent:

```text
AGENTS.md
WORKFLOW.md
.github/pull_request_template.md
.github/workflows/              # quality gates, if using GitHub Actions
.codex/                        # only if supported by actual runtime
  config.toml
  agents/
docs/engineering/ai-orchestration/
  README.md
  model-routing.md
  risk-classification.md
  context-handoff.md
  native-review.md
  review-findings-workflow.md
  quality-gates.md
  testing-and-evidence.md
```

Respect ownership of `docs/architecture/`, `docs/decisions/`, `docs/contracts/`, `docs/features/`, `docs/operations/`, `docs/reference-environments/lab-01/`. Document any change requiring an ADR. Never change the public Kordra backend solely to implement local developer AI routing.

## 13. Test matrix

| ID | Scenario | Expected outcome |
|---|---|---|
| AMR-01 | Mechanical symbol lookup | Deterministic tool or Luna; no unnecessary architectural reasoning |
| AMR-02 | New feature with source/feature ownership analysis | Sol Medium selected; context linked to source paths |
| AMR-03 | Routine CRUD with verified narrow spec | Luna High permitted; checks pass |
| AMR-04 | Marten/Wolverine integration with message contract changes | Sol Medium + contract checks |
| AMR-05 | Tenant scope or role permission change | CRITICAL path; Sol High analysis/review and tenant security tests |
| AMR-06 | Retry/duplicate dispatch and lost agent ACK | CRITICAL path; no duplicate uncontrolled side effects |
| AMR-07 | Repeated reproducible test failure | Evidence-backed escalation, bounded retries |
| AMR-08 | Model ID or reasoning effort unsupported | Explicit fallback and evidence; no silent substitution |
| AMR-09 | Switch model/session mid-task | Handoff preserves commit, decisions and test outcomes |
| AMR-10 | Native Auto-review eligible permission request | Demonstrable native decision; sandbox remains enforced |
| AMR-11 | Unauthorized production/K8s mutation attempt | Blocked or escalated for explicit authorized human approval |
| AMR-12 | GitHub PR with automatic Codex Code Review | Verified review on correct revision, findings triaged |
| AMR-13 | Code Review not installed/available | Honest `not_configured`/`unsupported` status; documented manual alternative |
| AMR-14 | Review finds a real bug | Fix + rerun regression + follow-up review where supported |
| AMR-15 | Review finding is false positive | Evidence-based dismissal with audit trail, not blind modification |
| AMR-16 | Missing/stale feature documentation | Drift surfaced; feature docs reconciled before completion |
| AMR-17 | LAB-01 read-only scope | No destructive/production actions without approval |
| AMR-18 | Token/usage not visible | Report `unknown`, never fabricated consumption or savings |

Unit-test routing independently of live models. Run an actual supported integration test for model selection and each enabled review mechanism before claiming successful integration.

## 14. Evaluation — quality first

Track:

- Acceptance and regression pass rate.
- Architecture, contract and tenant-isolation violations.
- Security/reliability findings and time to resolve.
- Number of repair attempts, reopened findings and manual interventions.
- Documentation correctness and traceability.
- Actual selected model and reasoning effort where inspectable.
- Token usage, context re-reading, time and cost **secondarily**.

Do not assert that a profile is cheaper or better in practice until measured on comparable Kordra tasks.

## 15. Deliverables and acceptance criteria

Agent must produce:

1. Runtime capability and support matrix with evidence.
2. A minimal, working AMR model-routing configuration or orchestrator (only when needed).
3. Documented routing and risk-classification policy.
4. Native Auto-review setup if supported; otherwise exact manual action and limitation.
5. Native Code Review setup instructions for the actual repository provider, with observable verification.
6. Updated `AGENTS.md`, `WORKFLOW.md` and feature/engineering docs without deleting existing policies.
7. CI/check integration and review-triage workflow.
8. Test results and actual model/review invocation evidence, including failures.
9. Clear report separating **implemented**, **verified**, **requires user action**, **unsupported**, and **not tested**.

**Do not report completion just because `.toml`, Markdown or placeholder code exists.**

## 16. Execution instruction to the coding agent

Work in this order:

1. Inspect existing repo and runtime.
2. Present brief implementation plan and non-negotiable safety boundaries.
3. Implement the smallest supported native integration first.
4. Add/extend review policies without bloating root `AGENTS.md`.
5. Implement only justified custom routing glue.
6. Run unit, integration, architecture and documentation checks.
7. Verify actual selected models, actual review triggers and human-approval boundaries.
8. Report evidence, unsupported items and next manual configuration steps.

Avoid unnecessary dependency additions, avoid creating an independent AI platform inside the product, do not touch production infrastructure, and never silently bypass quality gates.

---

## Official references (verify the current runtime before implementing)

- Codex subagents: https://developers.openai.com/codex/subagents
- Codex config reference: https://developers.openai.com/codex/config-reference
- Codex Auto-review: https://developers.openai.com/codex/auto-review
- Codex Code Review: https://developers.openai.com/codex/code-review
- Codex custom review rules: https://developers.openai.com/blog/custom-code-review-rules-for-codex
- Agents SDK model selection: https://developers.openai.com/api/docs/guides/agents/models
- Agents API: https://developers.openai.com/api/docs/guides/agents-api/overview
