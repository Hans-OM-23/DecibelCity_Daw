using System.Net;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Filters;
using DecibelCity.Api.Middleware;
using DecibelCity.Api.Repositories;
using DecibelCity.Api.Services;
using DecibelCity.Api.Services.Integrations;
using DecibelCity.Api.Services.Osm;
using DecibelCity.Api.Services.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace DecibelCity.Api.Infrastructure;

/// <summary>Composición de dependencias y del pipeline HTTP.</summary>
public static class ServiceRegistration
{
    /// <summary>Registra configuración, datos, servicios, clientes HTTP, CORS, cabeceras reenviadas y limitación.</summary>
    /// <param name="services">Servicios.</param>
    /// <returns>Los mismos servicios.</returns>
    public static IServiceCollection AddDecibelCity(this IServiceCollection services)
    {
        // Las opciones se resuelven tarde para que las pruebas puedan sustituir la configuración.
        services.AddSingleton(sp => DecibelCityOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(TimeProvider.System);

        services.ConfigureHttpJsonOptions(json => ApiJson.Configure(json.SerializerOptions));
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddOpenApi();
        services.AddMemoryCache();

        // Datos: un único NpgsqlDataSource (pool) con search_path y zona horaria UTC.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<DecibelCityOptions>();
            var builder = new NpgsqlDataSourceBuilder(DatabaseConnection.BuildConnectionString(options.DatabaseUrl, options.DatabaseSchema));
            return builder.Build();
        });
        services.AddSingleton<IDatabaseInitializer, PostgresDatabaseInitializer>();
        services.AddSingleton<INoiseRepository, PostgresNoiseRepository>();
        services.AddSingleton<IUserRepository, PostgresUserRepository>();
        services.AddHostedService<DatabaseInitializerHostedService>();

        // Seguridad: sesiones de un solo proceso y Argon2id compatible con argon2-cffi.
        services.AddSingleton<ISessionStore>(sp => new MemorySessionStore(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<DummyPasswordHash>();
        services.AddScoped<OriginValidationFilter>();
        services.AddScoped<ActiveUserFilter>();

        // Servicios de dominio.
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<INoiseService, NoiseService>();

        // OpenStreetMap: el throttle es único y compartido por /lookup y /reverse.
        services.AddSingleton(sp => new NominatimThrottle(
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<DecibelCityOptions>().NominatimMinInterval));
        services.AddHttpClient<NominatimClient>(HttpClientNames.Nominatim, client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        services.AddTransient<IPlaceVerifier>(sp => sp.GetRequiredService<NominatimClient>());
        services.AddTransient<IReverseGeocoder>(sp => sp.GetRequiredService<NominatimClient>());
        services.AddHttpClient<IPlaceSearcher, PhotonPlaceSearcher>(HttpClientNames.Photon, client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);

        // Integraciones externas.
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(HttpClientNames.Turnstile, client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        // La URL de Telegram contiene el token del bot: sin registros automáticos de peticiones.
        services.AddHttpClient<ITelegramNotifier, TelegramNotifier>(HttpClientNames.Telegram, client => client.Timeout = TelegramNotifier.Timeout)
            .ConfigurePrimaryHttpMessageHandler(NoRedirects)
            .RemoveAllLoggers();
        services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(HttpClientNames.OpenMeteo, client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);

        // CORS con credenciales: solo orígenes explícitos, GET/POST y Content-Type.
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<DecibelCityOptions>((cors, options) =>
            cors.AddDefaultPolicy(policy => policy
                .WithOrigins([.. options.CorsOrigins])
                .AllowCredentials()
                .WithMethods("GET", "POST")
                .WithHeaders("Content-Type")));

        // Cabeceras X-Forwarded-For/Proto de proxies de confianza (Cloudflare, Nginx...).
        services.AddOptions<ForwardedHeadersOptions>().Configure<DecibelCityOptions>((forwarded, options) =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Sin límite de saltos: detrás de Cloudflare y del balanceador de la plataforma hay dos proxies.
            // El recorrido se detiene en el primer salto que no pertenece a una red de confianza, de modo que
            // un cliente no puede falsificar su IP añadiendo entradas a X-Forwarded-For.
            forwarded.ForwardLimit = null;
            foreach (var entry in options.TrustedProxies)
            {
                if (System.Net.IPNetwork.TryParse(entry, out var network))
                {
                    forwarded.KnownIPNetworks.Add(network);
                }
                else if (IPAddress.TryParse(entry, out var address))
                {
                    forwarded.KnownProxies.Add(address);
                }
                else
                {
                    throw new InvalidOperationException("DECIBELCITY_TRUSTED_PROXIES debe contener IP o redes CIDR separadas por comas.");
                }
            }
        });

        services.AddDecibelCityRateLimiting();
        return services;
    }

    /// <summary>Configura el pipeline en el orden requerido por la seguridad del contrato.</summary>
    /// <param name="app">Aplicación.</param>
    /// <returns>La misma aplicación.</returns>
    public static WebApplication UseDecibelCity(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        // Falla al arrancar si la configuración no es segura (p. ej., CORS "*").
        _ = app.Services.GetRequiredService<DecibelCityOptions>();

        app.UseForwardedHeaders();
        app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = ApiExceptionHandler.WriteUnhandledAsync });
        app.UseStatusCodePages(static async context =>
        {
            var detail = context.HttpContext.Response.StatusCode switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
                _ => null,
            };
            if (detail is not null)
            {
                await ApiExceptionHandler.WriteDetailAsync(context.HttpContext, detail, context.HttpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
        });
        app.UseMiddleware<AuthCacheControlMiddleware>();
        app.UseRouting();
        app.UseCors();
        app.UseRateLimiter();

        app.MapOpenApi();
        return app;
    }

    private static HttpMessageHandler NoRedirects() => new SocketsHttpHandler { AllowAutoRedirect = false };
}

/// <summary>Ejecuta la inicialización idempotente del esquema al arrancar (si está habilitada).</summary>
/// <param name="initializer">Inicializador.</param>
/// <param name="options">Configuración.</param>
public sealed class DatabaseInitializerHostedService(IDatabaseInitializer initializer, DecibelCityOptions options) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        options.InitializeDatabase ? initializer.InitializeAsync(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
