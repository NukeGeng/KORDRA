using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kordra.Modules.IdentityAccess.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Primitives;

namespace Kordra.Api;

/// <summary>Explicit local-pilot HTTP surface for local self-authentication only.</summary>
public static class LocalAuthenticationEndpoints
{
    private const string CookieScheme = "KordraLocalSession";
    private const string SessionCookieName = "__Host-kordra.session";
    private const string PrincipalClaim = "kordra:principal-reference";
    private const string SessionClaim = "kordra:session-reference";
    private const string AuthenticatedAtClaim = "kordra:authenticated-at";
    private const string BootClaim = "kordra:boot-generation";
    private const string ValidationItem = "Kordra.LocalAuthentication.Validation";
    private const int MaximumBodyBytes = 8 * 1024;
    private static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(5);
    private const string Origin = "https://localhost:5128";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8
    };

    public static void AddServices(IServiceCollection services)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-Kordra-CSRF";
            options.Cookie.Name = "__Host-kordra.antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
                options.DefaultSignInScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                    OnValidatePrincipal = ValidatePrincipalAsync
                };
            });
    }

    public static Func<HttpContext, RequestDelegate, Task> RequestGuard(string authority) => async (context, next) =>
    {
        var path = context.Request.Path;
        var businessRequest = path.StartsWithSegments("/api/v1/targets", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/v2", StringComparison.OrdinalIgnoreCase);
        if (path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
        }

        if (!context.Request.IsHttps ||
            !string.Equals(context.Request.Host.Value, authority, StringComparison.Ordinal) ||
            context.Connection.LocalIpAddress is not { } localAddress || !IPAddress.IsLoopback(localAddress) ||
            context.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress) ||
            HasForwardedAuthority(context.Request.Headers))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "REQUEST_REJECTED", "Request could not be accepted.");
            return;
        }

        if (context.Request.Headers.TryGetValue("Origin", out var origins) &&
            (origins.Count != 1 || !string.Equals(origins[0], Origin, StringComparison.Ordinal)))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "REQUEST_REJECTED", "Request could not be accepted.");
            return;
        }

        if (context.Request.ContentLength > (businessRequest ? 128 * 1024 : MaximumBodyBytes))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is invalid.");
            return;
        }

        if (context.Request.Headers.ContainsKey("Transfer-Encoding") &&
            context.Request.ContentLength is not null)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is invalid.");
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsOptions(context.Request.Method) &&
            (!context.Request.Headers.TryGetValue("Origin", out origins) ||
             origins.Count != 1 || !string.Equals(origins[0], Origin, StringComparison.Ordinal)))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "REQUEST_REJECTED", "Request could not be accepted.");
            return;
        }

        var clientDisconnected = context.RequestAborted;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(clientDisconnected);
        deadline.CancelAfter(businessRequest ? TimeSpan.FromSeconds(10) : RequestDeadline);
        context.RequestAborted = deadline.Token;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !clientDisconnected.IsCancellationRequested)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                "AUTHORITY_UNAVAILABLE", "Authentication authority is unavailable.", CancellationToken.None);
        }
        finally
        {
            context.RequestAborted = clientDisconnected;
        }
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/auth/csrf", IssueCsrfAsync);
        app.MapPost("/api/v1/auth/login", LoginAsync);
        app.MapGet("/api/v1/auth/me", CurrentIdentityAsync);
        app.MapPost("/api/v1/auth/logout", LogoutAsync);
        app.MapPost("/api/v1/auth/logout-all", LogoutAllAsync);
        app.MapPost("/api/v1/auth/password", ChangePasswordAsync);
    }

    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var claims = context.Principal?.Claims.ToArray() ?? [];
        var principalClaims = claims.Where(claim => claim.Type == PrincipalClaim).ToArray();
        var sessionClaims = claims.Where(claim => claim.Type == SessionClaim).ToArray();
        var authTimeClaims = claims.Where(claim => claim.Type == AuthenticatedAtClaim).ToArray();
        var bootClaims = claims.Where(claim => claim.Type == BootClaim).ToArray();
        if (claims.Length != 4 || principalClaims.Length != 1 || sessionClaims.Length != 1 ||
            authTimeClaims.Length != 1 || bootClaims.Length != 1 ||
            !Guid.TryParseExact(principalClaims[0].Value, "D", out var principalId) || principalId == Guid.Empty ||
            !Guid.TryParseExact(bootClaims[0].Value, "D", out var bootGeneration) || bootGeneration == Guid.Empty ||
            !long.TryParse(authTimeClaims[0].Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out _) ||
            sessionClaims[0].Value.Length != 43)
        {
            context.HttpContext.Items[ValidationItem] = new LocalIdentityResult(LocalAuthenticationStatus.SessionInvalid);
            context.RejectPrincipal();
            return;
        }

        var service = context.HttpContext.RequestServices.GetRequiredService<LocalAuthenticationService>();
        LocalIdentityResult result;
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) &&
            context.HttpContext.Request.Path.Equals("/api/v1/auth/me", StringComparison.Ordinal))
        {
            var admission = await service.AdmitCurrentSessionAsync(
                principalId, sessionClaims[0].Value, bootGeneration, context.HttpContext.RequestAborted);
            result = admission.Result;
            if (result.Status == LocalAuthenticationStatus.Success)
            {
                context.HttpContext.Response.RegisterForDisposeAsync(admission);
            }
            else
            {
                await admission.DisposeAsync();
            }
        }
        else
        {
            result = await service.ResolveCurrentSessionAsync(
                principalId, sessionClaims[0].Value, bootGeneration, context.HttpContext.RequestAborted);
        }

        context.HttpContext.Items[ValidationItem] = result;
        if (result.Status != LocalAuthenticationStatus.Success)
        {
            context.RejectPrincipal();
        }
    }

    private static async Task IssueCsrfAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var response = new CsrfResponse(1, Guid.NewGuid(), tokens.RequestToken ?? string.Empty);
        await context.Response.WriteAsJsonAsync(response, JsonOptions, context.RequestAborted);
    }

    private static async Task LoginAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        LocalAuthenticationService authentication)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return;
        }

        var parsed = await TryReadJsonAsync<LoginRequest>(context);
        if (!parsed.Success)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is invalid.");
            return;
        }

        var login = await authentication.LoginAsync(parsed.Value!.Username, parsed.Value.Password, context.RequestAborted);
        if (login.Status != LocalAuthenticationStatus.Success || login.PrincipalId is null ||
            login.SessionReference is null || login.BootGeneration is null || login.AbsoluteExpiresAtUtc is null)
        {
            await WriteLoginFailureAsync(context, login.Status);
            return;
        }

        var authenticatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var identity = new ClaimsIdentity(
        [
            new Claim(PrincipalClaim, login.PrincipalId.Value.ToString("D")),
            new Claim(SessionClaim, login.SessionReference),
            new Claim(AuthenticatedAtClaim, authenticatedAt.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(BootClaim, login.BootGeneration.Value.ToString("D"))
        ], CookieScheme);
        var principal = new ClaimsPrincipal(identity);
        try
        {
            await context.SignInAsync(CookieScheme, principal, new AuthenticationProperties
            {
                ExpiresUtc = login.AbsoluteExpiresAtUtc,
                AllowRefresh = false,
                IsPersistent = false
            });
        }
        catch
        {
            context.Response.Cookies.Delete(SessionCookieName, new CookieOptions
            {
                Path = "/",
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                "AUTHORITY_UNAVAILABLE", "Authentication authority is unavailable.");
            return;
        }

        await context.Response.WriteAsJsonAsync(
            new LoginResponse(1, Guid.NewGuid(), login.PrincipalId.Value, login.AbsoluteExpiresAtUtc.Value),
            JsonOptions,
            context.RequestAborted);
    }

    private static async Task CurrentIdentityAsync(HttpContext context)
    {
        if (!TryGetIdentityResult(context, out var result))
        {
            await WriteUnauthenticatedAsync(context);
            return;
        }

        if (result.Status != LocalAuthenticationStatus.Success || result.PrincipalId is null ||
            result.UserName is null || result.AbsoluteExpiresAtUtc is null)
        {
            await WriteAuthenticationFailureAsync(context, result.Status);
            return;
        }

        await context.Response.WriteAsJsonAsync(
            new IdentityResponse(1, Guid.NewGuid(), result.PrincipalId.Value, result.UserName, result.AbsoluteExpiresAtUtc.Value),
            JsonOptions,
            context.RequestAborted);
    }

    private static async Task LogoutAsync(HttpContext context, IAntiforgery antiforgery, LocalAuthenticationService authentication)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return;
        }

        if (!await HasEmptyBodyAsync(context))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is invalid.");
            return;
        }

        if (!TryGetIdentityClaims(context, out var principalId, out var sessionReference, out var bootGeneration))
        {
            await WriteUnauthenticatedAsync(context);
            return;
        }

        var result = await authentication.LogoutAsync(principalId, sessionReference, bootGeneration, context.RequestAborted);
        if (result.Status != LocalAuthenticationStatus.Success)
        {
            await WriteCommandFailureAsync(context, result.Status);
            return;
        }

        await context.SignOutAsync(CookieScheme);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task LogoutAllAsync(HttpContext context, IAntiforgery antiforgery, LocalAuthenticationService authentication)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return;
        }

        if (!await HasEmptyBodyAsync(context))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is invalid.");
            return;
        }

        if (!TryGetIdentityClaims(context, out var principalId, out var sessionReference, out var bootGeneration))
        {
            await WriteUnauthenticatedAsync(context);
            return;
        }

        var idempotencyKey = GetSingleHeader(context.Request.Headers, "Idempotency-Key");
        var result = await authentication.LogoutAllAsync(
            principalId, sessionReference, bootGeneration, idempotencyKey, context.RequestAborted);
        if (result.Status != LocalAuthenticationStatus.Success)
        {
            await WriteCommandFailureAsync(context, result.Status);
            return;
        }

        await context.SignOutAsync(CookieScheme);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task ChangePasswordAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        LocalAuthenticationService authentication)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return;
        }

        var parsed = await TryReadJsonAsync<ChangePasswordRequest>(context);
        if (!parsed.Success)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request is invalid.");
            return;
        }

        if (!TryGetIdentityClaims(context, out var principalId, out var sessionReference, out var bootGeneration))
        {
            await WriteUnauthenticatedAsync(context);
            return;
        }

        var idempotencyKey = GetSingleHeader(context.Request.Headers, "Idempotency-Key");
        var result = await authentication.ChangePasswordAsync(
            principalId,
            sessionReference,
            bootGeneration,
            parsed.Value!.CurrentPassword,
            parsed.Value.NewPassword,
            idempotencyKey,
            context.RequestAborted);
        if (result.Status != LocalAuthenticationStatus.Success)
        {
            if (result.Status == LocalAuthenticationStatus.InvalidCredentials)
            {
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                    "AUTHENTICATION_FAILED", "Credentials could not be verified.");
                return;
            }

            await WriteCommandFailureAsync(context, result.Status);
            return;
        }

        await context.SignOutAsync(CookieScheme);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "REQUEST_REJECTED", "Request could not be accepted.");
            return false;
        }
    }

    private static async Task<(bool Success, T? Value)> TryReadJsonAsync<T>(HttpContext context) where T : class
    {
        if (!HasJsonContentType(context.Request.ContentType))
        {
            return (false, null);
        }

        var body = await ReadBoundedBodyAsync(context.Request, MaximumBodyBytes, context.RequestAborted);
        if (body is null || body.Length == 0)
        {
            return (false, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, null);
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!propertyNames.Add(property.Name))
                {
                    return (false, null);
                }
            }

            var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return (value is not null, value);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream(Math.Min(maximumBytes + 1, 4096));
        var chunk = new byte[2048];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, maximumBytes + 1 - (int)buffer.Length)), cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            if (buffer.Length > maximumBytes)
            {
                return null;
            }
        }
    }

    private static async Task<bool> HasEmptyBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            var body = await ReadBoundedBodyAsync(context.Request, 0, context.RequestAborted);
            return body is { Length: 0 };
        }

        return true;
    }

    private static bool HasJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var pieces = contentType.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Equals(pieces[0], "application/json", StringComparison.OrdinalIgnoreCase) &&
            pieces.Skip(1).All(piece => string.Equals(piece, "charset=utf-8", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetIdentityResult(HttpContext context, out LocalIdentityResult result)
    {
        if (context.Items.TryGetValue(ValidationItem, out var stored) && stored is LocalIdentityResult identityResult)
        {
            result = identityResult;
            return true;
        }

        result = new LocalIdentityResult(LocalAuthenticationStatus.SessionInvalid);
        return context.User.Identity?.IsAuthenticated == true;
    }

    public static Kordra.Modules.IdentityAccess.Contracts.SessionSelection? CurrentSessionSelection(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && TryGetIdentityClaims(context, out var principal, out var reference, out var boot)
            ? new(principal, reference, boot) : null;

    private static bool TryGetIdentityClaims(
        HttpContext context,
        out Guid principalId,
        out string sessionReference,
        out Guid bootGeneration)
    {
        principalId = Guid.Empty;
        sessionReference = string.Empty;
        bootGeneration = Guid.Empty;
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claims = context.User.Claims.ToArray();
        if (claims.Length != 4 ||
            !Guid.TryParseExact(SingleClaim(claims, PrincipalClaim), "D", out principalId) || principalId == Guid.Empty ||
            (sessionReference = SingleClaim(claims, SessionClaim) ?? string.Empty).Length != 43 ||
            !Guid.TryParseExact(SingleClaim(claims, BootClaim), "D", out bootGeneration) || bootGeneration == Guid.Empty ||
            !long.TryParse(SingleClaim(claims, AuthenticatedAtClaim), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            principalId = Guid.Empty;
            sessionReference = string.Empty;
            bootGeneration = Guid.Empty;
            return false;
        }

        return true;
    }

    private static string? SingleClaim(IEnumerable<Claim> claims, string type)
    {
        var matches = claims.Where(claim => claim.Type == type).ToArray();
        return matches.Length == 1 ? matches[0].Value : null;
    }

    private static string? GetSingleHeader(IHeaderDictionary headers, string name)
    {
        StringValues values = headers[name];
        return values.Count == 1 ? values[0] : null;
    }

    private static bool HasForwardedAuthority(IHeaderDictionary headers) =>
        headers.ContainsKey("Forwarded") || headers.ContainsKey("X-Forwarded-For") ||
        headers.ContainsKey("X-Forwarded-Host") || headers.ContainsKey("X-Forwarded-Proto") ||
        headers.ContainsKey("X-Forwarded-Port") || headers.ContainsKey("X-Forwarded-Prefix");

    private static async Task WriteLoginFailureAsync(HttpContext context, LocalAuthenticationStatus status)
    {
        switch (status)
        {
            case LocalAuthenticationStatus.InvalidCredentials:
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                    "AUTHENTICATION_FAILED", "Credentials could not be verified.");
                break;
            case LocalAuthenticationStatus.RateLimited:
                context.Response.Headers.RetryAfter = "60";
                await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests,
                    "RATE_LIMITED", "Try again later.");
                break;
            case LocalAuthenticationStatus.SessionLimitReached:
                await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                    "SESSION_LIMIT_REACHED", "The active session limit has been reached.");
                break;
            case LocalAuthenticationStatus.InvalidRequest:
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                    "INVALID_REQUEST", "Request is invalid.");
                break;
            default:
                await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "AUTHORITY_UNAVAILABLE", "Authentication authority is unavailable.");
                break;
        }
    }

    private static async Task WriteUnauthenticatedAsync(HttpContext context)
    {
        if (context.Items.TryGetValue(ValidationItem, out var stored) &&
            stored is LocalIdentityResult { Status: LocalAuthenticationStatus.AuthorityUnavailable })
        {
            await WriteAuthenticationFailureAsync(context, LocalAuthenticationStatus.AuthorityUnavailable);
            return;
        }

        await WriteAuthenticationFailureAsync(context, LocalAuthenticationStatus.SessionInvalid);
    }

    private static async Task WriteAuthenticationFailureAsync(HttpContext context, LocalAuthenticationStatus status)
    {
        if (status == LocalAuthenticationStatus.AuthorityUnavailable)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                "AUTHORITY_UNAVAILABLE", "Authentication authority is unavailable.");
        }
        else
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "SESSION_INVALID", "Authentication is required.");
        }
    }

    private static async Task WriteCommandFailureAsync(HttpContext context, LocalAuthenticationStatus status)
    {
        switch (status)
        {
            case LocalAuthenticationStatus.SessionInvalid:
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                    "SESSION_INVALID", "Authentication is required.");
                break;
            case LocalAuthenticationStatus.InvalidRequest:
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                    "INVALID_REQUEST", "Request is invalid.");
                break;
            case LocalAuthenticationStatus.Conflict:
                await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                    "REQUEST_CONFLICT", "The request conflicts with current state.");
                break;
            case LocalAuthenticationStatus.RateLimited:
                context.Response.Headers.RetryAfter = "60";
                await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests,
                    "RATE_LIMITED", "Try again later.");
                break;
            default:
                await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "AUTHORITY_UNAVAILABLE", "Authentication authority is unavailable.");
                break;
        }
    }

    private static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        CancellationToken? cancellationToken = null)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new ErrorResponse(1, Guid.NewGuid(), code, message), JsonOptions,
            cancellationToken ?? context.RequestAborted);
    }

    private sealed record CsrfResponse(int SchemaVersion, Guid CorrelationId, string RequestToken);
    private sealed record LoginResponse(int SchemaVersion, Guid CorrelationId, Guid PrincipalId, DateTimeOffset SessionExpiresAtUtc);
    private sealed record IdentityResponse(int SchemaVersion, Guid CorrelationId, Guid PrincipalId, string Username, DateTimeOffset SessionExpiresAtUtc);
    private sealed record ErrorResponse(int SchemaVersion, Guid CorrelationId, string Code, string Message);
    private sealed record LoginRequest(string? Username, string? Password);
    private sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
}
