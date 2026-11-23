using DecibelCity.Api.Configuration;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Services;
using DecibelCity.Api.Services.Integrations;
using DecibelCity.Api.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DecibelCity.Api.Endpoints;

/// <summary>Endpoints públicos: salud, mapa, puntos críticos, fuentes, lugares y clima.</summary>
public static class PublicEndpoints
{
    /// <summary>Registra los endpoints públicos bajo <c>/api</c>.</summary>
    /// <param name="routes">Constructor de rutas.</param>
    /// <returns>El mismo constructor.</returns>
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api").WithTags("DecibelCity");

        api.MapGet("/health", () => TypedResults.Ok(HealthResponse.Ok))
            .WithName("Health")
            .WithSummary("Estado del servicio");

        api.MapGet("/hotspots", ListHotspotsAsync)
            .WithName("ListHotspots")
            .WithSummary("Puntos críticos ilustrativos")
            .WithDescription("Datos de ejemplo con posición aproximada de OpenStreetMap; no son mediciones calibradas ni oficiales.");

        api.MapGet("/maps/config", (DecibelCityOptions options) => TypedResults.Ok(
                new PublicMapsConfig(options.MapTileUrl, options.MapAttribution, MapCenter.Huancayo, MapBounds.Huancayo)))
            .WithName("MapsConfig")
            .WithSummary("Configuración pública del mapa (OpenStreetMap, sin claves)");

        api.MapGet("/noise-sources", ListSourcesAsync)
            .WithName("ListNoiseSources")
            .WithSummary("Catálogo de fuentes de ruido");

        api.MapGet("/places/search", SearchPlacesAsync)
            .WithName("SearchPlaces")
            .WithSummary("Sugerencias de lugares (Photon) limitadas a Huancayo")
            .WithDescription("Parámetro q de 3 a 120 caracteres. Las sugerencias no se guardan y se verifican de nuevo al enviar el reporte.")
            .Produces<ValidationErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/places/reverse", ReversePlaceAsync)
            .WithName("ReversePlace")
            .WithSummary("Lugar OSM específico para coordenadas del navegador (Nominatim /reverse)")
            .WithDescription("Parámetros lat y lng dentro del área de Huancayo. Comparte el límite de 1 petición por segundo con la verificación.")
            .Produces<ValidationErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/weather", GetWeatherAsync)
            .WithName("Weather")
            .WithSummary("Clima actual (Open-Meteo) con caché de 15 minutos")
            .WithDescription("Parámetros opcionales lat y lng dentro del área de Huancayo; por defecto, el centro del mapa.")
            .Produces<ValidationErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return routes;
    }

    private static async Task<Ok<IReadOnlyList<Hotspot>>> ListHotspotsAsync(INoiseService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListHotspotsAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Ok<IReadOnlyList<NoiseSourceOption>>> ListSourcesAsync(INoiseService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListSourcesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Ok<IReadOnlyList<PlaceSuggestion>>> SearchPlacesAsync(
        HttpRequest request, INoiseService service, CancellationToken cancellationToken)
    {
        var q = QueryValidation.RequiredText(request, "q", 3, 120);
        var query = string.Join(' ', q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (JsonObjectValidator.TextLength(query) < 3)
        {
            throw new PlaceVerificationException("Escribe al menos 3 caracteres.");
        }

        return TypedResults.Ok(await service.SearchPlacesAsync(query, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Ok<PlaceSuggestion>> ReversePlaceAsync(
        HttpRequest request, INoiseService service, CancellationToken cancellationToken)
    {
        var (lat, lng) = QueryValidation.Coordinates(request, required: true)!.Value;
        EnsureInsideCoverage(lat, lng);
        return TypedResults.Ok(await service.ReversePlaceAsync(lat, lng, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Ok<WeatherResponse>> GetWeatherAsync(
        HttpRequest request, IWeatherService weather, CancellationToken cancellationToken)
    {
        var (lat, lng) = QueryValidation.Coordinates(request, required: false) ?? (MapCenter.Huancayo.Lat, MapCenter.Huancayo.Lng);
        EnsureInsideCoverage(lat, lng);
        return TypedResults.Ok(await weather.GetCurrentAsync(lat, lng, cancellationToken).ConfigureAwait(false));
    }

    private static void EnsureInsideCoverage(double lat, double lng)
    {
        if (!MapBounds.Huancayo.Contains(lat, lng))
        {
            throw new PlaceVerificationException("Las coordenadas están fuera del área de cobertura de Huancayo, Perú.");
        }
    }
}
