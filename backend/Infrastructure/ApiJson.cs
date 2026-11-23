using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace DecibelCity.Api.Infrastructure;

/// <summary>Opciones JSON del contrato HTTP: <c>snake_case</c>, números estrictos y fechas UTC con <c>Z</c>.</summary>
public static class ApiJson
{
    /// <summary>Aplica la configuración del contrato a unas opciones existentes.</summary>
    /// <param name="options">Opciones a modificar.</param>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.PropertyNameCaseInsensitive = false;
        options.AllowDuplicateProperties = false;
        // Los null se conservan ("decibels": null), igual que en FastAPI.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        // Texto en español legible (tildes sin \u); se siguen escapando caracteres HTML.
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
    }

    /// <summary>Instancia compartida para serializar fuera del pipeline de Minimal APIs.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// Serializa <see cref="DateTimeOffset"/> en UTC con sufijo <c>Z</c> y microsegundos, como Pydantic
/// (<c>2026-11-20T07:45:00.123456Z</c>; sin fracción cuando es cero).
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var utc = value.ToUniversalTime();
        var format = utc.Ticks % TimeSpan.TicksPerSecond / 10 == 0
            ? "yyyy-MM-dd'T'HH:mm:ss'Z'"
            : "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
        writer.WriteStringValue(utc.ToString(format, CultureInfo.InvariantCulture));
    }
}
