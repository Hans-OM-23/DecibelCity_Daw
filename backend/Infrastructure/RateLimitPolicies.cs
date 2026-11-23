using System.Globalization;
using System.Threading.RateLimiting;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;

namespace DecibelCity.Api.Infrastructure;

/// <summary>Políticas de limitación por IP (tras <c>UseForwardedHeaders</c>, la IP es la del cliente real).</summary>
public static class RateLimitPolicies
{
    /// <summary>Inicio de sesión.</summary>
    public const string Login = "auth-login";

    /// <summary>Registro.</summary>
    public const string Register = "auth-register";

    /// <summary>Reportes.</summary>
    public const string Reports = "reports";

    /// <summary>Registra el limitador nativo de ASP.NET Core con ventanas fijas de un minuto por IP.</summary>
    /// <param name="services">Servicios.</param>
    /// <returns>Los mismos servicios.</returns>
    public static IServiceCollection AddDecibelCityRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(Login, http => Partition(http, RateLimitPolicies.Login, o => o.LoginPerMinute));
            limiter.AddPolicy(Register, http => Partition(http, RateLimitPolicies.Register, o => o.RegisterPerMinute));
            limiter.AddPolicy(Reports, http => Partition(http, RateLimitPolicies.Reports, o => o.ReportsPerMinute));
            limiter.OnRejected = static async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await ApiExceptionHandler.WriteDetailAsync(context.HttpContext, ErrorMessages.TooManyRequests, cancellationToken)
                    .ConfigureAwait(false);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext http, string policy, Func<RateLimitOptions, int> permits)
    {
        var options = http.RequestServices.GetRequiredService<DecibelCityOptions>().RateLimit;
        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter(policy);
        }

        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var limit = permits(options);
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{limit}:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
