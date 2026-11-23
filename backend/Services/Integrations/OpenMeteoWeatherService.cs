using System.Globalization;
using System.Net;
using System.Text.Json;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace DecibelCity.Api.Services.Integrations;

/// <summary>Clima actual para el mapa.</summary>
public interface IWeatherService
{
    /// <summary>Devuelve el clima de la coordenada (redondeada a 2 decimales), con caché.</summary>
    /// <param name="latitude">Latitud dentro del área operativa.</param>
    /// <param name="longitude">Longitud dentro del área operativa.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Condiciones actuales.</returns>
    Task<WeatherResponse> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente de <c>https://api.open-meteo.com/v1/forecast</c> (sin clave) con <see cref="IMemoryCache"/>:
/// una consulta por coordenada redondeada cada 15 minutos. Los fallos no se guardan en caché.
/// </summary>
/// <param name="httpClient">Cliente HTTP con nombre <c>open-meteo</c>.</param>
/// <param name="options">Configuración de la API.</param>
/// <param name="cache">Caché en memoria del proceso.</param>
public sealed class OpenMeteoWeatherService(HttpClient httpClient, DecibelCityOptions options, IMemoryCache cache) : IWeatherService
{
    private const string Invalid = "El servicio de clima devolvió una respuesta que no se pudo validar.";

    /// <inheritdoc />
    public async Task<WeatherResponse> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var lat = Math.Round(latitude, 2, MidpointRounding.AwayFromZero);
        var lng = Math.Round(longitude, 2, MidpointRounding.AwayFromZero);
        var key = string.Create(CultureInfo.InvariantCulture, $"weather:{lat:F2}:{lng:F2}");
        if (cache.TryGetValue(key, out WeatherResponse? cached) && cached is not null)
        {
            return cached;
        }

        var weather = await FetchAsync(lat, lng, cancellationToken).ConfigureAwait(false);
        cache.Set(key, weather, options.Weather.CacheDuration);
        return weather;
    }

    private async Task<WeatherResponse> FetchAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        const string Fields = "temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m";
        var uri = string.Create(CultureInfo.InvariantCulture,
            $"{options.Weather.OpenMeteoUrl}/v1/forecast?latitude={latitude:F2}&longitude={longitude:F2}&current={Fields}&wind_speed_unit=kmh&timezone=GMT");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherUnavailableException("El servicio de clima no respondió a tiempo. Intenta nuevamente.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            throw new WeatherUnavailableException("No se pudo obtener el clima. Intenta nuevamente.", StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                throw new WeatherUnavailableException("El servicio de clima no está disponible. Intenta más tarde.", StatusCodes.Status503ServiceUnavailable);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new WeatherUnavailableException("No se pudo obtener el clima. Intenta nuevamente.", StatusCodes.Status502BadGateway);
            }

            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
                return Parse(document.RootElement);
            }
            catch (JsonException)
            {
                throw new WeatherUnavailableException(Invalid, StatusCodes.Status502BadGateway);
            }
        }
    }

    private static WeatherResponse Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("current", out var current)
            || current.ValueKind != JsonValueKind.Object)
        {
            throw new WeatherUnavailableException(Invalid, StatusCodes.Status502BadGateway);
        }

        var code = (int)Number(current, "weather_code");
        var (condition, icon) = WmoWeatherCodes.Describe(code);
        var time = current.TryGetProperty("time", out var timeElement) && timeElement.ValueKind == JsonValueKind.String
                   && DateTime.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture,
                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
            : throw new WeatherUnavailableException(Invalid, StatusCodes.Status502BadGateway);

        return new WeatherResponse(
            TemperatureC: Number(current, "temperature_2m"),
            ApparentTemperatureC: Number(current, "apparent_temperature"),
            HumidityPercent: Number(current, "relative_humidity_2m"),
            PrecipitationMm: Number(current, "precipitation"),
            WindSpeedKmh: Number(current, "wind_speed_10m"),
            WeatherCode: code,
            Condition: condition,
            Icon: icon,
            UpdatedAt: time);
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : throw new WeatherUnavailableException(Invalid, StatusCodes.Status502BadGateway);
}

/// <summary>Descripción en español e icono de los códigos meteorológicos WMO 4677 usados por Open-Meteo.</summary>
public static class WmoWeatherCodes
{
    /// <summary>Traduce un código WMO.</summary>
    /// <param name="code">Código <c>weather_code</c>.</param>
    /// <returns>Condición en español e identificador de icono.</returns>
    public static (string Condition, string Icon) Describe(int code) => code switch
    {
        0 => ("Despejado", "clear"),
        1 => ("Mayormente despejado", "mostly_clear"),
        2 => ("Parcialmente nublado", "partly_cloudy"),
        3 => ("Nublado", "cloudy"),
        45 => ("Niebla", "fog"),
        48 => ("Niebla con escarcha", "fog"),
        51 => ("Llovizna ligera", "drizzle"),
        53 => ("Llovizna moderada", "drizzle"),
        55 => ("Llovizna intensa", "drizzle"),
        56 => ("Llovizna helada ligera", "freezing_drizzle"),
        57 => ("Llovizna helada intensa", "freezing_drizzle"),
        61 => ("Lluvia ligera", "rain"),
        63 => ("Lluvia moderada", "rain"),
        65 => ("Lluvia intensa", "rain"),
        66 => ("Lluvia helada ligera", "freezing_rain"),
        67 => ("Lluvia helada intensa", "freezing_rain"),
        71 => ("Nevada ligera", "snow"),
        73 => ("Nevada moderada", "snow"),
        75 => ("Nevada intensa", "snow"),
        77 => ("Granos de nieve", "snow"),
        80 => ("Chubascos ligeros", "showers"),
        81 => ("Chubascos moderados", "showers"),
        82 => ("Chubascos violentos", "showers"),
        85 => ("Chubascos de nieve ligeros", "snow_showers"),
        86 => ("Chubascos de nieve intensos", "snow_showers"),
        95 => ("Tormenta eléctrica", "thunderstorm"),
        96 => ("Tormenta con granizo ligero", "thunderstorm_hail"),
        99 => ("Tormenta con granizo intenso", "thunderstorm_hail"),
        _ => ("Condición no disponible", "unknown"),
    };
}
