using System.Net;
using System.Text.Json;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Http;

/// <summary>Paridad del contrato HTTP público con el backend FastAPI.</summary>
public sealed class ContractTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ContractTests() => _client = _factory.CreateBrowserClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Health_ReturnsExactPayload()
    {
        var response = await _client.GetAsync("/api/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok","service":"DecibelCity API"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MapsConfig_UsesExactCenterAndBoundsFromPythonSchema()
    {
        var json = await (await _client.GetAsync("/api/maps/config", TestContext.Current.CancellationToken)).JsonAsync();

        Assert.Equal(["tile_url", "attribution", "center", "bounds"], json.EnumerateObject().Select(p => p.Name));
        Assert.Equal("https://tile.openstreetmap.org/{z}/{x}/{y}.png", json.GetProperty("tile_url").GetString());
        Assert.Equal(DecibelCityOptions.DefaultAttribution, json.GetProperty("attribution").GetString());
        Assert.Contains("colaboradores de OpenStreetMap", json.GetProperty("attribution").GetString(), StringComparison.Ordinal);
        Assert.Equal(-12.067, json.GetProperty("center").GetProperty("lat").GetDouble());
        Assert.Equal(-75.210, json.GetProperty("center").GetProperty("lng").GetDouble());
        var bounds = json.GetProperty("bounds");
        Assert.Equal(["south", "west", "north", "east"], bounds.EnumerateObject().Select(p => p.Name));
        Assert.Equal(-12.16, bounds.GetProperty("south").GetDouble());
        Assert.Equal(-75.30, bounds.GetProperty("west").GetDouble());
        Assert.Equal(-11.95, bounds.GetProperty("north").GetDouble());
        Assert.Equal(-75.12, bounds.GetProperty("east").GetDouble());
    }

    [Fact]
    public async Task Hotspots_AreIllustrativeAndInsideHuancayo()
    {
        var json = await (await _client.GetAsync("/api/hotspots", TestContext.Current.CancellationToken)).JsonAsync();
        var hotspots = json.EnumerateArray().ToList();

        Assert.Equal([74d, 78d, 69d], hotspots.Select(h => h.GetProperty("decibels").GetDouble()));
        Assert.All(hotspots, h => Assert.True(h.GetProperty("illustrative").GetBoolean()));
        Assert.All(hotspots, h => Assert.InRange(h.GetProperty("latitude").GetDouble(), -12.16, -11.95));
        Assert.All(hotspots, h => Assert.InRange(h.GetProperty("longitude").GetDouble(), -75.30, -75.12));
        Assert.Equal(
            ["id", "name", "area", "district", "sector", "decibels", "source", "source_label", "description", "time_label",
             "x", "y", "reference_limit", "illustrative", "latitude", "longitude"],
            hotspots[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task NoiseSources_ListFiveCodes()
    {
        var json = await (await _client.GetAsync("/api/noise-sources", TestContext.Current.CancellationToken)).JsonAsync();
        Assert.Equal(["traffic", "commerce", "construction", "entertainment", "other"],
            json.EnumerateArray().Select(s => s.GetProperty("value").GetString()));
        Assert.All(json.EnumerateArray(), s => Assert.Equal(["value", "label"], s.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public async Task Territories_ReturnOnlyActiveHierarchy()
    {
        _factory.Users.Distritos[1] = (_factory.Users.Distritos[1].Item, false);
        var json = await (await _client.GetAsync("/api/territories", TestContext.Current.CancellationToken)).JsonAsync();

        Assert.Equal(["departamentos", "provincias", "distritos"], json.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(json.GetProperty("distritos").EnumerateArray(), d => d.GetProperty("ubigeo").GetString() == "120107");
        Assert.Equal(["id_distrito", "id_provincia", "ubigeo", "nombre_distrito"],
            json.GetProperty("distritos")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task UnknownRoutesAndMethods_ReturnJsonDetail()
    {
        var notFound = await _client.GetAsync("/api/no-existe", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("Not Found", (await notFound.JsonAsync()).GetProperty("detail").GetString());

        // No existe un endpoint público que liste el texto privado de los reportes.
        var listReports = await _client.GetAsync("/api/reports", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, listReports.StatusCode);
        Assert.Equal("Method Not Allowed", (await listReports.JsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Cors_AllowsConfiguredFrontendWithCredentialsOnly()
    {
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/reports");
        allowed.Headers.Add("Origin", TestOptions.Frontend);
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        allowed.Headers.Add("Access-Control-Request-Headers", "content-type");
        var response = await _client.SendAsync(allowed, TestContext.Current.CancellationToken);
        Assert.Equal(TestOptions.Frontend, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var foreign = new HttpRequestMessage(HttpMethod.Options, "/api/reports");
        foreign.Headers.Add("Origin", "https://unknown.test");
        foreign.Headers.Add("Access-Control-Request-Method", "POST");
        var denied = await _client.SendAsync(foreign, TestContext.Current.CancellationToken);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Options_RejectWildcardCors()
    {
        var options = new DecibelCityOptions { CorsOrigins = ["*"] };
        var error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("orígenes CORS explícitos", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_DocumentsEndpointsWithoutPasswordHash()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.JsonAsync();
        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "/api/health", "/api/hotspots", "/api/maps/config", "/api/places/search", "/api/places/reverse", "/api/noise-sources",
            "/api/reports", "/api/territories", "/api/auth/register", "/api/auth/login", "/api/auth/session", "/api/auth/logout",
            "/api/auth/config", "/api/weather",
        }, paths);
        var profile = document.GetProperty("components").GetProperty("schemas").GetProperty("UserProfile").GetProperty("properties");
        Assert.False(profile.TryGetProperty("password_hash", out _));
        Assert.False(profile.TryGetProperty("contrasena", out _));
        Assert.Equal(JsonValueKind.Object, document.GetProperty("paths").GetProperty("/api/auth/login").GetProperty("post").GetProperty("responses").ValueKind);
    }
}
