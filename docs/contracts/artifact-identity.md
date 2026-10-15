---
id: CTR-ARTIFACT-IDENTITY
status: proposed
owner: engineering-maintainers
last_reviewed: 2026-10-15
---

# Artifact digest domain contract — validation increment v1

Owner: MOD-ART / FEAT-ART-001/I1. Consumers in this increment: its domain tests only. No public API/event/persisted schema exists; no existing consumer or message schema changes. Future cross-module consumers must explicitly review this contract before adoption.

`OCIImageDigest` is an immutable validated value with `Value`, `Algorithm`, `EncodedHash`, ordinal equality/hash semantics and stable textual output. It exposes `Parse(string)`, `TryParse(string?, out OCIImageDigest?)` and `MatchesContent(ReadOnlySpan<byte>, long expectedSize)`.

Supported profile: exactly `sha256:` plus 64 lowercase ASCII hex digits or `sha512:` plus 128 lowercase ASCII hex digits. No trimming, case folding, tag resolution, truncation or default algorithm. Unknown algorithms are unsupported by this implementation, rather than claimed invalid under all OCI formats. See the [OCI descriptor specification](https://github.com/opencontainers/image-spec/blob/main/descriptor.md) for the wider extensible grammar. New algorithms require a reviewed contract/implementation change and test vectors; no silent fallback.

Content verification first compares the supplied descriptor byte size to the raw content length, then hashes the exact bytes using the declared supported algorithm. Negative size, size mismatch or hash mismatch returns false. It does not normalize JSON, text, line endings or encodings. Digest parsing alone is not content verification; a match is not proof of provenance, signed metadata, registry existence, permission or deployment approval. A deployable identity must additionally bind registry/repository, which is outside I1.

No setters/public constructor permit invalid/default construction. Parse errors are safe fixed messages; invalid input is not echoed to logs/exceptions. TryParse always sets a null result on failure. Repeated/concurrent calls are independent, use no mutable static state, external storage or I/O. There is no side-effect idempotency key, retry, timeout/compensation or migration for a pure bounded value operation. Future streaming/registry verification must define byte limits, cancellation, retries and trusted descriptor acquisition separately.
