using System.Collections.Frozen;
using System.Text;
using DecibelCity.Api.Models.Domain;
using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Services.Osm;

/// <summary>Nombres de los clientes HTTP registrados (sustituibles en pruebas).</summary>
public static class HttpClientNames
{
    /// <summary>Nominatim (lookup y reverse).</summary>
    public const string Nominatim = "nominatim";

    /// <summary>Photon (búsqueda).</summary>
    public const string Photon = "photon";

    /// <summary>Cloudflare Turnstile.</summary>
    public const string Turnstile = "turnstile";

    /// <summary>Telegram Bot API.</summary>
    public const string Telegram = "telegram";

    /// <summary>Open-Meteo.</summary>
    public const string OpenMeteo = "open-meteo";
}

/// <summary>Verifica en el servidor un identificador OSM antes de guardar un reporte.</summary>
public interface IPlaceVerifier
{
    /// <summary>Consulta Nominatim <c>/lookup</c> y aplica las reglas de lugar específico en Huancayo.</summary>
    /// <param name="placeId">Identificador N/W/R + número.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Lugar verificado.</returns>
    Task<VerifiedPlace> VerifyAsync(string placeId, CancellationToken cancellationToken = default);
}

/// <summary>Geocodificación inversa (coordenadas del navegador → lugar OSM verificado).</summary>
public interface IReverseGeocoder
{
    /// <summary>Consulta Nominatim <c>/reverse</c> y aplica las mismas reglas que la verificación.</summary>
    /// <param name="latitude">Latitud dentro del área operativa.</param>
    /// <param name="longitude">Longitud dentro del área operativa.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Sugerencia con un <c>place_id</c> válido.</returns>
    Task<PlaceSuggestion> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <summary>Sugerencias de búsqueda (Photon), limitadas a Huancayo y sin guardar.</summary>
public interface IPlaceSearcher
{
    /// <summary>Busca lugares.</summary>
    /// <param name="query">Texto normalizado (3–120 caracteres).</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta ocho sugerencias.</returns>
    Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>Reglas compartidas de OpenStreetMap (equivalentes a <c>services/osm_places.py</c> del backend Python retirado).</summary>
public static class OsmRules
{
    /// <summary>Rango 25 o más: plazas, calles, edificios y comercios. Excluye barrios, distritos y ciudades.</summary>
    public const int MinPlaceRank = 25;

    /// <summary>Valores genéricos de <c>place=*</c> que no identifican un lugar específico.</summary>
    public static FrozenSet<string> GenericPlaceValues { get; } = new[]
    {
        "city", "town", "village", "hamlet", "suburb", "quarter", "neighbourhood", "city_block",
        "county", "district", "state", "region", "province", "country", "continent", "island",
        "islet", "municipality", "borough", "locality",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Recorta un texto a un máximo de caracteres Unicode sin partir pares sustitutos.</summary>
    /// <param name="value">Texto.</param>
    /// <param name="maxRunes">Máximo de caracteres.</param>
    /// <returns>Texto recortado.</returns>
    public static string Truncate(string value, int maxRunes)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ == maxRunes)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
