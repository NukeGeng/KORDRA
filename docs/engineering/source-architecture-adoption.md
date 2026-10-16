---
id: ENG-V12-NOTES
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# v1.2 Addendum — Source Architecture Docs Integration Notes

> **Status: proposed.** Đây là target architecture, không chứng minh các project/file đã tồn tại. Chỉ trở thành baseline sau khi ADR-0009 được phê duyệt; source thực tế, test và Git commit phải được đối chiếu trước khi đánh dấu implemented/verified.

## What changed from v1.1
- Added 6 proposed architecture specs: repository structure, solution, module structure, dependency rules, host composition, extension architecture.
- Added ADR-0009, project conventions and architecture test rules.
- Appended cross-references to AGENTS.md, WORKFLOW.md, indexes, README and source-of-truth guidance without modifying existing domain/feature semantics.

## What did NOT change
- All previous v1.1 feature specs, LAB-01 test cases, CI docs scripts and existing architectural decisions remain in place.
- No C# solution/project/code was created or moved, no real tests were run on a Kubernetes/production environment, no model routing was changed.
- ADR-0009 and source architecture are **proposed**, not accepted; previous accepted source (if any) must be audited.

## Current repository audit (2026-10-16)
This audit compares the proposal with the checked-out source. The commit at `HEAD` is `2d72e7cd7c3c7340d749b2def8f03d0e07b1618c`; the working tree also contains uncommitted AMR work and the imported documents. No source project was moved for this documentation integration.

| Area | Verified current state | v1.2 target | Impact / safe next step |
|---|---|---|---|
| Solution placement | Root `Kordra.slnx` includes the ArtifactManagement Domain project and its test project. `global.json` pins SDK 10.0.400. README, product CI and AMR gate detection also use the root path. | `src/Kordra.slnx` with a larger project graph. | Moving the solution or projects would change the accepted bootstrap layout and CI paths. Keep current paths until ADR-0009 and a migration plan are reviewed; update every consumer and add a regression test in the same migration. |
| Product source | One product project exists: `src/Modules/ArtifactManagement/Domain`; tests live under root `tests/`. There are no API, ControlWorker, Agent, DbMigrator or AppHost projects. | Multiple module, execution and host projects. | Treat the tree as a target only. Add projects for an approved feature; do not scaffold empty hosts or all 20 modules. |
| Dependencies | The Domain project has no package references. The isolated test project uses pinned xUnit/test SDK packages. No Marten, Wolverine, Aspire or provider SDK is referenced. | Marten/PostgreSQL, Wolverine and host composition are described as future choices. | ADR-0008 approves only the current .NET/test bootstrap. Persistence, messaging and host dependencies remain undecided; do not add them from this proposal alone. |
| Module inventory | There are 20 product module specs. The tooling check reports 21 module metadata entries because ENG-TOOLING is included. | A target folder for modules and shared projects. | Preserve the distinction between documented modules and implemented projects; create no folder from inventory alone. |
| Architecture tests | `OCIImageDigestTests.DomainAssemblyDoesNotDependOnOtherModulesOrAdapters` checks the current Domain assembly's references. Product CI runs the solution's test project. Broader project-graph, Contracts, host-composition and Agent-isolation checks are absent. | Dependency, assembly, host and security boundary tests. | Preserve the existing test and distinguish it from proposed graph/host checks. Add further deterministic checks only when their assemblies and rules are approved. |
| AMR location | The active implementation is `docs/tooling/amr.py`, `amr_native.py` and `amr_goal.py`, with FEAT-AMR-001 ownership. AMR currently detects the root solution to decide whether product gates are required. | The integrated proposal now retains `docs/tooling/`. | Do not copy or move AMR as part of this import. Any future relocation or solution move must update feature mappings, CI, gate discovery, tests and receipts together under a reviewed migration. |
| Messaging ownership | MOD-MSG owns internal envelope/routing/inbox/outbox/DLQ records. Customer broker metadata has no confirmed module owner. | The integrated proposal now names the internal module `Messaging` and leaves external broker inventory `[NEEDS-DECISION]`. | Keep MOD-MSG internal delivery focused; do not derive customer-broker ownership from LAB-01 topology or assign it by implication. |
| Module assembly granularity | The current product uses a standalone `Kordra.ArtifactManagement.Domain` assembly; no runtime or Infrastructure assembly exists. | The proposal also shows a single module runtime project containing Domain, Features and Infrastructure. | Decide whether to retain a separate Domain assembly before combining layers. Match enforcement tests to real assembly boundaries; do not flatten the existing domain project as a scaffold step. |
| ADR numbering | ADR-0008 is already accepted for the scoped .NET bootstrap and pinned test-only dependencies. | The supplied source-architecture proposal also used number 0008. | Preserve accepted history; this proposal is ADR-0009 and remains proposed pending owner/security review. |

