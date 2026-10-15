---
id: EXP-LAB-DATA
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# LAB-01 — synthetic fixtures and privacy

## Dataset classes
- OCI artifact: `test-app` with immutably addressed digest and intentionally broken health variant.
- Docker Compose fixture: 2 dummy services, no shared port, no actual business backend.
- Helm chart fixture: deployment, service, readiness probe, no persistent user data; controlled version N/N-1.
- MQTT fixture: synthetic `Traffic/v1/SBTEST0001/Init` and `/Monitor`, fake serial, no production keys, synthetic timestamp; AES/Base64 simulation uses **test-only** key not stored in git.
- RabbitMQ fixture: `test.ci.event.v1` in private vhost, publisher + consumer + DLQ; never reuse business exchange/queues.
- PostgreSQL fixture: isolated DB/schema without production data; migration rollback tested separately.
- Security fixture: org-A/org-B, project-A/project-B, prod/staging resources, org admin, release approver, viewer, platform operator; role permissions are config-driven.

## Evidence handling
Redact serial numbers, user names, emails, IPs, credentials, JWT, complete kubeconfig and tokens; retain only hashes/aggregate metrics where possible. Store sanitized message payload sample and explicit schema ID; never copy real device secrets.

## Reproducibility
For each fixture record chart/image digest, manifest hash, schema version, tool commit, Kubernetes version and isolated environment fingerprint. Fixtures must be deterministic, support setup/teardown and bounded resource quotas.
