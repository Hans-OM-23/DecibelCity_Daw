using System.Text.Json;
using System.Text.RegularExpressions;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Validation;

/// <summary>
/// Lee el cuerpo JSON de una petición con un tamaño máximo. No usa el enlazado automático
/// para poder devolver errores 422 exactos y sanitizados (sin repetir valores ni claves recibidas).
/// </summary>
public static class JsonBody
{
    /// <summary>Tamaño máximo aceptado para un cuerpo JSON.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>Lee y analiza el cuerpo como JSON.</summary>
    /// <param name="request">Petición HTTP.</param>
    /// <param name="cancellationToken">Cancelación de la petición.</param>
    /// <returns>Elemento raíz (copia independiente del documento).</returns>
    /// <exception cref="RequestValidationException">Si el cuerpo falta, es demasiado grande o no es JSON.</exception>
    public static async Task<JsonElement> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength > MaxBytes)
        {
            throw Invalid("json_invalid");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw Invalid("json_invalid");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw Invalid("missing");
        }

        var bytes = buffer.ToArray();
        // JsonDocument no valida el UTF-8 de las cadenas hasta leerlas: se comprueba antes (422, nunca 500).
        if (!System.Text.Unicode.Utf8.IsValid(bytes))
        {
            throw Invalid("json_invalid");
        }

        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            EnsureDecodable(document.RootElement);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: escapes de sustitutos sueltos (p. ej. "\ud800") imposibles de decodificar.
            throw Invalid("json_invalid");
        }
    }

    private static void EnsureDecodable(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    _ = property.Name;
                    EnsureDecodable(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    EnsureDecodable(item);
                }

                break;
            case JsonValueKind.String:
                _ = element.GetString();
                break;
        }
    }

    private static RequestValidationException Invalid(string type) =>
        new([new ValidationErrorItem(type, ["body"], ValidationMessages.For(type))]);
}

/// <summary>Mensajes en español por tipo de error; nunca incluyen datos recibidos.</summary>
public static class ValidationMessages
{
    /// <summary>Devuelve el mensaje correspondiente al tipo de error.</summary>
    /// <param name="type">Tipo de error compatible con Pydantic.</param>
    /// <returns>Mensaje en español.</returns>
    public static string For(string type) => type switch
    {
        "missing" => "Este campo es obligatorio.",
        "extra_forbidden" => "El cuerpo contiene campos no permitidos.",
        "json_invalid" => "El cuerpo debe contener JSON válido.",
        "model_attributes_type" => "El cuerpo debe ser un objeto JSON.",
        _ => "El valor no cumple el formato o la longitud permitidos.",
    };
}

/// <summary>
/// Valida un objeto JSON campo a campo con semántica equivalente a Pydantic v2
/// (recorte de espacios, longitudes en caracteres Unicode, enteros estrictos y campos extra prohibidos).
/// </summary>
public sealed class JsonObjectValidator
{
    private readonly Dictionary<string, JsonElement> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly List<ValidationErrorItem> _errors = [];

    /// <summary>Prepara la validación del elemento raíz.</summary>
    /// <param name="root">Cuerpo recibido.</param>
    /// <exception cref="RequestValidationException">Si el cuerpo no es un objeto JSON.</exception>
    public JsonObjectValidator(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new RequestValidationException(
                [new ValidationErrorItem("model_attributes_type", ["body"], ValidationMessages.For("model_attributes_type"))]);
        }

