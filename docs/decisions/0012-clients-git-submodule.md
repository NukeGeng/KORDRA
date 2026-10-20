---
id: ADR-0012
status: accepted
owner: engineering-maintainers
last_reviewed: 2026-10-19
---

# ADR-0012 — Independent Clients repository at the existing clients path

## Decision and human authorization

Accepted narrowly on 2026-10-19 by the repository owner: extract all existing `clients/` into an independent Git repository and mount it back at exactly `KORDRA/clients` as a Git submodule. The owner explicitly rejected creating a sibling `KORDRA-Frontend` worktree and then requested implementation and setup. This records that decision; it does not accept the broader ADR-0009 proposal.

## Scope and alternatives

Use `NukeGeng/KORDRA-Clients`, preserving the existing public scaffold's visibility and a clients-only Git history. KORDRA records a gitlink commit and a portable HTTPS URL in `.gitmodules`; this Mac uses the existing `github-nukegeng` SSH identity locally. Separate worktrees and a second checkout directory were considered and rejected by the owner to retain the designed root layout.

Product owns backend, canonical business/API contracts, its checkpoints and the integration pin. Clients owns `web/`, `desktop/`, shared packages, frontend tests and its own PRs. Two independent human-started sessions are allowed; this does not authorize spawning collaboration sub-agents or cross-session orchestration. The existing marketing Website repository is outside this migration; no Website source, services or database are moved.

## Isolation and integration contract

- Product validation does not recursively read or test Clients source. Only the explicitly configured `clients` boundary is excluded; malformed or additional submodule declarations fail validation.
- Product content identity includes the staged gitlink SHA, rather than an independently changing frontend worktree. Index/HEAD changes still invalidate context. Completed checkpoints remain historical evidence; snapshots from before this topology change require explicit refresh before reuse, without replay or rewriting execution receipts.
- Product pin CI initializes the exact recorded commit in a clean checkout and verifies its identity. Clients CI validates the frontend repository independently. Passing scaffold validation is not passing frontend application, browser or integration tests.
- Merge/push the Clients revision first, then update Product's pin in a scoped integration PR. Do not follow a floating branch during CI. Neither session recursively updates or resets the other repository while it is being edited.
- Product contracts remain canonical; an explicit Product revision is referenced from Clients. New or changed APIs and permission behavior require their existing feature/ADR decisions. No client-side permission check grants backend authority.
- Codex Auto PR Review remains the default per repository, subject to connector repository access and Code Review settings. A new repository is not assumed configured. BIR remains manual, at most three rounds per PR, with no additional local AI review or automatic Sol invocation.

## Limits, validation and reversal

No Vue/Tauri/package manager, runtime service, authentication mechanism, API exposure, database integration, IAM authority contract, license or other proposed ADR is adopted. GOAL Product remains paused. The four original directory reservations remain proposals inside Clients.

Acceptance requires clients-only history inspection, standalone Clients validation, a remote recursive clone at the recorded pin, negative boundary/pin/dirty-checkout tests, unchanged Product routing tests and applicable Product gates. Repository branch protections must require PRs and the repository's real CI checks; human merge approval remains explicit.

To reverse, use a reviewed Product PR that replaces the gitlink with the approved Clients tree at a recorded commit and removes `.gitmodules`. Preserve the Clients remote/history and existing dirty work; do not force-deinitialize an active frontend checkout. See the [setup and operating guide](../engineering/clients-development.md) and [FEAT-CLIENTS-001](../engineering/ai-orchestration/features/clients-repository.md).
