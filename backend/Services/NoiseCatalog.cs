using System.Collections.Frozen;
using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Services;

/// <summary>Catálogo de fuentes y puntos ilustrativos (equivalente a <c>services/noise_service.py</c> del backend Python retirado).</summary>
public static class NoiseCatalog
{
    /// <summary>Fuentes de ruido del formulario, en el orden de siembra.</summary>
    public static IReadOnlyList<NoiseSourceOption> Sources { get; } =
    [
        new("traffic", "Tráfico vehicular y transporte público"),
        new("commerce", "Comercio ambulante y perifoneo"),
        new("construction", "Construcción y obras viales"),
        new("entertainment", "Música, eventos y locales nocturnos"),
        new("other", "Otra fuente de ruido"),
    ];

    /// <summary>Códigos permitidos (coinciden con el CHECK de <c>tipos_fuente_ruido</c>).</summary>
    public static FrozenSet<string> SourceCodes { get; } = Sources.Select(s => s.Value).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Etiqueta en español de un código de fuente.</summary>
    /// <param name="code">Código.</param>
    /// <returns>Etiqueta o el propio código si no se reconoce.</returns>
    public static string LabelFor(string code) => Sources.FirstOrDefault(s => s.Value == code)?.Label ?? code;

    /// <summary>Categoría del catálogo para cada código de fuente.</summary>
    public static IReadOnlyDictionary<string, string> SourceCategories { get; } = new Dictionary<string, string>
    {
        ["traffic"] = "TRANSPORTE",
        ["commerce"] = "COMERCIO",
        ["construction"] = "CONSTRUCCION",
        ["entertainment"] = "ACTIVIDAD_RECREATIVA",
        ["other"] = "OTRAS",
    };

    /// <summary>
    /// Datos de demostración del diseño: no representan mediciones verificadas.
    /// Coordenadas aproximadas de OpenStreetMap (© colaboradores de OSM, ODbL).
    /// </summary>
    public static IReadOnlyList<Hotspot> DemoHotspots { get; } =
    [
        new(
            Id: "sector-01",
            Name: "Calle Real y Av. Giráldez",
            Area: "Centro Histórico",
            District: "Huancayo",
            Sector: "Sector 01 · Centro",
            Decibels: 74,
            Source: "traffic",
            SourceLabel: "Tráfico vehicular y transporte público",
            Description: "Congestión recurrente de microbuses y uso constante de bocinas en horas punta de la tarde.",
            TimeLabel: "Franja diurna (12 h – 19 h)",
            X: 27,
            Y: 32,
            ReferenceLimit: 60,
            Illustrative: true,
            Latitude: -12.06865,
            Longitude: -75.21025),
        new(
            Id: "sector-02",
            Name: "Mercado Mayorista",
            Area: "Av. Ferrocarril",
            District: "Huancayo",
            Sector: "Sector 02 · Comercial",
            Decibels: 78,
            Source: "commerce",
            SourceLabel: "Comercio ambulante y perifoneo",
            Description: "Altavoces comerciales promocionales y descarga de camiones en zona peatonal.",
            TimeLabel: "Franja matutina y comercial",
            X: 49,
            Y: 49,
            ReferenceLimit: 70,
            Illustrative: true,
            Latitude: -12.07304,
            Longitude: -75.20502),
        new(
            Id: "sector-03",
            Name: "Intersección Huancavelica & Jr. Ica",
            Area: "Eje de Carga Urbana",
            District: "Chilca",
            Sector: "Sector 03 · Conexión sur",
            Decibels: 69,
            Source: "traffic",
            SourceLabel: "Tráfico pesado y obras viales",
            Description: "Circulación de transporte interprovincial y maquinaria urbana en horarios diurnos.",
            TimeLabel: "Franja diurna continua",
            X: 60,
            Y: 55,
            ReferenceLimit: 60,
            Illustrative: true,
            Latitude: -12.07323,
            Longitude: -75.21301),
    ];
}
