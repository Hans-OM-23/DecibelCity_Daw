using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Http;

/// <summary>Turnstile en /login y limitación por IP.</summary>
public sealed class TurnstileAndRateLimitTests
{
    private static ApiFactory TurnstileFactory() => new()
    {
        Options = TestOptions.Create() with
        {
            Turnstile = new TurnstileOptions
            {
                SiteKey = "0x4AAAA-sitio",
                SecretKey = "0x4AAAA-secreto",
                VerifyUrl = "https://turnstile.test/siteverify",
            },
        },
    };

    [Fact]
    public async Task AuthConfig_ExposesOnlySiteKeyWhenEnabled()
    {
        using var factory = TurnstileFactory();
        using var client = factory.CreateBrowserClient();
        var text = await (await client.GetAsync("/api/auth/config", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("""{"turnstile_enabled":true,"turnstile_site_key":"0x4AAAA-sitio"}""", text);
        Assert.DoesNotContain("secreto", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithValidToken_VerifiesWithSiteverifyAndSucceeds()
    {
        using var factory = TurnstileFactory();
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync(); // El registro no exige Turnstile.

        var credentials = TestData.Credentials();
        credentials["turnstile_token"] = TestData.ValidTurnstileToken;
        var response = await client.PostJsonAsync("/api/auth/login", credentials);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(factory.Turnstile.Requests);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://turnstile.test/siteverify", call.Uri.ToString());
        var form = call.Body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        Assert.Equal("0x4AAAA-secreto", form["secret"]);
        Assert.Equal(TestData.ValidTurnstileToken, form["response"]);
        Assert.Equal(ApiFactory.ClientIp.ToString(), form["remoteip"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("token-invalido")]
    public async Task Login_WithMissingOrInvalidToken_Is422BeforeCheckingPassword(string? token)
    {
        using var factory = TurnstileFactory();
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();
        var credentials = TestData.Credentials();
        if (token is not null)
        {
            credentials["turnstile_token"] = token;
        }

        var response = await client.PostJsonAsync("/api/auth/login", credentials);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("""{"detail":"La verificación de seguridad ha fallado o expiró. Inténtalo nuevamente."}""",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(string.IsNullOrEmpty(token) ? 0 : 1, factory.Turnstile.Requests.Count);
    }

    [Fact]
    public async Task Login_WhenSiteverifyIsDown_Is422()
    {
        using var factory = TurnstileFactory();
        factory.Turnstile.Throw(new HttpRequestException("caído"));
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();
        var credentials = TestData.Credentials();
        credentials["turnstile_token"] = TestData.ValidTurnstileToken;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostJsonAsync("/api/auth/login", credentials)).StatusCode);
    }

    [Fact]
    public async Task Login_WhenDisabled_IgnoresTokenAndNeverCallsCloudflare()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();
        var credentials = TestData.Credentials();
        credentials["turnstile_token"] = "cualquiera";

        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/login", credentials)).StatusCode);
        Assert.Empty(factory.Turnstile.Requests);
    }

    [Fact]
    public async Task Login_IsRateLimitedPerIp()
    {
        using var factory = new ApiFactory { Options = TestOptions.Create() with { RateLimit = new RateLimitOptions { LoginPerMinute = 5 } } };
        using var client = factory.CreateBrowserClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostJsonAsync("/api/auth/login", TestData.Credentials("nadie"))).StatusCode);
        }

        var limited = await client.PostJsonAsync("/api/auth/login", TestData.Credentials("nadie"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Demasiadas solicitudes. Espera un momento e inténtalo nuevamente.", (await limited.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal("no-store", limited.Headers.CacheControl!.ToString());
        // Las rutas públicas no se limitan.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", TestContext.Current.CancellationToken)).StatusCode);
    }
}

/// <summary>Búsqueda Photon y geocodificación inversa con Nominatim.</summary>
public sealed class PlacesTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public PlacesTests() => _client = _factory.CreateBrowserClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Search_ReturnsOnlySpecificPlacesInsideHuancayo()
    {
        var response = await _client.GetAsync("/api/places/search?q=%20%20Av.%20%20Giraldez%20", TestContext.Current.CancellationToken);
        var json = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var place = Assert.Single(json.EnumerateArray());
        Assert.Equal("""{"place_id":"W813046785","name":"Avenida Giraldez","address":"San Carlos, Huancayo","lat":-12.0664834,"lng":-75.2062612}""", place.GetRawText());
        var request = Assert.Single(_factory.Photon.Requests);
        Assert.Contains("q=Av.%20Giraldez", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("bbox=-75.3,-12.16,-75.12,-11.95", request.Uri.Query, StringComparison.Ordinal);
        Assert.Equal(TestData.UserAgent, request.UserAgent);
    }

    [Theory]
    [InlineData("/api/places/search")]
    [InlineData("/api/places/search?q=ab")]
    [InlineData("/api/places/search?q=a%20%20")]
    public async Task Search_ShortQuery_Is422WithoutCallingPhoton(string url)
    {
        var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(_factory.Photon.Requests);
    }

    [Fact]
    public async Task Search_ProviderUnavailable_Is503()
    {
        _factory.Photon.RespondStatus(HttpStatusCode.TooManyRequests);
        var response = await _client.GetAsync("/api/places/search?q=Giraldez", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("La búsqueda de lugares no está disponible. Intenta más tarde.", (await response.JsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reverse_ReturnsVerifiedPlaceSuggestion()
    {
        var response = await _client.GetAsync("/api/places/reverse?lat=-12.0664834&lng=-75.2062612", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal(["place_id", "name", "address", "lat", "lng"], json.EnumerateObject().Select(p => p.Name));
        Assert.Equal("W813046785", json.GetProperty("place_id").GetString());
        Assert.Equal("Avenida Giraldez", json.GetProperty("name").GetString());
        Assert.Equal("San Carlos, Huancayo", json.GetProperty("address").GetString());
        var request = Assert.Single(_factory.Nominatim.Requests);
        Assert.Equal("/reverse", request.Uri.AbsolutePath);
        Assert.Contains("lat=-12.0664834", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("lon=-75.2062612", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("format=jsonv2", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("addressdetails=1", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("accept-language=es", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("zoom=18", request.Uri.Query, StringComparison.Ordinal);
        Assert.Equal(TestData.UserAgent, request.UserAgent);
    }

    [Theory]
    [InlineData("lat=-12.5&lng=-75.2")]
    [InlineData("lat=-12.06&lng=-77.03")]
    [InlineData("lat=NaN&lng=-75.2")]
    [InlineData("lat=Infinity&lng=-75.2")]
    [InlineData("lat=abc&lng=-75.2")]
    [InlineData("lat=-12.06")]
    [InlineData("")]
    public async Task Reverse_InvalidOrOutsideCoordinates_Is422WithoutCallingNominatim(string query)
    {
        var response = await _client.GetAsync("/api/places/reverse?" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(_factory.Nominatim.Requests);
    }

    public static TheoryData<string> GenericPlaces() => new()
    {
        """{"place_rank":16}""",
        """{"category":"place","type":"suburb","place_rank":26}""",
        """{"category":"boundary","place_rank":26}""",
        """{"address":{"country_code":"bo"}}""",
        """{"lat":"-12.5"}""",
        """{"osm_type":null}""",
    };

    [Theory]
    [MemberData(nameof(GenericPlaces))]
    public async Task Reverse_GenericOrForeignResult_Is422(string overrides)
    {
        var place = TestData.OsmPlace();
        foreach (var (key, value) in JsonNode.Parse(overrides)!.AsObject())
        {
            place[key] = value?.DeepClone();
        }

        _factory.Nominatim.RespondJson(place);
        var response = await _client.GetAsync("/api/places/reverse?lat=-12.0664834&lng=-75.2062612", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(JsonValueKind.String, (await response.JsonAsync()).GetProperty("detail").ValueKind);
    }

    [Fact]
    public async Task Reverse_UnableToGeocode_Is422()
    {
        _factory.Nominatim.RespondJson(new JsonObject { ["error"] = "Unable to geocode" });
        var response = await _client.GetAsync("/api/places/reverse?lat=-12.0664834&lng=-75.2062612", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.BadGateway)]
    public async Task Reverse_ProviderErrors_Map502Or503(HttpStatusCode provider, HttpStatusCode expected)
    {
        _factory.Nominatim.RespondStatus(provider);
        var response = await _client.GetAsync("/api/places/reverse?lat=-12.0664834&lng=-75.2062612", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Reverse_NetworkFailure_Is502()
    {
        _factory.Nominatim.Throw(new HttpRequestException("sin red"));
        var response = await _client.GetAsync("/api/places/reverse?lat=-12.0664834&lng=-75.2062612", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("No se pudo verificar la ubicación. Intenta nuevamente.", (await response.JsonAsync()).GetProperty("detail").GetString());
    }
}

/// <summary>Reportes: verificación Nominatim, persistencia anónima y tolerancia a fallos de Telegram.</summary>
public sealed class ReportTelegramTests
{
    private static ApiFactory TelegramFactory() => new()
    {
        Options = TestOptions.Create() with
        {
            Telegram = new TelegramOptions { BotToken = "123456:token-secreto", ChatId = "-1001234", ApiUrl = "https://telegram.test" },
        },
    };

    [Fact]
    public async Task Report_IsVerifiedSavedAndNotifiedWithoutPersonalData()
    {
        using var factory = TelegramFactory();
        using var client = factory.CreateBrowserClient();
        var profile = await client.RegisterAsync();

        var response = await client.PostJsonAsync("/api/reports", TestData.Report());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var receipt = await response.JsonAsync();
        Assert.Equal(["location", "place_id", "source", "description", "decibels", "id", "latitude", "longitude", "created_at", "status"],
            receipt.EnumerateObject().Select(p => p.Name));
        Assert.Equal("received", receipt.GetProperty("status").GetString());
        Assert.Equal("Avenida Giraldez, San Carlos, Huancayo, Junín, 12001, Perú", receipt.GetProperty("location").GetString());
        Assert.Equal(-12.0664834, receipt.GetProperty("latitude").GetDouble());
        Assert.EndsWith("Z", receipt.GetProperty("created_at").GetString(), StringComparison.Ordinal);
        Assert.True(Guid.TryParse(receipt.GetProperty("id").GetString(), out _));

        var lookup = Assert.Single(factory.Nominatim.Requests);
        Assert.Equal("/lookup", lookup.Uri.AbsolutePath);
        Assert.Contains("osm_ids=W813046785", lookup.Uri.Query, StringComparison.Ordinal);
        Assert.Single(factory.Noise.Reports);

        var telegram = Assert.Single(factory.Telegram.Requests);
        Assert.Equal("https://telegram.test/bot123456:token-secreto/sendMessage", telegram.Uri.ToString());
        var message = JsonDocument.Parse(telegram.Body).RootElement;
        Assert.Equal("-1001234", message.GetProperty("chat_id").GetString());
        var text = message.GetProperty("text").GetString()!;
        Assert.Contains("Avenida Giraldez, San Carlos", text, StringComparison.Ordinal);
        Assert.Contains("Tráfico vehicular y transporte público", text, StringComparison.Ordinal);
        Assert.Contains("74 dB", text, StringComparison.Ordinal);
        Assert.Contains("Bocinas constantes", text, StringComparison.Ordinal);
        Assert.Contains(receipt.GetProperty("id").GetString()!, text, StringComparison.Ordinal);
        foreach (var personal in new[] { "nombre_usuario", "nombres", "apellidos", "telefono", "direccion" })
        {
            Assert.DoesNotContain(profile.GetProperty(personal).GetString()!, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Report_WithoutDecibels_NotifiesNotSpecified()
    {
        using var factory = TelegramFactory();
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();
        var payload = TestData.Report();
        payload.Remove("decibels");

        var response = await client.PostJsonAsync("/api/reports", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await response.JsonAsync()).GetProperty("decibels").ValueKind);
        Assert.Contains("Nivel estimado: No especificado", JsonDocument.Parse(factory.Telegram.Requests.Single().Body).RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    public static TheoryData<string> TelegramFailures() => ["network", "status500", "unauthorized", "timeout"];

    [Theory]
    [MemberData(nameof(TelegramFailures))]
    public async Task Report_TelegramFailure_NeverBreaks201(string failure)
    {
        using var factory = TelegramFactory();
        switch (failure)
        {
            case "network":
                factory.Telegram.Throw(new HttpRequestException("sin red"));
                break;
            case "status500":
                factory.Telegram.RespondStatus(HttpStatusCode.InternalServerError);
                break;
            case "unauthorized":
                factory.Telegram.RespondStatus(HttpStatusCode.Unauthorized);
                break;
            default:
                factory.Telegram.Responder = async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                };
                break;
        }

        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();
        var started = System.Diagnostics.Stopwatch.StartNew();

        var response = await client.PostJsonAsync("/api/reports", TestData.Report());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(factory.Noise.Reports);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), "La notificación debe cortarse a los 2 s.");
    }

    [Fact]
    public async Task Report_WithoutTelegramConfiguration_IsNoOp()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostJsonAsync("/api/reports", TestData.Report())).StatusCode);
        Assert.Empty(factory.Telegram.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.BadGateway)]
    public async Task Report_NominatimFailure_IsNotSavedNorNotified(HttpStatusCode provider, HttpStatusCode expected)
    {
        using var factory = TelegramFactory();
        factory.Nominatim.RespondStatus(provider);
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();

        Assert.Equal(expected, (await client.PostJsonAsync("/api/reports", TestData.Report())).StatusCode);
        Assert.Empty(factory.Noise.Reports);
        Assert.Empty(factory.Telegram.Requests);
    }

    [Fact]
    public async Task Report_PlaceOutsideHuancayo_Is422()
    {
        using var factory = new ApiFactory();
        var place = TestData.OsmPlace();
        place["lat"] = "-12.0464";
        place["lon"] = "-77.0428";
        factory.Nominatim.RespondJson(new JsonArray(place));
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();

        var response = await client.PostJsonAsync("/api/reports", TestData.Report());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("El lugar debe estar dentro del área de cobertura de Huancayo, Perú.", (await response.JsonAsync()).GetProperty("detail").GetString());
        Assert.Empty(factory.Noise.Reports);
    }
}

/// <summary>Clima de Open-Meteo con caché por coordenada redondeada.</summary>
public sealed class WeatherTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public WeatherTests() => _client = _factory.CreateBrowserClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Weather_DefaultsToHuancayoAndUsesCacheOnSecondCall()
    {
        var first = await _client.GetAsync("/api/weather", TestContext.Current.CancellationToken);
        var second = await _client.GetAsync("/api/weather?lat=-12.0671&lng=-75.2098", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            """{"temperature_c":16.4,"apparent_temperature_c":15.8,"humidity_percent":68,"precipitation_mm":0,"wind_speed_kmh":12.5,"weather_code":2,"condition":"Parcialmente nublado","icon":"partly_cloudy","updated_at":"2026-11-20T07:45:00Z"}""",
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var request = Assert.Single(_factory.OpenMeteo.Requests);
        Assert.Equal("/v1/forecast", request.Uri.AbsolutePath);
        Assert.Contains("latitude=-12.07", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("longitude=-75.21", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m", request.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weather_DifferentRoundedCoordinate_FetchesAgain()
    {
        await _client.GetAsync("/api/weather", TestContext.Current.CancellationToken);
        await _client.GetAsync("/api/weather?lat=-12.00&lng=-75.20", TestContext.Current.CancellationToken);
        Assert.Equal(2, _factory.OpenMeteo.Requests.Count);
    }

    [Theory]
    [InlineData("lat=-13&lng=-75.2")]
    [InlineData("lat=-12.06")]
    [InlineData("lng=-75.2")]
    [InlineData("lat=x&lng=-75.2")]
    public async Task Weather_InvalidCoordinates_Is422(string query)
    {
        var response = await _client.GetAsync("/api/weather?" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(_factory.OpenMeteo.Requests);
    }

    [Fact]
    public async Task Weather_ProviderFailures_AreNotCached()
    {
        _factory.OpenMeteo.RespondStatus(HttpStatusCode.InternalServerError);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync("/api/weather", TestContext.Current.CancellationToken)).StatusCode);

        _factory.OpenMeteo.Throw(new HttpRequestException("sin red"));
        var network = await _client.GetAsync("/api/weather", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, network.StatusCode);
        Assert.Equal(JsonValueKind.String, (await network.JsonAsync()).GetProperty("detail").ValueKind);

        _factory.OpenMeteo.RespondJson(new JsonObject { ["current"] = new JsonObject { ["time"] = "2026-11-20T07:45" } });
        Assert.Equal(HttpStatusCode.BadGateway, (await _client.GetAsync("/api/weather", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(3, _factory.OpenMeteo.Requests.Count);
    }

    [Theory]
    [InlineData(0, "Despejado", "clear")]
    [InlineData(3, "Nublado", "cloudy")]
    [InlineData(45, "Niebla", "fog")]
    [InlineData(63, "Lluvia moderada", "rain")]
    [InlineData(95, "Tormenta eléctrica", "thunderstorm")]
    [InlineData(42, "Condición no disponible", "unknown")]
    public void WmoCodes_AreTranslatedToSpanish(int code, string condition, string icon)
    {
        Assert.Equal((condition, icon), DecibelCity.Api.Services.Integrations.WmoWeatherCodes.Describe(code));
    }
}
