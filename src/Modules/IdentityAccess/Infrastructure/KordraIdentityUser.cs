using Microsoft.AspNetCore.Identity;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>ASP.NET Identity credential fields owned by MOD-IAM.</summary>
public sealed class KordraIdentityUser : IdentityUser<Guid>
{
    public KordraIdentityUser()
    {
        Id = Guid.NewGuid();
    }

    public string AccountStatus { get; set; } = "active";
    public long Revision { get; set; } = 1;
}
