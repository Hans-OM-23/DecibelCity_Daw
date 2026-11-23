using System.Net;
using System.Text.Json.Nodes;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Http;

/// <summary>
/// IP del cliente detrás de Cloudflare y de un balanceador (dos saltos de confianza). Se observa a
/// través de la limitación por IP del login (1 petición por minuto).
/// </summary>
public sealed class ForwardedHeadersTests : IDisposable
{
    // ApiFactory.ClientIp (203.0.113.7) actúa como balanceador; 104.16.0.0/13 es un rango de Cloudflare.
    private const string Cloudflare = "104.16.0.1";

    private readonly ApiFactory _factory = new()
    {
        Options = TestOptions.Create() with
        {
            TrustedProxies = ["203.0.113.0/24", "104.16.0.0/13"],
            RateLimit = new RateLimitOptions { Enabled = true, LoginPerMinute = 1 },
        },
    };

    private readonly HttpClient _client;

    public ForwardedHeadersTests() => _client = _factory.CreateBrowserClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task TwoTrustedHops_ResolveRealClientIp()
    {
        // Clientes distintos tras el mismo nodo de Cloudflare: cada uno tiene su propio cupo.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await LoginAsync($"198.51.100.23, {Cloudflare}"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await LoginAsync($"198.51.100.24, {Cloudflare}"));

        // El mismo cliente agota su cupo.
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginAsync($"198.51.100.23, {Cloudflare}"));
    }

    [Fact]
    public async Task UntrustedHop_StopsProcessing_SoSpoofedEntriesAreIgnored()
    {
        // 192.0.2.50 no es de confianza: las entradas a su izquierda (falsificables) no cuentan.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await LoginAsync("198.51.100.1, 192.0.2.50"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginAsync("198.51.100.2, 192.0.2.50"));
    }

    private async Task<HttpStatusCode> LoginAsync(string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new JsonObject
            {
                ["nombre_usuario"] = "no_existe",
                ["contrasena"] = "ClaveIncorrecta#1",
            }),
        };
        request.Headers.TryAddWithoutValidation("Origin", TestOptions.Frontend);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
