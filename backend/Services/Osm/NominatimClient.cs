using System.Globalization;
using System.Net;
using System.Text.Json;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Domain;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Validation;

namespace DecibelCity.Api.Services.Osm;

/// <summary>
/// Cliente de Nominatim con User-Agent identificable y separación mínima entre peticiones
/// (compartida por <c>/lookup</c> y <c>/reverse</c>). Nunca autocompleta con Nominatim.
/// Los errores son seguros: no incluyen la respuesta del proveedor.
/// </summary>
/// <param name="httpClient">Cliente HTTP con nombre <see cref="HttpClientNames.Nominatim"/>.</param>
/// <param name="options">Configuración de la API.</param>
/// <param name="throttle">Separación compartida de 1 petición por segundo.</param>
public sealed class NominatimClient(HttpClient httpClient, DecibelCityOptions options, NominatimThrottle throttle)
    : IPlaceVerifier, IReverseGeocoder
{
    private const string InvalidPlace = "Selecciona una calle, plaza o establecimiento específico dentro de Huancayo.";
    private const string OutsideCoverage = "El lugar debe estar dentro del área de cobertura de Huancayo, Perú.";
    private const string Unvalidated = "El servicio de mapas devolvió una respuesta que no se pudo validar.";
    private const string Unverifiable = "El servicio de mapas devolvió un lugar que no se pudo verificar.";
    private const string NoSpecificPlace =
        "No se encontró una calle, plaza o establecimiento específico en tu ubicación. Busca el lugar por su nombre.";

    private static readonly MapBounds Bounds = MapBounds.Huancayo;

    /// <inheritdoc />
    public async Task<VerifiedPlace> VerifyAsync(string placeId, CancellationToken cancellationToken = default)
    {
        if (!OsmPlaceId.IsValid(placeId))
        {
            throw new PlaceVerificationException("Selecciona un lugar válido de las sugerencias.");
        }

        var uri = $"{options.NominatimUrl}/lookup?osm_ids={Uri.EscapeDataString(placeId)}&format=jsonv2&addressdetails=1&accept-language=es";
        using var document = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new PlaceVerificationException(Unvalidated, StatusCodes.Status502BadGateway);
        }

        var length = root.GetArrayLength();
        if (length == 0)
        {
            throw new PlaceVerificationException("El lugar seleccionado no existe o ya no está disponible.");
        }

        if (length != 1)
        {
            throw new PlaceVerificationException(Unverifiable, StatusCodes.Status502BadGateway);
        }

        var place = Validate(root[0], InvalidPlace, placeId);
        return new VerifiedPlace(place.PlaceId, place.DisplayName, place.Latitude, place.Longitude);
    }

    /// <inheritdoc />
    public async Task<PlaceSuggestion> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        if (!Bounds.Contains(latitude, longitude))
        {
            throw new PlaceVerificationException("Las coordenadas están fuera del área de cobertura de Huancayo, Perú.");
        }

        var uri = string.Create(CultureInfo.InvariantCulture,
            $"{options.NominatimUrl}/reverse?lat={latitude:R}&lon={longitude:R}&format=jsonv2&addressdetails=1&accept-language=es&zoom=18");
        using var document = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new PlaceVerificationException(Unvalidated, StatusCodes.Status502BadGateway);
        }

        // Nominatim responde 200 con {"error": "Unable to geocode"} cuando no hay resultado.
        if (root.TryGetProperty("error", out _))
        {
            throw new PlaceVerificationException(NoSpecificPlace);
        }

        var place = Validate(root, NoSpecificPlace, requestedId: null);
        return new PlaceSuggestion(
            place.PlaceId,
            OsmRules.Truncate(place.Name, 200),
            OsmRules.Truncate(place.ShortAddress, 400),
            place.Latitude,
            place.Longitude);
    }

    private async Task<JsonDocument> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await throttle.RunAsync(async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", options.GeocoderUserAgent);
                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlaceVerificationException("El servicio de mapas no respondió a tiempo. Intenta nuevamente.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            throw new PlaceVerificationException("No se pudo verificar la ubicación. Intenta nuevamente.", StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || status >= 500)
            {
                throw new PlaceVerificationException("La verificación de ubicaciones no está disponible. Intenta más tarde.", StatusCodes.Status503ServiceUnavailable);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new PlaceVerificationException("No se pudo verificar la ubicación. Intenta nuevamente.", StatusCodes.Status502BadGateway);
            }

            try
            {
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new PlaceVerificationException(Unvalidated, StatusCodes.Status502BadGateway);
            }
        }
    }

    private static NominatimPlace Validate(JsonElement data, string invalidMessage, string? requestedId)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new PlaceVerificationException(Unvalidated, StatusCodes.Status502BadGateway);
        }

        var osmType = data.TryGetProperty("osm_type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()!
            : null;
        long? osmId = data.TryGetProperty("osm_id", out var idElement) && idElement.ValueKind == JsonValueKind.Number
                      && idElement.TryGetInt64(out var id) ? id : null;
        var placeId = osmType is { Length: > 0 } && osmId is not null
            ? string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(osmType[0])}{osmId}")
            : null;

        if (requestedId is not null)
        {
            if (placeId != requestedId)
            {
                throw new PlaceVerificationException(Unverifiable, StatusCodes.Status502BadGateway);
            }
        }
        else if (!OsmPlaceId.IsValid(placeId))
        {
            throw new PlaceVerificationException(invalidMessage);
        }

        var displayName = StringProperty(data, "display_name")?.Trim();
        if (string.IsNullOrEmpty(displayName) || JsonObjectValidator.TextLength(displayName) > 1000)
        {
            throw new PlaceVerificationException(invalidMessage);
        }

        var category = StringProperty(data, "category");
        if (!data.TryGetProperty("place_rank", out var rankElement) || rankElement.ValueKind != JsonValueKind.Number
            || !rankElement.TryGetInt32(out var rank) || rank < OsmRules.MinPlaceRank || category == "boundary")
        {
            throw new PlaceVerificationException(invalidMessage);
        }

        if (category == "place" && StringProperty(data, "type") is { } placeType && OsmRules.GenericPlaceValues.Contains(placeType))
        {
            throw new PlaceVerificationException(invalidMessage);
        }

        // Nominatim envía las coordenadas como texto.
        if (!TryCoordinate(data, "lat", out var latitude) || !TryCoordinate(data, "lon", out var longitude)
            || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            throw new PlaceVerificationException(invalidMessage);
        }

        var address = data.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object
            ? addressElement
            : default;
        var isPeru = address.ValueKind == JsonValueKind.Object && StringProperty(address, "country_code") == "pe";
        if (!isPeru || !Bounds.Contains(latitude, longitude))
        {
            throw new PlaceVerificationException(OutsideCoverage);
        }

        var (name, shortAddress) = Describe(data, address, displayName);
        return new NominatimPlace(placeId!, displayName, name, shortAddress, latitude, longitude);
    }

    private static (string Name, string Address) Describe(JsonElement data, JsonElement address, string displayName)
    {
        var road = StringProperty(address, "road") ?? StringProperty(address, "pedestrian") ?? StringProperty(address, "footway");
        var street = string.Join(' ', new[] { road, StringProperty(address, "house_number") }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var name = StringProperty(data, "name")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = street.Length > 0 ? street : displayName.Split(',')[0].Trim();
        }

        var parts = new[]
        {
            street != name ? street : null,
            StringProperty(address, "suburb") ?? StringProperty(address, "neighbourhood") ?? StringProperty(address, "quarter") ?? StringProperty(address, "city_district"),
            StringProperty(address, "city") ?? StringProperty(address, "town") ?? StringProperty(address, "village") ?? StringProperty(address, "municipality"),
        };
        var shortAddress = string.Join(", ", parts.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal));
        return (name, shortAddress.Length > 0 ? shortAddress : displayName);
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryCoordinate(JsonElement data, string name, out double value)
    {
        value = double.NaN;
        if (!data.TryGetProperty(name, out var element))
        {
            return false;
        }

        var parsed = element.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            JsonValueKind.Number => element.TryGetDouble(out value),
            _ => false,
        };
        return parsed && double.IsFinite(value);
    }

    private sealed record NominatimPlace(string PlaceId, string DisplayName, string Name, string ShortAddress, double Latitude, double Longitude);
}
