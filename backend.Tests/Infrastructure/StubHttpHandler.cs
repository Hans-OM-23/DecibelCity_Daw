using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DecibelCity.Api.Tests.Infrastructure;

/// <summary>Petición saliente registrada por el doble HTTP.</summary>
/// <param name="Method">Método.</param>
/// <param name="Uri">URI completa.</param>
/// <param name="UserAgent">Cabecera User-Agent.</param>
/// <param name="Body">Cuerpo como texto.</param>
/// <param name="Timestamp">Marca de tiempo (Stopwatch) al recibirla.</param>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string UserAgent, string Body, long Timestamp);

/// <summary>
/// Doble de <see cref="HttpMessageHandler"/> que registra las peticiones y responde con una función
/// configurable. No se libera al rotar el handler de <c>IHttpClientFactory</c>.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>Función que produce la respuesta.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    /// <summary>Peticiones recibidas.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Responde siempre con el JSON indicado.</summary>
    /// <param name="value">Objeto a serializar.</param>
    /// <param name="status">Código HTTP.</param>
    public void RespondJson(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        Responder = (_, _) => Task.FromResult(Json(value, status));

    /// <summary>Responde siempre con el código indicado y cuerpo vacío.</summary>
    /// <param name="status">Código HTTP.</param>
    public void RespondStatus(HttpStatusCode status) =>
        Responder = (_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });

    /// <summary>Lanza la excepción indicada (fallo de red).</summary>
    /// <param name="exception">Excepción.</param>
    public void Throw(Exception exception) => Responder = (_, _) => Task.FromException<HttpResponseMessage>(exception);

    /// <summary>Crea una respuesta JSON.</summary>
    /// <param name="value">Objeto.</param>
    /// <param name="status">Código.</param>
    /// <returns>Respuesta.</returns>
    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            _requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(' ', agents) : string.Empty,
                body,
                Stopwatch.GetTimestamp()));
        }

        return await Responder(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Intencionadamente vacío: la fábrica de HttpClient puede rotar el handler durante la prueba.
    }
}
