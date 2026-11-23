using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Http;

/// <summary>Errores 422 sanitizados en /api/auth/* y validación estricta de Origin.</summary>
public sealed class ValidationAndOriginTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ValidationAndOriginTests() => _client = _factory.CreateBrowserClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    public static TheoryData<string, string> InvalidRegistrationChanges() => new()
    {
        { "nombres", "\"A\"" },
        { "apellidos", "\"     \"" },
        { "telefono", "\"12345\"" },
        { "telefono", "\"964-abc-123\"" },
        { "direccion", "\"Jr 1\"" },
        { "direccion", "\"" + new string('d', 201) + "\"" },
        { "nombre_usuario", "\"ab\"" },
        { "nombre_usuario", "\"ana quispe\"" },
        { "id_distrito", "\"1\"" },
        { "id_distrito", "true" },
        { "id_distrito", "0" },
        { "id_distrito", "1.0" },
        { "id_provincia", "\"1\"" },
        { "id_departamento", "true" },
        { "contrasena", "\"corta7!\"" },
        { "contrasena", "\"            \"" },
        { "contrasena", "\"" + new string('x', 129) + "\"" },
        { "rol", "\"administrador\"" },
        { "password_hash", "\"$argon2id$inyectado\"" },
    };

    [Theory]
    [MemberData(nameof(InvalidRegistrationChanges))]
    public async Task Register_InvalidInput_Returns422WithoutEchoingValues(string field, string rawValue)
    {
        var payload = TestData.Registration();
        payload[field] = JsonNode.Parse(rawValue);
        var response = await _client.PostJsonAsync("/api/auth/register", payload);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(TestData.Password, text, StringComparison.Ordinal);
        var value = JsonNode.Parse(rawValue)!.ToJsonString().Trim('"').Trim();
        if (value.Length > 3)
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }

        AssertSanitizedDetail(await response.JsonAsync(), ["nombres", "apellidos", "telefono", "direccion", "id_departamento",
            "id_provincia", "id_distrito", "nombre_usuario", "contrasena"]);
        Assert.Equal(0, _factory.Users.Count);
    }

    [Fact]
    public async Task Register_MissingSelector_ReportsMissingField()
    {
        var payload = TestData.Registration();
        payload.Remove("id_provincia");
        var detail = (await (await _client.PostJsonAsync("/api/auth/register", payload)).JsonAsync()).GetProperty("detail");

        var error = Assert.Single(detail.EnumerateArray());
        Assert.Equal("missing", error.GetProperty("type").GetString());
        Assert.Equal(["body", "id_provincia"], error.GetProperty("loc").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Este campo es obligatorio.", error.GetProperty("msg").GetString());
    }

    [Fact]
    public async Task Register_PasswordUsedAsExtraKey_IsNeverEchoed()
    {
        var payload = TestData.Registration();
        payload[TestData.Password] = "extra";
        var response = await _client.PostJsonAsync("/api/auth/register", payload);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(TestData.Password, text, StringComparison.Ordinal);
        var error = Assert.Single((await response.JsonAsync()).GetProperty("detail").EnumerateArray());
        Assert.Equal("extra_forbidden", error.GetProperty("type").GetString());
        Assert.Equal(["body"], error.GetProperty("loc").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("""{"nombre_usuario":"ana.quispe","contrasena":"Ruido seguro 2026!","extra":1}""")]
    [InlineData("""{"nombre_usuario":"ana.quispe"}""")]
    [InlineData("""{"nombre_usuario":"","contrasena":"Ruido seguro 2026!"}""")]
    [InlineData("""{"nombre_usuario":"ana.quispe","contrasena":"Ruido seguro 2026!","turnstile_token":5}""")]
    [InlineData("""{"nombre_usuario":"ana.quispe","contrasena": "Ruido seguro 2026!" """)]
    [InlineData("""["Ruido seguro 2026!"]""")]
    [InlineData("")]
    public async Task Login_InvalidBody_Returns422Sanitized(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(TestData.Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain("input", text, StringComparison.Ordinal);
        AssertSanitizedDetail(await response.JsonAsync(), ["nombre_usuario", "contrasena", "turnstile_token"]);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/reports")]
    public async Task Body_WithInvalidUtf8OrLoneSurrogate_Is422NotServerError(string path)
    {
        await _client.RegisterAsync("cuenta.utf8");
        // "Constitución" codificado en Latin-1 (0xF3) en lugar de UTF-8.
        var latin1 = Encoding.Latin1.GetBytes("""{"location":"Plaza Constitución","nombre_usuario":"x","contrasena":"y"}""");
        var surrogate = Encoding.UTF8.GetBytes("""{"location":"\ud800","nombre_usuario":"x","contrasena":"y"}""");

        foreach (var bytes in new[] { latin1, surrogate })
        {
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var response = await _client.PostAsync(path, content, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var error = Assert.Single((await response.JsonAsync()).GetProperty("detail").EnumerateArray());
            Assert.Equal("json_invalid", error.GetProperty("type").GetString());
        }

        Assert.Empty(_factory.Noise.Reports);
    }

    public static TheoryData<string, string?> InvalidReportChanges() => new()
    {
        { "location", "\"abcd\"" },
        { "location", "\"          \"" },
        { "location", "\"" + new string('x', 1001) + "\"" },
        { "description", "\"short\"" },
        { "description", "\"" + new string('x', 501) + "\"" },
        { "source", "\"unknown\"" },
        { "decibels", "29.9" },
        { "decibels", "120.1" },
        { "decibels", "true" },
        { "decibels", "\"74\"" },
        { "email", "\"person@example.test\"" },
        { "place_id", "\"\"" },
        { "place_id", "\"W\"" },
        { "place_id", "\"W" + new string('1', 20) + "\"" },
        { "place_id", "\"X123\"" },
        { "place_id", "\"w123\"" },
        { "place_id", "\"ChIJ_TestHuancayo123\"" },
        { "place_id", "\"../lookup?osm_ids=W1\"" },
        { "place_id", "813046785" },
        { "latitude", "-12.068" },
        { "place_id", null },
    };

    [Theory]
    [MemberData(nameof(InvalidReportChanges))]
    public async Task Report_InvalidInput_IsRejectedAndNotSaved(string field, string? rawValue)
    {
        await _client.RegisterAsync();
        var payload = TestData.Report();
        if (rawValue is null)
        {
            payload.Remove(field);
        }
        else
        {
            payload[field] = JsonNode.Parse(rawValue);
        }

        var response = await _client.PostJsonAsync("/api/reports", payload);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(_factory.Noise.Reports);
        Assert.Empty(_factory.Nominatim.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30.0)]
    [InlineData(120.0)]
    [InlineData(68.5)]
    public async Task Report_OptionalNoiseLevelBoundaries_AreAccepted(double? decibels)
    {
        await _client.RegisterAsync();
        var payload = TestData.Report();
        payload["decibels"] = decibels is null ? null : JsonValue.Create(decibels.Value);
        var response = await _client.PostJsonAsync("/api/reports", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.JsonAsync();
        if (decibels is null)
        {
            Assert.Equal(JsonValueKind.Null, json.GetProperty("decibels").ValueKind);
        }
        else
        {
            Assert.Equal(decibels.Value, json.GetProperty("decibels").GetDouble());
        }
    }

    [Theory]
    [InlineData("https://unknown.test", "/api/auth/register")]
    [InlineData("null", "/api/auth/login")]
    [InlineData("http://localhost:4200.evil.test", "/api/auth/logout")]
    [InlineData("https://unknown.test", "/api/reports")]
    [InlineData("null", "/api/reports")]
    [InlineData("http://localhost:4200.evil.test", "/api/auth/login")]
    [InlineData("https://localhost", "/api/auth/register")]
    public async Task ForeignOrigin_IsDeniedBeforeSideEffects(string origin, string path)
    {
        var profile = await _client.RegisterAsync();
        JsonNode? body = path switch
        {
            "/api/auth/register" => TestData.Registration("otra.cuenta"),
            "/api/auth/login" => TestData.Credentials(),
            "/api/reports" => TestData.Report(),
            _ => null,
        };

        var response = await _client.PostJsonAsync(path, body, origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("""{"detail":"El origen de la solicitud no está permitido."}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        // Un logout ajeno no revoca la cookie y nada se guarda.
        var session = await (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).JsonAsync();
        Assert.Equal(profile.GetRawText(), session.GetRawText());
        Assert.Equal(1, _factory.Users.Count);
        Assert.Empty(_factory.Noise.Reports);
        Assert.Empty(_factory.Nominatim.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost")]
    [InlineData(TestOptions.Frontend)]
    public async Task SameOriginFrontendAndApiClients_CanAuthenticateAndReport(string? origin)
    {
        var register = await _client.PostJsonAsync("/api/auth/register", TestData.Registration(), origin);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/auth/logout", null, origin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostJsonAsync("/api/auth/login", TestData.Credentials(), origin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostJsonAsync("/api/reports", TestData.Report(), origin)).StatusCode);
    }

    private static void AssertSanitizedDetail(JsonElement json, IReadOnlyCollection<string> knownFields)
    {
        var detail = json.GetProperty("detail");
        Assert.Equal(JsonValueKind.Array, detail.ValueKind);
        Assert.NotEmpty(detail.EnumerateArray());
        foreach (var error in detail.EnumerateArray())
        {
            Assert.Equal(["type", "loc", "msg"], error.EnumerateObject().Select(p => p.Name));
            var loc = error.GetProperty("loc").EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.Equal("body", loc[0]);
            Assert.True(loc.Count == 1 || (loc.Count == 2 && knownFields.Contains(loc[1])), $"loc inesperado: {string.Join('/', loc)}");
        }
    }
}
