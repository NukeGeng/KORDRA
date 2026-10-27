---
id: OPS-ARTIFACT-REGISTRATION-V1
status: proposed
owner: NukeGeng
last_reviewed: 2026-10-27
---

# Artifact registration v1 — local operation

This describes the accepted [artifact contract](../contracts/artifact-registration-v1.md) and in-progress local implementation. Synthetic tests do not authorize migration of an existing installation, use of existing secrets or connection to a registry.

## Activation

`KORDRA_ARTIFACT_CONNECTION_STRING` selects the private loopback `kordra` database as `kordra_app`. Set `KORDRA_ARTIFACT_REGISTRY_CONFIGURATION_FILE` and `KORDRA_ARTIFACT_REGISTRY_CONFIGURATION_SHA256` together to enable new registration and registry selectors. The configuration is a private regular file; the SHA-256 covers its exact bytes. Partial, malformed or mismatched configuration fails startup. Without registry configuration, current-authorized historical catalog reads and receipt replay remain available; new registration and selectors return unavailable. Startup neither migrates nor reads credentials.

The local HTTPS service uses its existing session and `X-Kordra-CSRF` header. Grant `artifact.read` and `artifact.register` explicitly on the project. A registry reference or idempotency key is not a permission. Registration verifies manifest/config content only; layer availability, signatures and declared provenance remain unverified. Registry access is bounded and read-only.

Follow [local PostgreSQL setup](local-postgresql-setup.md) and the credential runbook for separately approved offline migrations; do not migrate an existing installation solely on the basis of this document. ART's down migration refuses to erase immutable catalog/history.

## Interrupted registration

Recovery does not run on startup. Stop the API and establish the exact installation, registration, boot, invocation and old process identity from trustworthy local evidence. The private JSON plan has exactly `schemaVersion`, `planId`, `installationId`, `registrationId`, `bootId`, `invocationId`, `oldProcessId`, `receiptHash`, and `auditHash`. Initially the two hash fields are `null`. IDs are nonempty lowercase canonical GUIDs. `oldProcessId` is the old API process ID, not the recovery command's PID. If the old process identity or helper termination is uncertain, stop for investigation.

With the stopped installation lock and a private admin locator file, inspect:

```sh
dotnet run --project src/Host/Kordra.Api -- artifact-recovery inspect PRIVATE_PLAN PRIVATE_ADMIN_LOCATOR
```

The output contains a bound `plan` and `approvedSha256`. Store the exact `plan` object in a private file, inspect its receipt/audit evidence, and obtain maintainer approval for the hash before applying:

```sh
dotnet run --project src/Host/Kordra.Api -- artifact-recovery apply PRIVATE_BOUND_PLAN PRIVATE_ADMIN_LOCATOR APPROVED_SHA256
```

The command loads only the installation identity/lock, private plan and admin database locator. It does not load the API's TLS key, registry configuration or Keychain. It verifies custody, the old PID and absence of other `Kordra.Api` processes, then checks ART/AUD evidence. A confirmed Pending attempt without a committed M can become `Abandoned` in one ART/AUD/clock-floor transaction. Confirmed Registered and Failed records remain unchanged; ambiguous history is fenced for investigation. A lost commit acknowledgement is not retried automatically; reconcile authoritative state with the same reviewed plan after the process is stopped. A subsequent attempt needs a new explicit key. Recovery contacts no registry.

The process check is deliberately conservative: another `Kordra.Api` process or PID reuse may block recovery. Do not terminate unrelated processes. This is local single-process recovery, not a distributed worker recovery guarantee.
