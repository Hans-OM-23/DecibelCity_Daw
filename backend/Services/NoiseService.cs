using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Repositories;
using DecibelCity.Api.Services.Integrations;
using DecibelCity.Api.Services.Osm;

namespace DecibelCity.Api.Services;

/// <summary>Catálogo ilustrativo, búsqueda de lugares y recepción de reportes.</summary>
public interface INoiseService
{
    /// <summary>Puntos críticos ilustrativos.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Puntos.</returns>
    Task<IReadOnlyList<Hotspot>> ListHotspotsAsync(CancellationToken cancellationToken = default);

    /// <summary>Catálogo de fuentes.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Fuentes.</returns>
    Task<IReadOnlyList<NoiseSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sugerencias de Photon (no se guardan).</summary>
    /// <param name="query">Texto normalizado.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Sugerencias.</returns>
    Task<IReadOnlyList<PlaceSuggestion>> SearchPlacesAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Lugar OSM específico para unas coordenadas del navegador.</summary>
    /// <param name="latitude">Latitud.</param>
    /// <param name="longitude">Longitud.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Sugerencia verificada.</returns>
    Task<PlaceSuggestion> ReversePlaceAsync(double latitude, double longitude, CancellationToken cancellationToken = default);

    /// <summary>Verifica el lugar en Nominatim, guarda el reporte y notifica (sin bloquear el 201 ante fallos).</summary>
    /// <param name="payload">Formulario validado.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Comprobante.</returns>
    Task<NoiseReport> CreateReportAsync(NoiseReportCreate payload, CancellationToken cancellationToken = default);
}

/// <summary>Implementación equivalente a <c>services/noise_service.py</c> del backend Python retirado más la notificación de Telegram.</summary>
/// <param name="repository">Persistencia.</param>
/// <param name="placeVerifier">Verificación Nominatim.</param>
/// <param name="reverseGeocoder">Geocodificación inversa Nominatim.</param>
/// <param name="placeSearcher">Búsqueda Photon.</param>
/// <param name="notifier">Notificación de Telegram.</param>
/// <param name="time">Reloj.</param>
public sealed class NoiseService(
    INoiseRepository repository,
    IPlaceVerifier placeVerifier,
    IReverseGeocoder reverseGeocoder,
    IPlaceSearcher placeSearcher,
    ITelegramNotifier notifier,
    TimeProvider time) : INoiseService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Hotspot>> ListHotspotsAsync(CancellationToken cancellationToken = default) =>
        repository.ListHotspotsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<NoiseSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default) =>
        repository.ListSourcesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaceSuggestion>> SearchPlacesAsync(string query, CancellationToken cancellationToken = default) =>
        placeSearcher.SearchAsync(query, cancellationToken);

    /// <inheritdoc />
    public Task<PlaceSuggestion> ReversePlaceAsync(double latitude, double longitude, CancellationToken cancellationToken = default) =>
        reverseGeocoder.ReverseAsync(latitude, longitude, cancellationToken);

    /// <inheritdoc />
    public async Task<NoiseReport> CreateReportAsync(NoiseReportCreate payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var place = await placeVerifier.VerifyAsync(payload.PlaceId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        // Precisión de microsegundos, como TIMESTAMPTZ: la respuesta coincide con lo almacenado.
        var createdAt = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % 10), TimeSpan.Zero);
        var report = new NoiseReport(
            Location: place.Address,
            PlaceId: place.PlaceId,
            Source: payload.Source,
            Description: payload.Description,
            Decibels: payload.Decibels,
            Id: Guid.NewGuid().ToString("D"),
            Latitude: place.Latitude,
            Longitude: place.Longitude,
            CreatedAt: createdAt,
            Status: "received");
        await repository.SaveReportAsync(report, cancellationToken).ConfigureAwait(false);

        // Ya guardado: la notificación no depende de que el cliente siga conectado y nunca lanza.
        await notifier.NotifyReportAsync(report, CancellationToken.None).ConfigureAwait(false);
        return report;
    }
}
