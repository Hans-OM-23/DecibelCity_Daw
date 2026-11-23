namespace DecibelCity.Api.Models.Dtos;

/// <summary>Centro inicial del mapa (Huancayo).</summary>
/// <param name="Lat">Latitud en grados decimales.</param>
/// <param name="Lng">Longitud en grados decimales.</param>
public sealed record MapCenter(double Lat, double Lng)
{
    /// <summary>Centro oficial del área operativa, idéntico a <c>schemas/maps.py</c> del backend Python retirado.</summary>
    public static MapCenter Huancayo { get; } = new(-12.067, -75.210);
}

/// <summary>Límites del área operativa de DecibelCity.</summary>
/// <param name="South">Latitud mínima.</param>
/// <param name="West">Longitud mínima.</param>
/// <param name="North">Latitud máxima.</param>
/// <param name="East">Longitud máxima.</param>
public sealed record MapBounds(double South, double West, double North, double East)
{
    /// <summary>Límites oficiales, idénticos a <c>schemas/maps.py</c> del backend Python retirado.</summary>
    public static MapBounds Huancayo { get; } = new(-12.16, -75.30, -11.95, -75.12);

    /// <summary>Indica si la coordenada (inclusive) está dentro de los límites.</summary>
    /// <param name="latitude">Latitud.</param>
    /// <param name="longitude">Longitud.</param>
    /// <returns><c>true</c> si está dentro del área.</returns>
    public bool Contains(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude)
        && South <= latitude && latitude <= North && West <= longitude && longitude <= East;
}

/// <summary>Datos públicos del mapa para el navegador. OpenStreetMap no utiliza claves.</summary>
/// <param name="TileUrl">Plantilla de teselas.</param>
/// <param name="Attribution">Atribución obligatoria de OpenStreetMap.</param>
/// <param name="Center">Centro inicial.</param>
/// <param name="Bounds">Límites del área operativa.</param>
public sealed record PublicMapsConfig(string TileUrl, string Attribution, MapCenter Center, MapBounds Bounds);

/// <summary>Sugerencia de lugar de OpenStreetMap. Solo orienta: se verifica de nuevo al guardar.</summary>
/// <param name="PlaceId">Identificador OSM compacto (N/W/R + número).</param>
/// <param name="Name">Nombre corto del lugar.</param>
/// <param name="Address">Dirección resumida.</param>
/// <param name="Lat">Latitud.</param>
/// <param name="Lng">Longitud.</param>
public sealed record PlaceSuggestion(string PlaceId, string Name, string Address, double Lat, double Lng);
