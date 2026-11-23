using System.Globalization;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Validation;

/// <summary>Validación de parámetros de consulta con errores 422 sanitizados (<c>loc = ["query", nombre]</c>).</summary>
public static class QueryValidation
{
    /// <summary>Texto obligatorio con límites de longitud (sin recortar, como <c>Query(min_length, max_length)</c>).</summary>
    /// <param name="request">Petición.</param>
    /// <param name="name">Parámetro.</param>
    /// <param name="minLength">Mínimo de caracteres.</param>
    /// <param name="maxLength">Máximo de caracteres.</param>
    /// <returns>Valor recibido.</returns>
    public static string RequiredText(HttpRequest request, string name, int minLength, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Query.TryGetValue(name, out var values) || values.Count == 0 || values[^1] is null)
        {
            throw Error(name, "missing");
        }

        var value = values[^1]!;
        var length = JsonObjectValidator.TextLength(value);
        if (length < minLength)
        {
            throw Error(name, "string_too_short");
        }

        return length > maxLength ? throw Error(name, "string_too_long") : value;
    }

    /// <summary>Lee un par de coordenadas (ambas obligatorias u opcionales a la vez), finitas.</summary>
    /// <param name="request">Petición.</param>
    /// <param name="required">Si deben venir obligatoriamente.</param>
    /// <returns>Coordenadas o <c>null</c> si son opcionales y no se enviaron.</returns>
    public static (double Lat, double Lng)? Coordinates(HttpRequest request, bool required)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hasLat = request.Query.ContainsKey("lat");
        var hasLng = request.Query.ContainsKey("lng");
        if (!required && !hasLat && !hasLng)
        {
            return null;
        }

        var errors = new List<ValidationErrorItem>();
        var lat = Coordinate(request, "lat", errors);
        var lng = Coordinate(request, "lng", errors);
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return (lat, lng);
    }

    private static double Coordinate(HttpRequest request, string name, List<ValidationErrorItem> errors)
    {
        if (!request.Query.TryGetValue(name, out var values) || values.Count == 0)
        {
            errors.Add(Item(name, "missing"));
            return double.NaN;
        }

        if (!double.TryParse(values[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            errors.Add(Item(name, "float_parsing"));
            return double.NaN;
        }

        return value;
    }

    private static RequestValidationException Error(string name, string type) => new([Item(name, type)]);

    private static ValidationErrorItem Item(string name, string type) =>
        new(type, ["query", name], type == "missing" ? "Este parámetro es obligatorio." : ValidationMessages.For(type));
}
