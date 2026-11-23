using System.Net;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Repositories;
using DecibelCity.Api.Services.Osm;
using DecibelCity.Api.Services.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DecibelCity.Api.Tests.Infrastructure;

/// <summary>
/// Aplicación real (pipeline, filtros, JSON, cookies) con dobles HTTP para OSM, Turnstile, Telegram y
/// Open-Meteo. Por defecto usa repositorios en memoria; con <see cref="UseRealDatabase"/> usa PostgreSQL.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>IP simulada del cliente (TestServer no asigna ninguna).</summary>
    public static readonly IPAddress ClientIp = IPAddress.Parse("203.0.113.7");

    /// <summary>Configuración de la aplicación bajo prueba.</summary>
    public DecibelCityOptions Options { get; init; } = TestOptions.Create();

    /// <summary>Usar PostgreSQL (repositorios reales e inicialización del esquema).</summary>
    public bool UseRealDatabase { get; init; }

    /// <summary>Usar los parámetros Argon2 reales (lentos) en lugar de los de prueba.</summary>
    public bool UseProductionArgon2 { get; init; }

    /// <summary>Reloj sustituible (sesiones).</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>Cuentas en memoria.</summary>
    public InMemoryUserRepository Users { get; } = new();

    /// <summary>Reportes en memoria.</summary>
    public InMemoryNoiseRepository Noise { get; } = new();

    /// <summary>Nominatim simulado.</summary>
    public StubHttpHandler Nominatim { get; } = TestData.NominatimStub();

    /// <summary>Photon simulado.</summary>
    public StubHttpHandler Photon { get; } = TestData.PhotonStub();

    /// <summary>Cloudflare Turnstile simulado.</summary>
    public StubHttpHandler Turnstile { get; } = TestData.TurnstileStub();

    /// <summary>Telegram simulado.</summary>
    public StubHttpHandler Telegram { get; } = TestData.TelegramStub();

    /// <summary>Open-Meteo simulado.</summary>
    public StubHttpHandler OpenMeteo { get; } = TestData.OpenMeteoStub();

    /// <summary>Almacén de sesiones de la aplicación.</summary>
    public ISessionStore Sessions => Services.GetRequiredService<ISessionStore>();

    /// <summary>Cliente HTTP con cookies.</summary>
    /// <param name="https">Usar <c>https://localhost</c>.</param>
    /// <returns>Cliente.</returns>
    public HttpClient CreateBrowserClient(bool https = false) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Options);
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new RemoteIpStartupFilter(ClientIp));
            if (Time is not null)
            {
                services.AddSingleton(Time);
            }

            if (!UseProductionArgon2)
            {
                services.AddSingleton<IPasswordHasher>(new Argon2PasswordHasher(TestData.FastArgon2));
            }

            if (!UseRealDatabase)
            {
                services.AddSingleton<IUserRepository>(Users);
                services.AddSingleton<INoiseRepository>(Noise);
                services.AddSingleton<IDatabaseInitializer, NoopDatabaseInitializer>();
            }

            services.AddHttpClient(HttpClientNames.Nominatim).ConfigurePrimaryHttpMessageHandler(() => Nominatim);
            services.AddHttpClient(HttpClientNames.Photon).ConfigurePrimaryHttpMessageHandler(() => Photon);
            services.AddHttpClient(HttpClientNames.Turnstile).ConfigurePrimaryHttpMessageHandler(() => Turnstile);
            services.AddHttpClient(HttpClientNames.Telegram).ConfigurePrimaryHttpMessageHandler(() => Telegram);
            services.AddHttpClient(HttpClientNames.OpenMeteo).ConfigurePrimaryHttpMessageHandler(() => OpenMeteo);
        });
    }

    /// <summary>Asigna una IP remota fija antes del resto del pipeline.</summary>
    private sealed class RemoteIpStartupFilter(IPAddress address) : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>Opciones de prueba: servicios externos simulados, sin espera de Nominatim y sin limitación.</summary>
public static class TestOptions
{
    /// <summary>Origen del frontend permitido en pruebas.</summary>
    public const string Frontend = "http://localhost:4200";

    /// <summary>Crea opciones aisladas.</summary>
    /// <returns>Opciones.</returns>
    public static DecibelCityOptions Create() => new()
    {
        DatabaseUrl = "postgresql://sin-uso@127.0.0.1:1/sin_uso_test",
        InitializeDatabase = false,
        CorsOrigins = [Frontend],
        NominatimUrl = "https://nominatim.test",
        PhotonUrl = "https://photon.test",
        GeocoderUserAgent = TestData.UserAgent,
        NominatimMinInterval = TimeSpan.Zero,
        RateLimit = new RateLimitOptions { Enabled = false },
        Turnstile = new TurnstileOptions { VerifyUrl = "https://turnstile.test/siteverify" },
        Telegram = new TelegramOptions { ApiUrl = "https://telegram.test" },
        Weather = new WeatherOptions { OpenMeteoUrl = "https://open-meteo.test" },
    };
}
