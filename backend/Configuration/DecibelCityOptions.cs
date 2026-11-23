using System.Globalization;

namespace DecibelCity.Api.Configuration;

/// <summary>
/// Configuración tipada de la API. Cada valor se lee de una variable de entorno
/// <c>DECIBELCITY_*</c> (o de su equivalente en <c>appsettings.json</c> / <c>.env</c>).
/// </summary>
public sealed record DecibelCityOptions
{
    /// <summary>User-Agent identificable exigido por las políticas de uso de OpenStreetMap.</summary>
    public const string DefaultUserAgent = "DecibelCity/0.1 (prototipo educativo de reportes de ruido)";

    /// <summary>Atribución obligatoria de la cartografía de OpenStreetMap.</summary>
    public const string DefaultAttribution =
        "&copy; <a href=\"https://www.openstreetmap.org/copyright\">colaboradores de OpenStreetMap</a>";

    /// <summary>URL (<c>postgresql://</c>) o cadena de conexión Npgsql de la base de la aplicación.</summary>
    public string DatabaseUrl { get; init; } = "postgresql://decibelcitydaw@127.0.0.1:5432/decibelcitydaw";

    /// <summary>Esquema PostgreSQL donde viven las once tablas de dominio.</summary>
    public string DatabaseSchema { get; init; } = "public";

    /// <summary>Crea o verifica el esquema v5 y siembra los catálogos al arrancar.</summary>
    public bool InitializeDatabase { get; init; } = true;

    /// <summary>Orígenes del navegador permitidos (CORS con credenciales y validación de <c>Origin</c>).</summary>
    public IReadOnlyList<string> CorsOrigins { get; init; } = ["http://localhost:4200", "http://127.0.0.1:4200"];

    /// <summary>Modo SameSite de la cookie de sesión (Lax por defecto, None para cross-site en HTTPS, Strict).</summary>
    public string CookieSameSite { get; init; } = "Lax";

    /// <summary>Proxies o redes (IP o CIDR) cuyas cabeceras <c>X-Forwarded-*</c> son confiables.</summary>
    public IReadOnlyList<string> TrustedProxies { get; init; } = [];

    /// <summary>Servidor Nominatim (verificación y geocodificación inversa).</summary>
    public string NominatimUrl { get; init; } = "https://nominatim.openstreetmap.org";

    /// <summary>Servidor Photon (sugerencias de búsqueda).</summary>
    public string PhotonUrl { get; init; } = "https://photon.komoot.io";

    /// <summary>User-Agent enviado a los servicios de OpenStreetMap.</summary>
    public string GeocoderUserAgent { get; init; } = DefaultUserAgent;