The supplied package reports validation at 148 Markdown files, 20 modules, no missing v1.1 files and zero document warnings. Those are package-reported figures, not results for this checkout.

## Independent architecture review (2026-10-16)
A native read-only assessment ran on the integrated checkout with the requested **`gpt-6.1-sol/high`** profile. The app-server route and terminal turn both reported that exact model and effort, the turn completed, and `source_stable=true` for reviewed checkout `2d72e7cd7c3c7340d749b2def8f03d0e07b1618c` plus its then-current working changes. Native thread `01a143e4-8255-70f3-811e-80a17fcb6c66`, turn `01a143e4-82ab-7370-99f4-b9c480ccbfd9`. The review changed no files and accepted no decision.

The assessment identified these items for the proposal:

| Finding | Disposition in this integration |
|---|---|
| ADR-0009 proposes Marten/Wolverine and a broader target while ADR-0008 accepts only the bootstrap. | Clarify ADR-0009's proposed scope and keep every technology/project addition unimplemented until that proposal is reviewed and accepted. ADR-0008 remains unchanged. |
| Moving `Kordra.slnx` to `src/` would break README/CI and AMR's product-gate discovery. | Retain the root solution until a complete consumer migration is approved and tested. |
| The architecture test audit omitted the existing Domain assembly dependency test and its CI execution. | Correct the audit: one narrow test exists and CI runs it; broader graph, Contracts, hosts and Agent isolation checks do not exist. |
| `MessagingTopology` left external broker metadata ownership ambiguous against MOD-MSG's internal delivery ownership. | Rename the proposed internal module entry to `Messaging`; leave external broker inventory as `[NEEDS-DECISION]` without importing LAB-01 topology into a generic module. |
| Module layer and assembly granularity is unresolved against the current standalone Domain assembly. | Preserve the current Domain boundary until the proposal chooses and tests a compatible project shape. |
| ADR-0009 requires a package list, host PoC, Module→Contract/Agent isolation evidence and named owner review before acceptance. | Keep ADR-0009 proposed; these acceptance blockers need concrete evidence and assigned reviewers. |

The read-only check ran strict document validation (157 Markdown files, 139 IDs, 21 module metadata entries, zero errors/warnings) and an in-memory solution graph check (two projects, only tests → Domain; Domain has no project/package references). It did not run build, product tests or the full AMR gates. Final post-integration gate evidence is recorded in [runtime validation](ai-orchestration/testing-and-evidence.md).

## Steps for adoption
1. Import package into Kordra repository (preserve Git history; inspect diff against any files modified since v1.1).
2. **Completed:** independently review ADR-0009 and the six specs against current source using native GPT-6.1 Sol High; resolve the findings above in the proposal and evidence.
3. Architecture owner, security reviewer and project maintainer review the remaining open decisions and acceptance evidence; do not accept ADR-0009 until those reviews and its PoC criteria are satisfied.
4. Choose a minimal viable structure and record each decision in the ADR; do not create unused projects.
5. Preserve the existing Domain assembly test. Add deterministic project-graph, Contracts, host and Agent checks only after the corresponding rules and assemblies are approved; integrate and verify them in CI.
6. Migrate one approved feature at a time, updating all path consumers and mappings; only then promote statuses `implemented` / `verified`.

## Validation disclaimer
Static `doccheck.py` validates Markdown structure/links; it does NOT validate C# project references, source ownership, source folder existence, runtime deployment or legal ownership of OSS code.
