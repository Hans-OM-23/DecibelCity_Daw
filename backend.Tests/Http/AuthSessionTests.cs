using System.Net;
using System.Text.Json;
using DecibelCity.Api.Services.Security;
using DecibelCity.Api.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace DecibelCity.Api.Tests.Http;

/// <summary>Registro, inicio de sesión, cookie opaca, sesión, cierre y estados de cuenta/rol.</summary>
public sealed class AuthSessionTests : IDisposable
{
    private static readonly string[] ProfileKeys =
    [
        "id_usuario", "nombre_usuario", "nombres", "apellidos", "telefono", "direccion", "id_distrito", "nombre_distrito",
        "id_provincia", "nombre_provincia", "id_departamento", "nombre_departamento", "rol", "fecha_registro",
    ];

    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public AuthSessionTests() => _client = _factory.CreateBrowserClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task SessionLifecycle_RegisterSessionLogoutLoginSessionLogout()
    {
        var register = await _client.PostJsonAsync("/api/auth/register", TestData.Registration());
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var profile = await register.JsonAsync();
        Assert.Equal(ProfileKeys, profile.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Ana María", profile.GetProperty("nombres").GetString());
        Assert.Equal("ciudadano", profile.GetProperty("rol").GetString());
        Assert.EndsWith("Z", profile.GetProperty("fecha_registro").GetString(), StringComparison.Ordinal);

        var session = await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal(profile.GetRawText(), (await session.JsonAsync()).GetRawText());
        Assert.Equal("no-store", session.Headers.CacheControl!.ToString());

        var logout = await _client.PostJsonAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Empty(await logout.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var cleared = SetCookie(logout);
        Assert.Contains("max-age=0", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", logout.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);

        var login = await _client.PostJsonAsync("/api/auth/login", TestData.Credentials(" ANA.QUISPE "));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(profile.GetRawText(), (await login.JsonAsync()).GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_SetsCookieAttributesWithoutExposingTokenOrHash(bool https)
    {
        using var client = _factory.CreateBrowserClient(https);
        await client.RegisterAsync();

        var response = await client.PostJsonAsync("/api/auth/login", TestData.Credentials());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = SetCookie(response);
        var token = cookie.Split(';')[0].Split('=', 2)[1];
        Assert.StartsWith(SessionCookie.Name + "=", cookie, StringComparison.Ordinal);
        Assert.Equal(43, token.Length);
        Assert.Contains("max-age=28800", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(https, cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("password_hash", body, StringComparison.Ordinal);
        Assert.DoesNotContain("$argon2", body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Login_WithSameSiteNone_SetsSameSiteNoneAndSecure()
    {
        using var factory = new ApiFactory { Options = TestOptions.Create() with { CookieSameSite = "None" } };
        using var client = factory.CreateBrowserClient(https: false);
        await client.RegisterAsync();

        var response = await client.PostJsonAsync("/api/auth/login", TestData.Credentials());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = SetCookie(response);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_RotatesCookieAndRejectsReplayOfPreviousSession()
    {
        var registerResponse = await _client.PostJsonAsync("/api/auth/register", TestData.Registration());
        var oldToken = Token(registerResponse);

        var login = await _client.PostJsonAsync("/api/auth/login", TestData.Credentials());
        var newToken = Token(login);

        Assert.NotEqual(oldToken, newToken);
        Assert.Null(_factory.Sessions.Resolve(oldToken));
        Assert.NotNull(_factory.Sessions.Resolve(newToken));

        using var replay = _factory.CreateBrowserClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={oldToken}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task FailedLogin_DoesNotIssueOrRotateCookie()
    {
        var profile = await _client.RegisterAsync();
        var response = await _client.PostJsonAsync("/api/auth/login", TestData.Credentials(password: "incorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var session = await (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).JsonAsync();
        Assert.Equal(profile.GetRawText(), session.GetRawText());
    }

    [Theory]
    [InlineData("ana.quispe", TestData.Password + "x")]
    [InlineData("ana.quispe", "ruido seguro 2026!")]
    [InlineData("no.existe", TestData.Password)]
    public async Task Login_BadCredentials_ReturnSameMessage(string username, string password)
    {
        await _client.RegisterAsync();
        var response = await _client.PostJsonAsync("/api/auth/login", TestData.Credentials(username, password));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"detail":"Nombre de usuario o contraseña incorrectos."}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Login_InactiveAccount_RevealsStateOnlyAfterValidPassword()
    {
        await _client.RegisterAsync();
        _factory.Users.SetAllUsersActive(false);

        var valid = await _client.PostJsonAsync("/api/auth/login", TestData.Credentials());
        Assert.Equal(HttpStatusCode.Forbidden, valid.StatusCode);
        Assert.Equal("La cuenta está desactivada. Comunícate con el equipo de DecibelCity.", (await valid.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/auth/login", TestData.Credentials(password: "otra"))).StatusCode);
    }

    [Fact]
    public async Task ExistingSession_WithDisabledRole_IsForbidden()
    {
        await _client.RegisterAsync();
        _factory.Users.RoleActive = false;

        var session = await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
        Assert.Equal("La cuenta o su rol están desactivados.", (await session.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostJsonAsync("/api/reports", TestData.Report())).StatusCode);
        Assert.Empty(_factory.Noise.Reports);
        Assert.Empty(_factory.Nominatim.Requests);
    }

    [Fact]
    public async Task ExistingSession_OfDeletedAccount_IsUnauthorized()
    {
        await _client.RegisterAsync();
        _factory.Users.DeleteAll();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/reports", TestData.Report())).StatusCode);
    }

    [Fact]
    public async Task Session_ExpiresAfterEightHours()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var factory = new ApiFactory { Time = time };
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);

        time.Advance(TimeSpan.FromHours(8));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostJsonAsync("/api/reports", TestData.Report())).StatusCode);
        Assert.Empty(factory.Nominatim.Requests);
    }

    [Fact]
    public async Task Sessions_AreLocalToTheProcess()
    {
        await _client.RegisterAsync();
        var token = _factory.Sessions.Create(1);
        using var other = new ApiFactory();
        Assert.Null(other.Sessions.Resolve(token));
    }

    [Theory]
    [InlineData("ana.quispe")]
    [InlineData("Ana.Quispe")]
    public async Task Register_DuplicateUsername_IsCaseInsensitiveConflict(string username)
    {
        await _client.RegisterAsync();
        var response = await _client.PostJsonAsync("/api/auth/register", TestData.Registration(username));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("""{"detail":"El nombre de usuario ya está registrado."}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(5, 9, 1)] // Distrito de otra provincia.
    [InlineData(1, 1, 99)] // Departamento inexistente.
    [InlineData(999, 1, 1)] // Distrito inexistente.
    public async Task Register_InvalidTerritorialSelection_Is422(int idDistrito, int idProvincia, int idDepartamento)
    {
        var payload = TestData.Registration();
        payload["id_distrito"] = idDistrito;
        payload["id_provincia"] = idProvincia;
        payload["id_departamento"] = idDepartamento;
        var response = await _client.PostJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("El distrito seleccionado no existe o no está disponible para el registro.",
            (await response.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(0, _factory.Users.Count);
    }

    [Fact]
    public async Task Register_InactiveProvince_Is422()
    {
        _factory.Users.Provincias[0] = (_factory.Users.Provincias[0].Item, false);
        var response = await _client.PostJsonAsync("/api/auth/register", TestData.Registration());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, _factory.Users.Count);
    }

    [Fact]
    public async Task Register_WithoutActiveDefaultRole_Is503()
    {
        _factory.Users.RoleActive = false;
        var response = await _client.PostJsonAsync("/api/auth/register", TestData.Registration());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"detail":"El registro no está disponible en este momento."}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Register_KeepsPasswordSpacesAndStoresArgon2idHash()
    {
        var payload = TestData.Registration();
        payload["contrasena"] = $"  {TestData.Password}  ";
        Assert.Equal(HttpStatusCode.Created, (await _client.PostJsonAsync("/api/auth/register", payload)).StatusCode);

        Assert.StartsWith("$argon2id$v=19$", _factory.Users.HashOf("ana.quispe"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/auth/login", TestData.Credentials())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostJsonAsync("/api/auth/login", TestData.Credentials(password: $"  {TestData.Password}  "))).StatusCode);
    }

    [Fact]
    public async Task Login_WithArgonCffiHash_AuthenticatesAndRehashesWeakParameters()
    {
        using var factory = new ApiFactory { UseProductionArgon2 = true };
        using var client = factory.CreateBrowserClient();
        factory.Users.Seed("python.default", TestData.ArgonCffiDefaultHash);
        factory.Users.Seed("python.debil", TestData.ArgonCffiWeakHash);

        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/login", TestData.Credentials("python.default"))).StatusCode);
        Assert.Equal(TestData.ArgonCffiDefaultHash, factory.Users.HashOf("python.default"));

        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/login", TestData.Credentials("python.debil"))).StatusCode);
        var rehashed = factory.Users.HashOf("python.debil");
        Assert.NotEqual(TestData.ArgonCffiWeakHash, rehashed);
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", rehashed, StringComparison.Ordinal);
        Assert.True(new Argon2PasswordHasher().Verify(rehashed, TestData.Password));
    }

    [Fact]
    public async Task Login_WithCorruptStoredHash_FailsClosed()
    {
        _factory.Users.Seed("corrupta", "no-es-un-hash");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/auth/login", TestData.Credentials("corrupta"))).StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutSession_IsIdempotent()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("inventada")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task ForgedCookieOrHeaders_DoNotAuthorize(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reports")
        {
            Content = System.Net.Http.Json.JsonContent.Create(TestData.Report()),
        };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        request.Headers.Add("X-User-Id", "1");
        request.Headers.Add("Authorization", "Bearer perfil-falsificado");
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Debes iniciar sesión para continuar.", (await response.JsonAsync()).GetProperty("detail").GetString());
        Assert.Empty(_factory.Nominatim.Requests);
        Assert.Empty(_factory.Noise.Reports);
    }

    [Fact]
    public async Task AuthConfig_DisabledByDefault_IsNoStore()
    {
        var response = await _client.GetAsync("/api/auth/config", TestContext.Current.CancellationToken);
        Assert.Equal("""{"turnstile_enabled":false,"turnstile_site_key":""}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    private static string SetCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal));

    private static string Token(HttpResponseMessage response) => SetCookie(response).Split(';')[0].Split('=', 2)[1];

}