    /// <summary>Separación mínima entre peticiones a Nominatim (1 s en servidores públicos).</summary>
    public TimeSpan NominatimMinInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Plantilla de teselas del mapa.</summary>
    public string MapTileUrl { get; init; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>Atribución visible del mapa.</summary>
    public string MapAttribution { get; init; } = DefaultAttribution;

    /// <summary>Cloudflare Turnstile (solo en <c>POST /api/auth/login</c>).</summary>
    public TurnstileOptions Turnstile { get; init; } = new();

    /// <summary>Notificaciones salientes de Telegram al recibir reportes.</summary>
    public TelegramOptions Telegram { get; init; } = new();

    /// <summary>Clima de Open-Meteo.</summary>
    public WeatherOptions Weather { get; init; } = new();

    /// <summary>Limitación de peticiones por IP.</summary>
    public RateLimitOptions RateLimit { get; init; } = new();

    /// <summary>Construye y valida la configuración a partir de <see cref="IConfiguration"/>.</summary>
    /// <param name="configuration">Fuente de configuración (variables de entorno, appsettings, .env).</param>
    /// <returns>Opciones validadas.</returns>
    /// <exception cref="InvalidOperationException">Si algún valor es inválido.</exception>
    public static DecibelCityOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var defaults = new DecibelCityOptions();

        var options = new DecibelCityOptions
        {
            DatabaseUrl = Text(configuration, "DECIBELCITY_DATABASE_URL")
                          ?? Text(configuration, "DATABASE_URL")
                          ?? defaults.DatabaseUrl,
            DatabaseSchema = Text(configuration, "DECIBELCITY_DATABASE_SCHEMA") ?? defaults.DatabaseSchema,
            InitializeDatabase = Flag(configuration, "DECIBELCITY_DATABASE_INITIALIZE", defaults.InitializeDatabase),
            CorsOrigins = List(configuration, "DECIBELCITY_CORS_ORIGINS")?.Select(s => s.TrimEnd('/')).ToList() ?? defaults.CorsOrigins,
            CookieSameSite = Text(configuration, "DECIBELCITY_COOKIE_SAMESITE") ?? defaults.CookieSameSite,
            TrustedProxies = List(configuration, "DECIBELCITY_TRUSTED_PROXIES") ?? defaults.TrustedProxies,
            NominatimUrl = Url(configuration, "DECIBELCITY_NOMINATIM_URL") ?? defaults.NominatimUrl,
            PhotonUrl = Url(configuration, "DECIBELCITY_PHOTON_URL") ?? defaults.PhotonUrl,
            GeocoderUserAgent = Text(configuration, "DECIBELCITY_GEOCODER_USER_AGENT") ?? defaults.GeocoderUserAgent,
            NominatimMinInterval = TimeSpan.FromMilliseconds(
                Number(configuration, "DECIBELCITY_NOMINATIM_MIN_INTERVAL_MS", 1000, 0, 60_000)),
            MapTileUrl = Text(configuration, "DECIBELCITY_MAP_TILE_URL") ?? defaults.MapTileUrl,
            MapAttribution = Text(configuration, "DECIBELCITY_MAP_ATTRIBUTION") ?? defaults.MapAttribution,
            Turnstile = new TurnstileOptions
            {
                Enabled = Flag(configuration, "DECIBELCITY_TURNSTILE_ENABLED", true),
                SiteKey = Text(configuration, "DECIBELCITY_TURNSTILE_SITE_KEY") ?? string.Empty,
                SecretKey = Text(configuration, "DECIBELCITY_TURNSTILE_SECRET_KEY") ?? string.Empty,
                VerifyUrl = Text(configuration, "DECIBELCITY_TURNSTILE_VERIFY_URL") ?? TurnstileOptions.DefaultVerifyUrl,
            },
            Telegram = new TelegramOptions
            {
                BotToken = Text(configuration, "DECIBELCITY_TELEGRAM_BOT_TOKEN") ?? string.Empty,
                ChatId = Text(configuration, "DECIBELCITY_TELEGRAM_CHAT_ID") ?? string.Empty,
                ApiUrl = Url(configuration, "DECIBELCITY_TELEGRAM_API_URL") ?? TelegramOptions.DefaultApiUrl,
            },
            Weather = new WeatherOptions
            {
                OpenMeteoUrl = Url(configuration, "DECIBELCITY_OPEN_METEO_URL") ?? WeatherOptions.DefaultOpenMeteoUrl,
                CacheDuration = TimeSpan.FromMinutes(
                    Number(configuration, "DECIBELCITY_WEATHER_CACHE_MINUTES", 15, 1, 24 * 60)),
            },
            RateLimit = new RateLimitOptions
            {
                Enabled = Flag(configuration, "DECIBELCITY_RATE_LIMIT_ENABLED", true),
                LoginPerMinute = (int)Number(configuration, "DECIBELCITY_RATE_LIMIT_LOGIN_PER_MINUTE", 5, 1, 10_000),
                RegisterPerMinute = (int)Number(configuration, "DECIBELCITY_RATE_LIMIT_REGISTER_PER_MINUTE", 3, 1, 10_000),
                ReportsPerMinute = (int)Number(configuration, "DECIBELCITY_RATE_LIMIT_REPORTS_PER_MINUTE", 10, 1, 10_000),
            },
        };
        options.Validate();
        return options;
    }

