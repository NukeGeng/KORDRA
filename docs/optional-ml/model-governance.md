---
id: ML-MODEL
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Model registry, evaluation & deployment safety

## Model record
`modelId`, version, intended purpose, architecture and training code, dataset lineage, evaluation protocol, metrics with uncertainty, known weaknesses, bias/fairness considerations, thresholds/calibration, accepted use cases, monitoring/drift plan, rollback, owner and approval.

## Deployment
Only deploy pinned model weights + preprocessing + threshold/config hashes; model and pipeline must be versioned together. Regression tests for schema/feature drift, dataset imbalance, class leakage and real-world failure samples (do not rely solely on synthetic data).

## VietAIS-specific consideration
AI signals assist human review; do not equate a model score with a final misconduct finding. Evidence handling, privacy and appeal are separate domain features.
