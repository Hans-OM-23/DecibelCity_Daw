namespace DecibelCity.Api.Models.Dtos;

/// <summary>Estado del servicio.</summary>
/// <param name="Status">Siempre <c>ok</c>.</param>
/// <param name="Service">Nombre del servicio.</param>
public sealed record HealthResponse(string Status, string Service)
{
    /// <summary>Respuesta única de salud, idéntica al backend Python.</summary>
    public static HealthResponse Ok { get; } = new("ok", "DecibelCity API");
}

/// <summary>Opción del catálogo de fuentes de ruido.</summary>
/// <param name="Value">Código (<c>traffic</c>, <c>commerce</c>, <c>construction</c>, <c>entertainment</c>, <c>other</c>).</param>
/// <param name="Label">Etiqueta en español.</param>
public sealed record NoiseSourceOption(string Value, string Label);

/// <summary>Punto crítico ilustrativo del mapa (no es una medición verificada).</summary>
/// <param name="Id">Identificador del punto.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Area">Área o referencia urbana.</param>
/// <param name="District">Distrito.</param>
/// <param name="Sector">Sector descriptivo.</param>
/// <param name="Decibels">Nivel ilustrativo en dB.</param>
/// <param name="Source">Código de la fuente.</param>
/// <param name="SourceLabel">Etiqueta de la fuente.</param>
/// <param name="Description">Descripción.</param>
/// <param name="TimeLabel">Franja horaria.</param>
/// <param name="X">Posición relativa horizontal (0–100).</param>
/// <param name="Y">Posición relativa vertical (0–100).</param>
/// <param name="ReferenceLimit">Límite de referencia en dB.</param>
/// <param name="Illustrative">Siempre <c>true</c> para los datos de ejemplo.</param>
/// <param name="Latitude">Latitud aproximada de OpenStreetMap.</param>
/// <param name="Longitude">Longitud aproximada de OpenStreetMap.</param>
public sealed record Hotspot(
    string Id,
    string Name,
    string Area,
    string District,
    string Sector,
    double Decibels,
    string Source,
    string SourceLabel,
    string Description,
    string TimeLabel,
    double X,
    double Y,
    double ReferenceLimit,
    bool Illustrative,
    double? Latitude,
    double? Longitude);

/// <summary>Datos enviados por el formulario de reporte.</summary>
public sealed record NoiseReportCreate
{
    /// <summary>Texto del lugar mostrado en el formulario (5–1000 caracteres).</summary>
    public required string Location { get; init; }

    /// <summary>Identificador OSM elegido entre las sugerencias (N/W/R + número).</summary>
    public required string PlaceId { get; init; }

    /// <summary>Código de la fuente de ruido.</summary>
    public required string Source { get; init; }

    /// <summary>Descripción (10–500 caracteres).</summary>
    public required string Description { get; init; }

    /// <summary>Nivel estimado opcional (30–120 dB). Relativo, no calibrado.</summary>
    public double? Decibels { get; init; }
}

/// <summary>Comprobante del reporte recibido.</summary>
/// <param name="Location">Dirección verificada por Nominatim.</param>
/// <param name="PlaceId">Identificador OSM verificado.</param>
/// <param name="Source">Código de la fuente.</param>
/// <param name="Description">Descripción.</param>
/// <param name="Decibels">Nivel estimado o <c>null</c>.</param>
/// <param name="Id">Identificador del reporte (UUID).</param>
/// <param name="Latitude">Latitud verificada.</param>
/// <param name="Longitude">Longitud verificada.</param>
/// <param name="CreatedAt">Fecha y hora UTC de recepción.</param>
/// <param name="Status">Siempre <c>received</c>.</param>
public sealed record NoiseReport(
    string Location,
    string PlaceId,
    string Source,
    string Description,
    double? Decibels,
    string Id,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAt,
    string Status);
