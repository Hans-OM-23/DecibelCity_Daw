using DecibelCity.Api.Configuration;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Services;
using DecibelCity.Api.Services.Security;
using Microsoft.Extensions.DependencyInjection;

namespace DecibelCity.Api.Filters;

/// <summary>
/// Protección CSRF de los POST con cookies: si llega <c>Origin</c> (navegadores), debe coincidir
/// exactamente con el origen de la propia API o con un origen CORS configurado. Los clientes sin
/// <c>Origin</c> (herramientas de API) pasan. Se ejecuta antes de leer el cuerpo y de cualquier efecto.
/// </summary>
/// <param name="options">Configuración (orígenes CORS).</param>
public sealed class OriginValidationFilter(DecibelCityOptions options) : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var request = context.HttpContext.Request;
        if (request.Headers.Origin.Count > 0)
        {
            var origin = request.Headers.Origin.ToString();
            var self = $"{request.Scheme}://{request.Host.Value}";
            if (!string.Equals(origin, self, StringComparison.Ordinal)
                && !options.CorsOrigins.Contains(origin, StringComparer.Ordinal))
            {
                throw new AuthException(StatusCodes.Status403Forbidden, ErrorMessages.ForbiddenOrigin);
            }
        }

        return next(context);
    }
}

/// <summary>
/// Exige una cookie de sesión vigente y relee la cuenta y su rol en la base. El perfil del navegador,
/// <c>sessionStorage</c> o cabeceras como <c>Authorization</c> no autorizan nada.
/// </summary>
/// <param name="sessions">Almacén de sesiones.</param>
/// <param name="authService">Lectura del perfil activo.</param>
public sealed class ActiveUserFilter(ISessionStore sessions, IAuthService authService) : IEndpointFilter
{
    private const string ProfileKey = "decibelcity.profile";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var idUsuario = sessions.Resolve(http.Request.Cookies[SessionCookie.Name])
                        ?? throw new AuthException(StatusCodes.Status401Unauthorized, ErrorMessages.LoginRequired);
        http.Items[ProfileKey] = await authService.GetActiveProfileAsync(idUsuario, http.RequestAborted).ConfigureAwait(false);
        return await next(context).ConfigureAwait(false);
    }

    /// <summary>Perfil verificado por el filtro para la petición actual.</summary>
    /// <param name="http">Contexto HTTP.</param>
    /// <returns>Perfil activo.</returns>
    public static UserProfile CurrentProfile(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items[ProfileKey] as UserProfile
               ?? throw new InvalidOperationException("El filtro de sesión no se ejecutó para este endpoint.");
    }
}

/// <summary>Emisión y borrado de la cookie de sesión opaca.</summary>
public static class SessionCookies
{
    /// <summary>
    /// Emite una sesión nueva (revocando la cookie anterior) con <c>HttpOnly</c>, <c>SameSite=Lax</c>,
    /// <c>Path=/api</c>, 8 horas y <c>Secure</c> si la petición es HTTPS (tras <c>UseForwardedHeaders</c>).
    /// El token solo viaja en la cookie: nunca en el cuerpo ni en registros.
    /// </summary>
    /// <param name="http">Contexto HTTP.</param>
    /// <param name="sessions">Almacén de sesiones.</param>
    /// <param name="idUsuario">Cuenta autenticada.</param>
    public static void Issue(HttpContext http, ISessionStore sessions, int idUsuario)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(sessions);
        var token = sessions.Create(idUsuario, http.Request.Cookies[SessionCookie.Name]);
        http.Response.Cookies.Append(SessionCookie.Name, token, Options(http, SessionCookie.Lifetime));
    }

    /// <summary>Borra la cookie en el navegador (<c>Max-Age=0</c>).</summary>
    /// <param name="http">Contexto HTTP.</param>
    public static void Clear(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var options = Options(http, TimeSpan.Zero);
        options.Expires = DateTimeOffset.UnixEpoch;
        http.Response.Cookies.Append(SessionCookie.Name, string.Empty, options);
    }

    private static CookieOptions Options(HttpContext http, TimeSpan maxAge)
    {
        var configuredSameSite = http.RequestServices.GetService<DecibelCityOptions>()?.CookieSameSite;
        var isCrossSiteNone = string.Equals(configuredSameSite, "None", StringComparison.OrdinalIgnoreCase);

        var sameSite = isCrossSiteNone ? SameSiteMode.None
            : string.Equals(configuredSameSite, "Strict", StringComparison.OrdinalIgnoreCase) ? SameSiteMode.Strict
            : SameSiteMode.Lax;

        // SameSite=None exige estrictamente Secure=true en los navegadores (RFC 6265bis).
        var secure = isCrossSiteNone || http.Request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = sameSite,
            Path = SessionCookie.Path,
            MaxAge = maxAge,
            Secure = secure,
            IsEssential = true,
        };
    }
}
