namespace DecibelCity.Api.Models.Dtos;

/// <summary>Error con mensaje en español: <c>{ "detail": "..." }</c>.</summary>
/// <param name="Detail">Mensaje para la persona usuaria.</param>
public sealed record ErrorResponse(string Detail);

/// <summary>Elemento de un error de validación (sin valores de entrada).</summary>
/// <param name="Type">Tipo de error (compatible con Pydantic: <c>missing</c>, <c>extra_forbidden</c>...).</param>
/// <param name="Loc">Ubicación (<c>["body", "campo"]</c> o <c>["query", "q"]</c>).</param>
/// <param name="Msg">Mensaje en español.</param>
public sealed record ValidationErrorItem(string Type, IReadOnlyList<string> Loc, string Msg);

/// <summary>Respuesta 422 sanitizada: <c>{ "detail": [{ "type", "loc", "msg" }] }</c>.</summary>
/// <param name="Detail">Errores encontrados.</param>
public sealed record ValidationErrorResponse(IReadOnlyList<ValidationErrorItem> Detail);

/// <summary>Condiciones meteorológicas actuales (Open-Meteo).</summary>
/// <param name="TemperatureC">Temperatura en °C.</param>
/// <param name="ApparentTemperatureC">Sensación térmica en °C.</param>
/// <param name="HumidityPercent">Humedad relativa (%).</param>
/// <param name="PrecipitationMm">Precipitación (mm).</param>
/// <param name="WindSpeedKmh">Velocidad del viento (km/h).</param>
/// <param name="WeatherCode">Código WMO.</param>
/// <param name="Condition">Descripción en español.</param>
/// <param name="Icon">Identificador de icono (<c>clear</c>, <c>rain</c>...).</param>
/// <param name="UpdatedAt">Hora UTC de la observación.</param>
public sealed record WeatherResponse(
    double TemperatureC,
    double ApparentTemperatureC,
    double HumidityPercent,
    double PrecipitationMm,
    double WindSpeedKmh,
    int WeatherCode,
    string Condition,
    string Icon,
    DateTimeOffset UpdatedAt);
