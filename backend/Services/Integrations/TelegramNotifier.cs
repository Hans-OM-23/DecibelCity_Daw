using System.Globalization;
using System.Net.Http.Json;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Infrastructure;
using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Services.Integrations;

/// <summary>Notificación saliente de reportes recibidos.</summary>
public interface ITelegramNotifier
{
    /// <summary>
    /// Envía el aviso del reporte. Nunca lanza excepciones: un fallo o un tiempo de espera
    /// se registra como advertencia y no afecta a la respuesta 201.
    /// </summary>
    /// <param name="report">Reporte guardado (sin datos de la cuenta).</param>
    /// <param name="cancellationToken">Cancelación del servidor (no la del cliente).</param>
    /// <returns><c>true</c> si Telegram confirmó el envío.</returns>
    Task<bool> NotifyReportAsync(NoiseReport report, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente de <c>sendMessage</c> de la Bot API. Sin token o chat configurados opera como no-op.
/// El token forma parte de la URL: el cliente HTTP se registra sin registros de peticiones y aquí
/// solo se anotan el estado HTTP o el tipo de excepción.
/// </summary>
/// <param name="httpClient">Cliente HTTP con nombre <c>telegram</c>.</param>
/// <param name="options">Configuración de la API.</param>
/// <param name="logger">Registro de advertencias.</param>
public sealed partial class TelegramNotifier(HttpClient httpClient, DecibelCityOptions options, ILogger<TelegramNotifier> logger)
    : ITelegramNotifier
{
    /// <summary>Tiempo máximo total de la notificación.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<bool> NotifyReportAsync(NoiseReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!options.Telegram.IsConfigured)
        {
            LogNotConfigured(logger);
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var payload = new TelegramMessage(options.Telegram.ChatId, TelegramMessageBuilder.Build(report), DisableWebPagePreview: true);
            using var response = await httpClient.PostAsJsonAsync(
                $"{options.Telegram.ApiUrl}/bot{options.Telegram.BotToken}/sendMessage", payload, ApiJson.Options, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogRejected(logger, (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            LogFailed(logger, "timeout");
            return false;
        }
#pragma warning disable CA1031 // La notificación jamás debe romper el 201: se captura cualquier fallo.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // Solo el tipo: el mensaje podría contener la URL con el token.
            LogFailed(logger, exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram no está configurado; no se envía la notificación del reporte.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram rechazó la notificación del reporte (HTTP {StatusCode}).")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo enviar la notificación del reporte a Telegram ({Reason}).")]
    private static partial void LogFailed(ILogger logger, string reason);

    private sealed record TelegramMessage(string ChatId, string Text, bool DisableWebPagePreview);
}

/// <summary>
/// Texto de la notificación. Solo incluye datos del reporte (lugar verificado, fuente, nivel,
/// descripción, fecha/hora e ID): nunca nombres, apellidos, teléfono, dirección declarada ni usuario.
/// </summary>
public static class TelegramMessageBuilder
{
    private static readonly TimeSpan PeruOffset = TimeSpan.FromHours(-5);

    /// <summary>Construye el mensaje en texto plano (sin <c>parse_mode</c>, sin riesgo de inyección de formato).</summary>
    /// <param name="report">Reporte guardado.</param>
    /// <returns>Texto del mensaje.</returns>
    public static string Build(NoiseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var level = report.Decibels is { } decibels
            ? string.Create(CultureInfo.InvariantCulture, $"{decibels:0.#} dB (estimación relativa, no calibrada)")
            : "No especificado";
        var utc = report.CreatedAt.ToUniversalTime();
        var peru = utc.ToOffset(PeruOffset);
        return string.Join('\n',
            "🔊 Nuevo reporte de ruido - DecibelCity",
            "━━━━━━━━━━━━━━━━━━━━",
            $"📍 Lugar: {report.Location}",
            $"🏷️ Fuente: {NoiseCatalog.LabelFor(report.Source)}",
            $"📊 Nivel estimado: {level}",
            $"📝 Descripción: {report.Description}",
            string.Create(CultureInfo.InvariantCulture,
                $"🕒 Fecha/Hora: {peru:yyyy-MM-dd HH:mm} (hora de Perú) · {utc:yyyy-MM-dd HH:mm} UTC"),
            $"🆔 ID reporte: {report.Id}");
    }
}
