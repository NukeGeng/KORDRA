---
id: ADR-0008
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
---

# ADR-0008 — Validate the first module-local .NET domain increment

**Decision status: Proposed; human acceptance required before product adoption/merge.** This record does not accept ADR-0001–0007 or select persistence, authentication, transport, deployment topology or a production policy engine.

## Context and scope
At `d0eff75951d1d587b1d9689c189a8a62bae18d58`, all 20 product modules are specifications with empty source/test mappings. .NET SDK 10.0.400 is installed. The proposed stack already names .NET 10. The first independently testable work is FEAT-ART-001/I1: immutable, locally verifiable OCI digests owned solely by artifact-management. Registry discovery, project ownership and authorization have unmet dependencies and cannot honestly be called implemented.

## Proposal
- Validate a `net10.0` module-local domain library under `src/Modules/ArtifactManagement/Domain` and its isolated tests. No product host, deployment, storage or runtime package dependency.
- Pin SDK 10.0.400, allow patch roll-forward only; enable nullable analysis and warnings as errors. Use one solution with just this module and its tests.
- Use xUnit 2.9.3, Microsoft.NET.Test.Sdk 18.10.1 and xunit.runner.visualstudio 3.1.5 as test-only, pinned development dependencies; package lock files are committed and restore uses locked mode in CI. These versions are available in the local cache. No coverage, mocking, database, registry or transport package is needed.
- Implement SHA-256/SHA-512 digest syntax and content verification using .NET cryptography. This is an explicitly bounded supported-algorithm profile, not all possible OCI algorithms. Preserve raw bytes; no JSON/content normalization before hashing.
- Treat this branch as a validation increment until the human accepts this ADR. Feature spec remains proposed/partially implemented; never claim a deployable backend or completed registration feature.

## Alternatives and tradeoffs
1. Implement all proposed hosts, database and messaging immediately: rejected for this increment; would adopt unaccepted architectural decisions and obscure independent validation.
2. Add a handwritten test runner: no package dependencies, but reinvents discovery/reporting and is harder to maintain. Prefer standard `dotnet test` with isolated test-only packages.
3. xUnit v3 or MSTest: viable; not needed for these deterministic assertions. The SDK-compatible cached xUnit v2 test/runner combination keeps the initial change small. Revisit test framework once broader bootstrap decisions are accepted.
4. Implement product domain in Python to reuse AMR tooling: rejected; would create a competing product stack instead of validating the documented .NET candidate.

## Security, contracts and operations
Digest parsing is not authentication, authorization, registry existence, signature verification or deployment approval. It accepts no tenant/role/credentials, opens no network connections and persists nothing. A digest alone is not a deployable image identity; registry/repository association belongs to subsequent FEAT-ART-001 increments. Existing permission model, message names and public HTTP contracts remain unchanged.

## Validation and reversal
Build, deterministic positive/negative/content/equality/concurrency tests, module dependency checks, existing Python checks, source-to-feature guard and independent critical review are required. See the feature's evidence. Reversal before adoption is deleting the isolated module/test projects and solution configuration; no data migration or infrastructure rollback exists.

## Human decision required
`[NEEDS-DECISION]`: approve/reject this narrow .NET/testing bootstrap and assign the product module owner before adopting it. Subsequent identity provider, durable storage, messaging, tenant policy and operational changes require their own accepted decisions. Do not infer approval from green tests.

## References
- [FEAT-ART-001](../features/artifact-management/features/immutable-artifact.md)
- [OCI descriptor specification](https://github.com/opencontainers/image-spec/blob/main/descriptor.md)
- [Microsoft .NET unit testing guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-csharp-with-xunit)
