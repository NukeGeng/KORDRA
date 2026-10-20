---
id: ENG-CLIENTS-DEVELOPMENT
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-19
---

# Product and Clients independent development

[ADR-0012](../decisions/0012-clients-git-submodule.md) accepts only the repository split. [FEAT-CLIENTS-001](ai-orchestration/features/clients-repository.md) owns implementation and tests. Product GOAL remains paused during setup; no frontend runtime or broader source architecture is approved by folder creation.

## Workspaces and ownership

Session #1 opens `/Users/dangvyhao/Documents/KORDRA`: backend, Product tests, canonical API/business contracts, Product PRs and private `.amr-local/` checkpoints. Session #2 opens `/Users/dangvyhao/Documents/KORDRA/clients`: Clients source/docs/tests and Clients PRs. Start it as an independent human-owned session, not a delegated sub-agent. Its local AGENTS and WORKFLOW define the frontend boundary; root security invariants still apply.

Use `codex/backend/<feature>` and `codex/frontend/<feature>` branches in their respective repositories. Stage explicit paths. Product owns `.gitmodules` and deliberate gitlink changes; never include an unreviewed frontend HEAD using a blanket `git add -A`. Clients must not write parent files, checkpoints, Git metadata, databases or services. Do not copy AMR checkpoints or add coordination services.

The Website repository remains independent and is not part of this operation. Moving it into Clients would require a separate explicit migration; no Website process or source is used here.

## Clone and local identity

Portable clone for collaborators and CI:

```sh
git clone --recurse-submodules https://github.com/NukeGeng/KORDRA.git
```

On this owner's Mac, use `git@github-nukegeng:NukeGeng/KORDRA.git` for Product and `git@github-nukegeng:NukeGeng/KORDRA-Clients.git` for Clients. Keep that machine-specific SSH alias in local Git configuration, not `.gitmodules`. Confirm `ssh -T git@github-nukegeng` identifies NukeGeng. Initialize only while the frontend session is idle:

```sh
git -c submodule.clients.url=git@github-nukegeng:NukeGeng/KORDRA-Clients.git submodule update --init -- clients
```

Default submodule initialization checks out a detached pin. Before coding, Session #2 creates a named branch in Clients from an agreed remote revision. Do not recursively update, deinitialize, clean or reset Clients while it is being edited. Git repository separation is not a filesystem sandbox; instructions and explicit staging remain necessary.

## Contracts, services and tests

Canonical accepted contracts remain in Product. Clients records a Product revision and links to its contracts; new APIs need their owning Product feature and decision. Current inventory ingress remains denied. Mock UI uses synthetic fixtures and is labelled as a mock, never a real permission/tenant decision. No database secrets, kubeconfig or company data enter frontend bundles or examples.

The backend session owns Product API/PostgreSQL lifecycle; the frontend session owns its dev server when an actual UI feature is approved. Agree existing ports before starting services; do not kill unrelated services. No runtime/dependency is installed by this scaffold setup.

Product docs/context checks exclude only the declared Clients submodule. They retain its indexed pin and Product source identity. Product's `clients-pin` CI job initializes and verifies that exact commit; frontend CI owns frontend tests. Scaffold checks do not prove browser/UI functionality. Integration/E2E gates belong to a concrete approved feature at the integration boundary, not every independent edit.

## Integration and review

1. Clients developer tests and creates a scoped Clients PR.
2. Codex Auto PR Review runs when connector access and repository settings are enabled; track BIR independently for each PR (default one round, maximum three). Do not invoke Sol for routine PR review or install a trigger controller.
3. Human maintainer approves merge; push the accepted Clients commit before referencing it in Product.
4. Product coordinator updates only the intended gitlink pin and matching feature evidence in a Product PR. Record the Clients SHA and Product contract revision, and let `clients-pin` validate a fresh checkout.
5. Human approves Product merge. Frontend merge is not Product integration approval.

New repositories may require the owner to add `NukeGeng/KORDRA-Clients` to the ChatGPT Codex Connector installation and enable automatic Code Review. GitHub PR protection and deterministic CI do not prove those provider settings. Report observed setup results separately; no manual trigger is sent merely to test configuration.

## Checkpoint compatibility and rollback

Do not reset completed Product GOAL checkpoints. Existing receipts remain historical. Pre-migration source snapshots include the former Clients files and must be refreshed explicitly before unfinished work resumes; no execution is replayed automatically. Future Product content identities track the indexed gitlink, so unrelated Clients edits cannot make Product gates unstable.

Rollback requires a reviewed Product change replacing the gitlink with the selected Clients tree and removing its declaration. Preserve Clients commits and dirty work first; keep the remote repository/history available. There is no automatic destructive cleanup.

## Setup evidence

KORDRA-Clients exists with clients-only extracted history and bootstrap `387c56edc75df8332775e267e8cac50462b781dd`. All four original READMEs match byte-for-byte at extraction commit `bd1a50d635d5932fb769bb4892d642bdcc92e42c`; their bootstrap links now work without a parent checkout. Its six standalone/negative tests and scaffold validation passed; [GitHub CI run 39262407727](https://github.com/NukeGeng/KORDRA-Clients/actions/runs/39262407727) passed on that bootstrap SHA.

Clients [main ruleset 25408245](https://github.com/NukeGeng/KORDRA-Clients/rules/25408245) is active: PR required, strict `repository-integrity` check, resolved review threads, no force push/deletion, zero required approving reviewers and no bypass actors. GitHub reports NukeGeng as the sole admin collaborator. Portable submodule URL is HTTPS; local Product and Clients remotes use `github-nukegeng`.

The native GitHub installation now visibly includes KORDRA-Clients, following owner authentication. The native per-repository Code Review page for `github-1425676424` shows automatic review enabled, trigger `When PR opens`, exhaustive review off, and author scope following the owner's enabled personal preferences. Only Clients settings were changed. This verifies configuration, not execution: no test-only Clients PR or manual review trigger was created. Native BIR hard-cap support remains unverified.

On 2026-10-20, `python3 docs/tooling/amr.py gates --base 63a7951f48b4441e880defaf315c3755ff79842b` passed with stable source: strict docs 219 Markdown/0 errors/0 warnings in this workspace, scope 39/39, tooling 168 tests, pin metadata validation, locked restore, Release build 0 warnings/errors, Product tests 48 Artifact + 85 Inventory Bootstrap, and source ownership 9 changed paths/0 problems. The one earlier mapping failure was fixed by requiring both owner documents for shared paths. The recorded Product tests validate existing behavior, not a frontend application. Only evidence text is added after that local run; final PR CI validates its exact published revision. This feature branch still requires current CI/provider evidence and human merge; Product main is not claimed migrated until that merge. No additional local AI review, Website access, application dependency or runtime server is used.