        // Con claves duplicadas prevalece la última, como en el JSON de Python.
        foreach (var property in root.EnumerateObject())
        {
            _properties[property.Name] = property.Value;
        }
    }

    /// <summary>Cadena obligatoria u opcional con recorte, longitud y patrón.</summary>
    /// <param name="name">Nombre del campo.</param>
    /// <param name="minLength">Longitud mínima (tras recortar).</param>
    /// <param name="maxLength">Longitud máxima (tras recortar).</param>
    /// <param name="pattern">Patrón opcional.</param>
    /// <param name="trim">Recortar espacios extremos.</param>
    /// <param name="required">Si es obligatorio.</param>
    /// <returns>Valor válido o <c>null</c>.</returns>
    public string? Text(string name, int minLength, int maxLength, Regex? pattern = null, bool trim = true, bool required = true)
    {
        if (!TryGet(name, required, out var element))
        {
            return null;
        }

        if (!required && element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return Fail<string>(name, "string_type");
        }

        var value = element.GetString()!;
        if (trim)
        {
            value = value.Trim();
        }

        var length = TextLength(value);
        if (length < minLength)
        {
            return Fail<string>(name, "string_too_short");
        }

        if (length > maxLength)
        {
            return Fail<string>(name, "string_too_long");
        }

        if (pattern is not null && !pattern.IsMatch(value))
        {
            return Fail<string>(name, "string_pattern_mismatch");
        }

        return value;
    }

    /// <summary>Entero estricto (no acepta texto, booleanos ni decimales) mayor que cero.</summary>
    /// <param name="name">Nombre del campo.</param>
    /// <returns>Valor válido o <c>null</c>.</returns>
    public int? PositiveStrictInt(string name)
    {
        if (!TryGet(name, required: true, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            return Fail<int?>(name, "int_type");
        }

        if (value <= 0)
        {
            return Fail<int?>(name, "greater_than");
        }

        if (value > int.MaxValue)
        {
            return Fail<int?>(name, "less_than_equal");
        }

        return (int)value;
    }

    /// <summary>Número opcional estricto (acepta enteros y decimales, no texto ni booleanos) en un rango.</summary>
    /// <param name="name">Nombre del campo.</param>
    /// <param name="min">Mínimo inclusive.</param>
    /// <param name="max">Máximo inclusive.</param>
    /// <returns>Valor válido o <c>null</c> (ausente o <c>null</c>).</returns>
    public double? OptionalStrictNumber(string name, double min, double max)
    {
        if (!TryGet(name, required: false, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            return Fail<double?>(name, "float_type");
        }

        if (value < min)
        {
            return Fail<double?>(name, "greater_than_equal");
        }

        if (value > max)
        {
            return Fail<double?>(name, "less_than_equal");
        }

        return value;
    }

    /// <summary>Cadena que debe pertenecer a un conjunto de valores.</summary>
    /// <param name="name">Nombre del campo.</param>
    /// <param name="allowed">Valores permitidos.</param>
    /// <returns>Valor válido o <c>null</c>.</returns>
    public string? Enum(string name, IReadOnlyCollection<string> allowed)
    {
        if (!TryGet(name, required: true, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || !allowed.Contains(element.GetString()!))
        {
            return Fail<string>(name, "enum");
        }

        return element.GetString();
    }

    /// <summary>Registra un error de valor en un campo ya leído.</summary>
    /// <param name="name">Nombre del campo.</param>
    public void ValueError(string name) => _errors.Add(Item(name, "value_error"));

    /// <summary>Lanza la excepción de validación si hubo errores o campos no permitidos.</summary>
    /// <exception cref="RequestValidationException">Si la validación falló.</exception>
    public void ThrowIfInvalid()
    {
        // Las claves desconocidas pueden contener datos (incluso la contraseña): solo se informa ["body"].
        var extraCount = _properties.Keys.Count(key => !_known.Contains(key));
        for (var i = 0; i < extraCount; i++)
        {
            _errors.Add(new ValidationErrorItem("extra_forbidden", ["body"], ValidationMessages.For("extra_forbidden")));
        }

        if (_errors.Count > 0)
        {
            throw new RequestValidationException(_errors);
        }
    }

    /// <summary>Longitud en caracteres Unicode (code points), como <c>len()</c> de Python.</summary>
    /// <param name="value">Texto.</param>
    /// <returns>Número de code points.</returns>
    public static int TextLength(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private bool TryGet(string name, bool required, out JsonElement element)
    {
        _known.Add(name);
        if (_properties.TryGetValue(name, out element))
        {
            return true;
        }

        if (required)
        {
            _errors.Add(Item(name, "missing"));
        }

        return false;
    }

    private T? Fail<T>(string name, string type)
    {
        _errors.Add(Item(name, type));
        return default;
    }

    private static ValidationErrorItem Item(string name, string type) =>
        new(type, ["body", name], ValidationMessages.For(type));
}
