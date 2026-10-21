namespace Kordra.Modules.IdentityAccess.Authentication;

public enum LocalAuthenticationStatus
{
    Success,
    InvalidRequest,
    InvalidCredentials,
    RateLimited,
    SessionLimitReached,
    SessionInvalid,
    Conflict,
    AuthorityUnavailable
}

public sealed record LocalLoginResult(
    LocalAuthenticationStatus Status,
    Guid? PrincipalId = null,
    string? UserName = null,
    string? SessionReference = null,
    Guid? BootGeneration = null,
    DateTimeOffset? AbsoluteExpiresAtUtc = null);

public sealed record LocalIdentityResult(
    LocalAuthenticationStatus Status,
    Guid? PrincipalId = null,
    string? UserName = null,
    DateTimeOffset? AbsoluteExpiresAtUtc = null);

public sealed record LocalCommandResult(
    LocalAuthenticationStatus Status,
    Guid? PrincipalId = null,
    Guid? CorrelationId = null);

public sealed record LocalAuthenticationConfiguration(
    Guid InstallationId,
    Guid BootGeneration,
    int PasswordHashIterations = 220_000,
    TimeSpan? AbsoluteSessionLifetime = null,
    TimeSpan? IdleSessionLifetime = null,
    int MaximumActiveSessions = 5)
{
    public TimeSpan AbsoluteLifetime => AbsoluteSessionLifetime ?? TimeSpan.FromHours(8);
    public TimeSpan IdleLifetime => IdleSessionLifetime ?? TimeSpan.FromMinutes(30);
}
