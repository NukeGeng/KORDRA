using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>
/// Read-only owner observation. Caller must supply trusted scope, an independently opened reader and terminal-backend proof.
/// This component neither proves those external facts nor grants authority/replay, even for a matching row.
/// </summary>
public sealed class WorkflowReceiptInspector : IWorkflowReceiptInspector, IAsyncDisposable
{
    private readonly WorkflowDbContext _context;
    private WorkflowReceiptInspector(WorkflowDbContext context) => _context = context;
    public static WorkflowReceiptInspector CreateIsolatedTestInspector(NpgsqlConnection independent, Guid caseId)
        => new(WorkflowDbContext.CreateFixture(independent, caseId));

    public async Task<WorkflowReceiptInspectionStatus> InspectAsync(WorkflowReceiptExpectation expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ct.ThrowIfCancellationRequested();
        if (_context.Database.GetDbConnection().State != ConnectionState.Open || _context.Database.CurrentTransaction is not null)
            return WorkflowReceiptInspectionStatus.Unavailable;
        try
        {
            var identity = expected.Identity;
            var row = await _context.Set<WorkflowInstanceRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
            if (row is null) return WorkflowReceiptInspectionStatus.StillUnknown;
            if (row.RequestId != expected.RequestId || !SameHash(row.SpecificationHash, expected.SpecificationHash))
                return WorkflowReceiptInspectionStatus.Conflict;
            var canonical = WorkflowSpecification.ReadCanonical(row.Specification);
            if (canonical.InstallationId != identity.InstallationId || canonical.TenantId != identity.TenantId ||
                canonical.InstanceId != identity.InstanceId || canonical.RequestId != expected.RequestId ||
                !SameHash(canonical.SpecificationHash, expected.SpecificationHash) || row.Version < expected.ResultingVersion)
                return WorkflowReceiptInspectionStatus.Conflict;
            if (expected.OperationId is { } operation)
            {
                var transition = await _context.Set<WorkflowTransitionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
                    x.InstanceId == identity.InstanceId && x.OperationId == operation, ct);
                if (transition is null) return WorkflowReceiptInspectionStatus.StillUnknown;
                return transition.StepId == expected.StepId && transition.ResultingVersion == expected.ResultingVersion &&
                    SameHash(transition.SpecificationHash, expected.SpecificationHash)
                    ? WorkflowReceiptInspectionStatus.MatchingObserved : WorkflowReceiptInspectionStatus.Conflict;
            }
            var receipt = await _context.Set<WorkflowRequestReceiptRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.RequestId == expected.RequestId, ct);
            if (receipt is null) return WorkflowReceiptInspectionStatus.StillUnknown;
            if (receipt.InstanceId != identity.InstanceId || !SameHash(receipt.ContentHash, expected.SpecificationHash))
                return WorkflowReceiptInspectionStatus.Conflict;
            using var result = JsonDocument.Parse(receipt.ResultJson);
            var fields = result.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
            if (fields.Length != 2 || fields.Distinct(StringComparer.Ordinal).Count() != 2 ||
                !fields.Contains("instanceId", StringComparer.Ordinal) || !fields.Contains("version", StringComparer.Ordinal))
                return WorkflowReceiptInspectionStatus.Unavailable;
            return result.RootElement.GetProperty("instanceId").GetGuid() == identity.InstanceId &&
                result.RootElement.GetProperty("version").GetInt64() == expected.ResultingVersion
                ? WorkflowReceiptInspectionStatus.MatchingObserved : WorkflowReceiptInspectionStatus.Conflict;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or FormatException or System.Data.Common.DbException)
        { return WorkflowReceiptInspectionStatus.Unavailable; }
    }

    private static bool SameHash(string first, string second) => first is { Length: 64 } && second is { Length: 64 } &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
    public ValueTask DisposeAsync() => _context.DisposeAsync();
}
