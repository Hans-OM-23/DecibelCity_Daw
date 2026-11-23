using System.Globalization;
using System.Net;
using System.Text.Json;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Validation;

namespace DecibelCity.Api.Services.Osm;

/// <summary>Búsqueda con Photon limitada al área de Huancayo (equivalente a <c>PhotonSearcher</c>).</summary>
/// <param name="httpClient">Cliente HTTP con nombre <see cref="HttpClientNames.Photon"/>.</param>
/// <param name="options">Configuración de la API.</param>
public sealed class PhotonPlaceSearcher(HttpClient httpClient, DecibelCityOptions options) : IPlaceSearcher
{
    /// <summary>Máximo de sugerencias devueltas.</summary>
    public const int Limit = 8;

    private const string Unvalidated = "La búsqueda devolvió una respuesta que no se pudo validar.";
    private static readonly MapBounds Bounds = MapBounds.Huancayo;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var uri = string.Create(CultureInfo.InvariantCulture,
            $"{options.PhotonUrl}/api/?q={Uri.EscapeDataString(query)}&limit={Limit * 2}&bbox={Bounds.West},{Bounds.South},{Bounds.East},{Bounds.North}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", options.GeocoderUserAgent);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlaceVerificationException("La búsqueda de lugares no respondió a tiempo. Intenta nuevamente.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            throw new PlaceVerificationException("No se pudo conectar con la búsqueda de lugares. Intenta nuevamente.", StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                throw new PlaceVerificationException("La búsqueda de lugares no está disponible. Intenta más tarde.", StatusCodes.Status503ServiceUnavailable);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new PlaceVerificationException("No se pudo completar la búsqueda de lugares.", StatusCodes.Status502BadGateway);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (JsonException)
            {
                throw new PlaceVerificationException(Unvalidated, StatusCodes.Status502BadGateway);
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("features", out var features)
                    || features.ValueKind != JsonValueKind.Array)
                {
                    throw new PlaceVerificationException(Unvalidated, StatusCodes.Status502BadGateway);
                }

                var suggestions = new List<PlaceSuggestion>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var feature in features.EnumerateArray())
                {
                    if (ToSuggestion(feature) is { } suggestion && seen.Add(suggestion.PlaceId))
                    {
                        suggestions.Add(suggestion);
                    }

                    if (suggestions.Count == Limit)
                    {
                        break;
                    }
                }

                return suggestions;
            }
        }
    }

    private static PlaceSuggestion? ToSuggestion(JsonElement feature)
    {
        if (feature.ValueKind != JsonValueKind.Object
            || !feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object
            || !feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object
            || !geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() != 2
            || coordinates[0].ValueKind != JsonValueKind.Number || coordinates[1].ValueKind != JsonValueKind.Number
            || !coordinates[0].TryGetDouble(out var longitude) || !coordinates[1].TryGetDouble(out var latitude)
            || !double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            return null;
        }

        if (Text(properties, "countrycode") != "PE" || !Bounds.Contains(latitude, longitude))
        {
            return null;
        }

        var key = Text(properties, "osm_key");
        if (key == "boundary" || (key == "place" && Text(properties, "osm_value") is { } value && OsmRules.GenericPlaceValues.Contains(value)))
        {
            return null;
        }

        var placeId = $"{Text(properties, "osm_type")}{Text(properties, "osm_id")}";
        if (!OsmPlaceId.IsValid(placeId))
        {
            return null;
        }

        var street = string.Join(' ', new[] { Text(properties, "street"), Text(properties, "housenumber") }.Where(p => !string.IsNullOrEmpty(p)));
        var name = (Text(properties, "name") is { Length: > 0 } n ? n : street).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var details = new[]
        {
            street != name ? street : null,
            Text(properties, "district") is { Length: > 0 } district ? district : Text(properties, "locality"),
            Text(properties, "city"),
        };
        var address = string.Join(", ", details.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal));
        return new PlaceSuggestion(placeId, OsmRules.Truncate(name, 200), OsmRules.Truncate(address, 400), latitude, longitude);
    }

    // Equivale a str(valor) de Python para textos y números; otros tipos se ignoran.
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;
}
