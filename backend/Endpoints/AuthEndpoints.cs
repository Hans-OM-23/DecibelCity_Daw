using DecibelCity.Api.Errors;
using DecibelCity.Api.Filters;
using DecibelCity.Api.Infrastructure;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Services;
using DecibelCity.Api.Services.Integrations;
using DecibelCity.Api.Services.Security;
using DecibelCity.Api.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DecibelCity.Api.Endpoints;

/// <summary>Cuentas, sesiones y catálogo territorial.</summary>
public static class AuthEndpoints
{
    /// <summary>Registra los endpoints de cuentas.</summary>
    /// <param name="routes">Constructor de rutas.</param>
    /// <returns>El mismo constructor.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api").WithTags("Usuarios");

        api.MapGet("/territories", ListTerritoriesAsync)
            .WithName("ListTerritories")
            .WithSummary("Catálogo territorial con jerarquía activa (Junín, INEI)");

        var auth = api.MapGroup("/auth");

        auth.MapGet("/config", (ITurnstileVerifier turnstile) =>
                TypedResults.Ok(new AuthConfigResponse(turnstile.IsEnabled, turnstile.SiteKey)))
            .WithName("AuthConfig")
            .WithSummary("Configuración pública de autenticación (Turnstile)");

        auth.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<OriginValidationFilter>()
            .RequireRateLimiting(RateLimitPolicies.Register)
            .Accepts<UserRegistration>("application/json")
            .WithName("Register")
            .WithSummary("Registro de cuenta (nueve campos); emite la cookie de sesión")
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ValidationErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        auth.MapPost("/login", LoginAsync)
            .AddEndpointFilter<OriginValidationFilter>()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .Accepts<LoginCredentials>("application/json")
            .WithName("Login")
            .WithSummary("Inicio de sesión (Argon2id; Turnstile si está activo); emite la cookie de sesión")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ValidationErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests);

        auth.MapGet("/session", (HttpContext http) => TypedResults.Ok(ActiveUserFilter.CurrentProfile(http)))
            .AddEndpointFilter<ActiveUserFilter>()
            .WithName("Session")
            .WithSummary("Perfil de la sesión vigente")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        auth.MapPost("/logout", (HttpContext http, ISessionStore sessions) =>
            {
                sessions.Revoke(http.Request.Cookies[SessionCookie.Name]);
                SessionCookies.Clear(http);
                return TypedResults.NoContent();
            })
            .AddEndpointFilter<OriginValidationFilter>()
            .WithName("Logout")
            .WithSummary("Revoca la sesión en memoria y borra la cookie")
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        return routes;
    }

    private static async Task<Ok<TerritoryCatalog>> ListTerritoriesAsync(IAuthService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListTerritoriesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Created<UserProfile>> RegisterAsync(
        HttpContext http, IAuthService service, ISessionStore sessions, CancellationToken cancellationToken)
    {
        var registration = RequestValidators.Registration(await JsonBody.ReadAsync(http.Request, cancellationToken).ConfigureAwait(false));
        var created = await service.RegisterAsync(registration, cancellationToken).ConfigureAwait(false);
        var profile = await service.GetActiveProfileAsync(created.IdUsuario, cancellationToken).ConfigureAwait(false);
        SessionCookies.Issue(http, sessions, profile.IdUsuario);
        return TypedResults.Created("/api/auth/session", profile);
    }

    private static async Task<Ok<UserProfile>> LoginAsync(
        HttpContext http,
        IAuthService service,
        ISessionStore sessions,
        ITurnstileVerifier turnstile,
        CancellationToken cancellationToken)
    {
        var credentials = RequestValidators.Login(await JsonBody.ReadAsync(http.Request, cancellationToken).ConfigureAwait(false));
        if (turnstile.IsEnabled)
        {
            var remoteIp = http.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrEmpty(credentials.TurnstileToken)
                || !await turnstile.VerifyAsync(credentials.TurnstileToken, remoteIp, cancellationToken).ConfigureAwait(false))
            {
                throw new AuthException(StatusCodes.Status422UnprocessableEntity, ErrorMessages.TurnstileFailed);
            }
        }

        var authenticated = await service.LoginAsync(credentials, cancellationToken).ConfigureAwait(false);
        var profile = await service.GetActiveProfileAsync(authenticated.IdUsuario, cancellationToken).ConfigureAwait(false);
        SessionCookies.Issue(http, sessions, profile.IdUsuario);
        return TypedResults.Ok(profile);
    }
}