    /// <summary>Comprueba las reglas que la aplicación no puede relajar.</summary>
    /// <exception cref="InvalidOperationException">Si la configuración no es segura o no es válida.</exception>
    public void Validate()
    {
        if (CorsOrigins.Any(origin => origin == "*"))
        {
            throw new InvalidOperationException("Configura orígenes CORS explícitos para las sesiones con cookies.");
        }

        if (!string.Equals(CookieSameSite, "Lax", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(CookieSameSite, "None", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(CookieSameSite, "Strict", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DECIBELCITY_COOKIE_SAMESITE debe ser Lax, None o Strict.");
        }

        if (!SchemaName.IsValid(DatabaseSchema))
        {
            throw new InvalidOperationException("El nombre del esquema PostgreSQL no es válido.");
        }

        foreach (var url in new[] { NominatimUrl, PhotonUrl, Telegram.ApiUrl, Weather.OpenMeteoUrl, Turnstile.VerifyUrl })
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("Las URL de servicios externos deben ser absolutas (http o https).");
            }
        }
    }

    private static string? Text(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? Url(IConfiguration configuration, string key) => Text(configuration, key)?.TrimEnd('/');

    private static List<string>? List(IConfiguration configuration, string key)
    {
        var value = Text(configuration, key);
        return value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool Flag(IConfiguration configuration, string key, bool fallback) =>
        Text(configuration, key)?.ToUpperInvariant() switch
        {
            null => fallback,
            "1" or "TRUE" or "YES" or "ON" => true,
            "0" or "FALSE" or "NO" or "OFF" => false,
            _ => throw new InvalidOperationException($"El valor de {key} debe ser true o false."),
        };

    private static double Number(IConfiguration configuration, string key, double fallback, double min, double max)
    {
        var value = Text(configuration, key);
        if (value is null)
        {
            return fallback;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number) || number < min || number > max)
        {
            throw new InvalidOperationException($"El valor de {key} debe ser un número entre {min} y {max}.");
        }

        return number;
    }
}

/// <summary>Configuración de Cloudflare Turnstile.</summary>
public sealed record TurnstileOptions
{
    /// <summary>Endpoint oficial de verificación de tokens.</summary>
    public const string DefaultVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>Interruptor manual; Turnstile solo se activa si además hay claves.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Clave pública del widget (se entrega al navegador).</summary>
    public string SiteKey { get; init; } = string.Empty;

    /// <summary>Clave secreta (solo servidor; nunca se devuelve ni se registra).</summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>URL de verificación (sustituible en pruebas).</summary>
    public string VerifyUrl { get; init; } = DefaultVerifyUrl;

    /// <summary>Turnstile se exige solo si está habilitado y ambas claves están configuradas.</summary>
    public bool IsActive => Enabled && SecretKey.Length > 0 && SiteKey.Length > 0;
}

/// <summary>Configuración del bot de Telegram.</summary>
public sealed record TelegramOptions
{
    /// <summary>API pública de bots de Telegram.</summary>
    public const string DefaultApiUrl = "https://api.telegram.org";

    /// <summary>Token del bot (BotFather). Secreto: nunca se registra.</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>Chat o canal privado que recibe las alertas.</summary>
    public string ChatId { get; init; } = string.Empty;

    /// <summary>URL base de la API de Telegram.</summary>
    public string ApiUrl { get; init; } = DefaultApiUrl;

    /// <summary>Las notificaciones se envían solo con token y chat configurados.</summary>
    public bool IsConfigured => BotToken.Length > 0 && ChatId.Length > 0;
}

/// <summary>Configuración del clima (Open-Meteo, sin clave).</summary>
public sealed record WeatherOptions
{
    /// <summary>API pública de Open-Meteo.</summary>
    public const string DefaultOpenMeteoUrl = "https://api.open-meteo.com";

    /// <summary>URL base de Open-Meteo.</summary>
    public string OpenMeteoUrl { get; init; } = DefaultOpenMeteoUrl;

    /// <summary>Duración de la caché por coordenada redondeada.</summary>
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(15);
}

/// <summary>Límites por IP en los endpoints sensibles.</summary>
public sealed record RateLimitOptions
{
    /// <summary>Permite desactivar la limitación (por ejemplo, en pruebas).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Intentos de inicio de sesión por minuto e IP.</summary>
    public int LoginPerMinute { get; init; } = 5;

    /// <summary>Registros por minuto e IP.</summary>
    public int RegisterPerMinute { get; init; } = 3;

    /// <summary>Reportes por minuto e IP.</summary>
    public int ReportsPerMinute { get; init; } = 10;
}
