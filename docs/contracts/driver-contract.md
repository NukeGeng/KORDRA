---
id: CTR-DRIVER
status: proposed
owner: unassigned
last_reviewed: 2026-10-15
---

# Deployment driver interface & capability contract

## Responsibilities
Driver implements capability-specific `ValidateTarget`, `Plan`, `Execute`, `Observe`, `Rollback` (only if supported). No driver decides policy, membership or whether approval is valid; worker verifies signed approved operation.

```csharp
public interface IDeploymentDriver
{
    string DriverType { get; }
    DriverCapabilities Capabilities { get; }
    Task<PlanResult> PlanAsync(DeploymentSpec spec, CancellationToken ct);
    Task<OperationReceipt> ExecuteAsync(ApprovedExecution context, CancellationToken ct);
    Task<ObservedDeployment> ObserveAsync(TargetRef target, CancellationToken ct);
    Task<RollbackReceipt> RollbackAsync(ApprovedRollback context, CancellationToken ct);
}
```

## Capability matrix (proposed; validate in conformance tests)
| Capability | Helm | Docker Compose via SSH | Argo CD |
|---|---|---|---|
| plan/diff | supported with limitations | depends on host/compose versions | diff supported |
| immutable artifact pin | yes | yes | through Git desired state |
| observe rollout | K8s API/Helm | container/services/health | app sync+health |
| native revision rollback | `helm rollback` | redeploy prior digest/compose config | Git revert + sync; do not assume direct rollback |
| drift auto-remediate | only if direct owner | if direct owner | **external GitOps owner** |

## Error taxonomy
`TransientConnectivity`, `CapabilityUnsupported`, `PolicyDenied`, `TargetConflict`, `OperationUnknown`, `PartialApply`, `VerificationFailed`, `CredentialExpired`. Operations return safe diagnostics, nonsecret structured evidence.

## Conformance tests
Plan→execute→observe, duplicate operationId, crash-after-apply-before-receipt, revoked identity, unsupported capability, rollback/compensation, audit telemetry, timeout classification.
