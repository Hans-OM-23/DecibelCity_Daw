using System.Text.Json;
using DecibelCity.Api.Configuration;

namespace DecibelCity.Api.Services.Integrations;

/// <summary>Verificación de tokens de Cloudflare Turnstile.</summary>
public interface ITurnstileVerifier
{
    /// <summary>Indica si el inicio de sesión exige Turnstile.</summary>
    bool IsEnabled { get; }

    /// <summary>Clave pública del widget (vacía si está desactivado).</summary>
    string SiteKey { get; }

    /// <summary>Valida el token con <c>siteverify</c>. Nunca lanza por fallos del proveedor.</summary>
    /// <param name="token">Token generado por el widget.</param>
    /// <param name="remoteIp">IP del cliente (opcional).</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns><c>true</c> solo si Cloudflare confirma <c>success: true</c>.</returns>
    Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente de <c>https://challenges.cloudflare.com/turnstile/v0/siteverify</c>. La clave secreta
/// solo viaja en el cuerpo del POST; no se registra ni se devuelve.
/// </summary>
/// <param name="httpClient">Cliente HTTP con nombre <c>turnstile</c>.</param>
/// <param name="options">Configuración de la API.</param>
/// <param name="logger">Registro de advertencias (sin token ni secreto).</param>
public sealed partial class TurnstileVerifier(HttpClient httpClient, DecibelCityOptions options, ILogger<TurnstileVerifier> logger)
    : ITurnstileVerifier
{
    /// <inheritdoc />
    public bool IsEnabled => options.Turnstile.IsActive;

    /// <inheritdoc />
    public string SiteKey => IsEnabled ? options.Turnstile.SiteKey : string.Empty;

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("secret", options.Turnstile.SecretKey),
            new("response", token),
        };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form.Add(new("remoteip", remoteIp));
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(options.Turnstile.VerifyUrl, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogProviderStatus(logger, (int)response.StatusCode);
                return false;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("success", out var success)
                   && success.ValueKind == JsonValueKind.True;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogProviderFailure(logger, "timeout");
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            LogProviderFailure(logger, exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile respondió con el estado HTTP {StatusCode}.")]
    private static partial void LogProviderStatus(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo verificar Turnstile ({Reason}).")]
    private static partial void LogProviderFailure(ILogger logger, string reason);
}
