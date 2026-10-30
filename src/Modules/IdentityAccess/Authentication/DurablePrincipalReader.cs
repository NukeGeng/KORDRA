using System.Security.Cryptography;
using System.Text;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Authentication;

/// <summary>Reads IAM principal facts without loading a session or password, and never returns the raw security stamp.</summary>
public sealed class DurablePrincipalReader(IDbContextFactory<KordraIdentityDbContext> contexts) : IDurablePrincipalReader
{
    public async Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty) throw new ArgumentException("Principal identity is required.", nameof(principalId));
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Principals.AsNoTracking().Where(x => x.Id == principalId)
            .Select(x => new { x.Id, x.Revision, x.AccountStatus, x.SecurityStamp }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        if (row.Revision <= 0 || row.AccountStatus is not ("active" or "disabled") || string.IsNullOrWhiteSpace(row.SecurityStamp))
            throw new InvalidDataException("IAM principal evidence is malformed.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("iam-durable-security-v1\n" + row.SecurityStamp));
        return new(row.Id, row.Revision, row.AccountStatus == "active", Convert.ToHexStringLower(hash));
    }
}
