namespace DecibelCity.Api.Middleware;

/// <summary>
/// Añade <c>Cache-Control: no-store</c> a toda respuesta de <c>/api/auth/*</c> (incluidos errores,
/// 422 y 429): los perfiles y las cookies de sesión no deben guardarse en cachés.
/// </summary>
/// <param name="next">Siguiente middleware.</param>
public sealed class AuthCacheControlMiddleware(RequestDelegate next)
{
    private static readonly PathString AuthPrefix = new("/api/auth");

    /// <summary>Procesa la petición.</summary>
    /// <param name="context">Contexto HTTP.</param>
    /// <returns>Tarea asíncrona.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Path.StartsWithSegments(AuthPrefix, StringComparison.OrdinalIgnoreCase, out var remaining)
            && remaining.HasValue)
        {
            context.Response.OnStarting(static state =>
            {
                ((HttpResponse)state).Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            }, context.Response);
        }

        return next(context);
    }
}
