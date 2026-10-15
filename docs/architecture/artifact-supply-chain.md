---
id: ARCH-ARTIFACT
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Immutable artifact & software supply chain

## Artifact identity
OCI image deployable identity is **registry/repository + content digest**. Tags are mutable references for discovery only; approval pins digest and deploy spec hash. For other artifact types store cryptographic hash, source revision, producer identity and content location.

## Admission checks
Validate digest resolvability, target architecture/platform, signature/attestation policy where supported, vulnerability policy, expiration, registry access; store attestation references, not claims of verification without evidence.

## Trust boundaries
CI publishing artifact ≠ deployment approval. Do not accept arbitrary `latest` or re-resolve mutable tag after approval; reject if digest does not match. Repo/CI webhooks verify signature and deduplicate delivery.

## Proposed future
SBOM/provenance policy (SLSA/Sigstore integration) may be added through ADR. Avoid making it a blocker for first local prototype but preserve metadata fields.
