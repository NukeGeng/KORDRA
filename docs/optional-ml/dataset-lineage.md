---
id: ML-DATA
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Dataset lineage, parameters and reproducibility

## Required dataset registry fields
`datasetId`, version, source/license, collection time window, creator/owner, consent/legal basis, sampling criteria, data classification, schema, preprocessing code commit, train/val/test split procedure and seed, known leakage risk, quality metrics, artifact hash, retention/deletion.

## Training run linkage
`trainingRunId → code commit + image digest + datasetIds/versions/hashes + feature extraction config + hyperparameters + random seeds + compute environment + checkpoint + metrics + evaluator`. Store raw data only in governed storage, docs only metadata/references.

## Security
Annotate personally identifying/sensitive footage, access controls, anonymization and policy approval; require lawful basis and deletion process. Do not publish images or sensitive examples in public OSS docs.
