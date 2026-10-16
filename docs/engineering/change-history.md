---
id: ENG-HISTORY
status: proposed
owner: unassigned
last_reviewed: 2026-10-16
---

# Feature change log and release-note discipline

## Policy
Thay đổi nghiệp vụ được ghi ở chính feature doc với evidence (PR/commit/version), **không tạo bản mô tả song song theo sprint**. Trang này chỉ giữ index timeline cho thay đổi cross-cutting/major.

## Changes
Sol clarification is tracked by FEAT-AMR-001 (ENG-TOOLING); existing router/config mapping is unchanged. No architecture decision, product authorization change or migration is required.

| Date | Change ID | Module / Feature | Source PR/Commit | ADR / contract | Migration impact | Owner |
|---|---|---|---|---|---|---|
| 2026-10-16 | AMR-GOAL-EXECUTION | ENG-TOOLING / FEAT-AMR-001 | Current branch; native smoke and E2E receipts recorded after validation | Existing local task contract extended with plan/checkpoint v1; no product ADR change | Private local checkpoints only; no production operations or auto-merge | engineering-maintainers |
| 2026-10-16 | AMR-SOL-6.1 | ENG-TOOLING / FEAT-AMR-001 | User clarification; working-tree regression in test_amr.py | Local contract v1 clarified: Sol exclusively means gpt-6.1-sol | None; no model fallback or product behavior change | engineering-maintainers |
| 2026-10-15 | BOOTSTRAP-DOCS | All proposed docs | initial scaffold | proposed ADRs | none | unassigned |
| 2026-10-15 | AMR-2.1 | ENG-TOOLING / FEAT-AMR-001 | Local working tree, no Git metadata; [evidence](ai-orchestration/testing-and-evidence.md) | New local artifact schema v1; no product ADR/contract changes | None; no public API/data migration | engineering-maintainers |
| 2026-10-15 | GIT-BOOTSTRAP | Repository / ENG-TOOLING | User-supplied [GitHub repository](https://github.com/NukeGeng/KORDRA); initial `main` history and `origin` setup | No product contract or ADR change | Local files preserved; private receipts excluded | engineering-maintainers |
| 2026-10-15 | F0-ART-I1 | MOD-ART / FEAT-ART-001 increment I1 | Branch feat/artifact-identity-foundation; local Release build/format, 48 domain tests, 47 Python tests, native diff review completed | ADR-0008 scoped bootstrap; new local digest contract; public APIs/messages unchanged | No storage, registration, infrastructure or data migration | engineering-maintainers pending product owner assignment |
| 2026-10-16 | ADR-0008-ACCEPTED | Foundation / MOD-ART / FEAT-ART-001 I1 | Explicit user approval: "Approve the scoped ADR-0008 bootstrap" | Accept .NET 10 and pinned test-only dependencies only; other ADRs remain proposed | No runtime deployment or automatic merge authorized | User / engineering-maintainers |
| 2026-10-16 | KORDRA-DOCS-V1.2-SOURCE | Architecture / ENG-TOOLING | Integrated ten source-architecture documents; native GPT-6.1 Sol High review findings reconciled; no source migration | Proposal assigned ADR-0009 because ADR-0008 is already accepted for the scoped bootstrap | No project, package, host, contract or product behavior changed; proposal and broad architecture checks remain proposed; existing Domain boundary test remains in CI | engineering-maintainers |

## Commit convention suggestion
`feat(module): ... [FEAT-XXX]`, `fix(module): ... [FEAT-XXX]`, `docs(module): ... [FEAT-XXX]` (workflow hỗ trợ GitHub/CI, không bắt buộc conventional commits nếu team có quy tắc khác).

When implementing, require linkage via PR fields and code mapping; this timeline is *not* an implementation evidence source by itself.
