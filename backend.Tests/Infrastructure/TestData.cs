using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DecibelCity.Api.Services.Security;

namespace DecibelCity.Api.Tests.Infrastructure;

/// <summary>Datos y respuestas simuladas compartidas por las pruebas.</summary>
public static class TestData
{
    /// <summary>User-Agent de pruebas.</summary>
    public const string UserAgent = "DecibelCity-tests/1.0";

    /// <summary>Contraseña de las cuentas de prueba (no debe aparecer nunca en respuestas).</summary>
    public const string Password = "Ruido seguro 2026!";

    /// <summary>Token de Turnstile aceptado por el doble.</summary>
    public const string ValidTurnstileToken = "token-turnstile-valido";

    /// <summary>
    /// Hashes reales generados con <c>argon2-cffi 25.1.0</c> (<c>PasswordHasher().hash(...)</c>)
    /// para la contraseña <see cref="Password"/>.
    /// </summary>
    public const string ArgonCffiDefaultHash =
        "$argon2id$v=19$m=65536,t=3,p=4$31KWXXrKM3XAj9YuxMR4Dg$e8/it5zJhubtw/q+InXmdypU5JP0SxGYv1J4occyq0o";

    /// <summary>Hash de <c>PasswordHasher(time_cost=1, memory_cost=8192, parallelism=1)</c> para <see cref="Password"/>.</summary>
    public const string ArgonCffiWeakHash =
        "$argon2id$v=19$m=8192,t=1,p=1$DfLyJ9u2xqhw8IJddPcwdw$FQZrJvuiY1plneP//rpnam7jxGrDif9m4q/oRZFLlig";

    /// <summary>Contraseña Unicode con espacios extremos.</summary>
    public const string UnicodePassword = "  Contraseña ñandú 2026!  ";

    /// <summary>Hash de argon2-cffi para <see cref="UnicodePassword"/>.</summary>
    public const string ArgonCffiUnicodeHash =
        "$argon2id$v=19$m=65536,t=3,p=4$uYX4JgokLk9eWzlj3z12+Q$N06zdYCzcoro2B1MKMUx11/evj92xa3apBykg/4uNE8";

    /// <summary>Parámetros rápidos para pruebas HTTP (no se usan en producción).</summary>
    public static Argon2Parameters FastArgon2 { get; } = new(1, 1024, 1, 32, 16);

    /// <summary>Registro válido para el distrito 1 (Huancayo) del repositorio en memoria.</summary>
    /// <param name="username">Nombre de usuario.</param>
    /// <returns>Objeto JSON mutable.</returns>
    public static JsonObject Registration(string username = "ana.quispe") => new()
    {
        ["nombres"] = "  Ana María ",
        ["apellidos"] = "Quispe Rojas",
        ["telefono"] = "+51 964 123 456",
        ["direccion"] = "Jr. Ica 123, Huancayo",
        ["id_departamento"] = 1,
        ["id_provincia"] = 1,
        ["id_distrito"] = 1,
        ["nombre_usuario"] = username,
        ["contrasena"] = Password,
    };

    /// <summary>Credenciales.</summary>
    /// <param name="username">Usuario.</param>
    /// <param name="password">Contraseña.</param>
    /// <returns>Objeto JSON.</returns>
    public static JsonObject Credentials(string username = "ana.quispe", string password = Password) => new()
    {
        ["nombre_usuario"] = username,
        ["contrasena"] = password,
    };

    /// <summary>Reporte válido.</summary>
    /// <returns>Objeto JSON.</returns>
    public static JsonObject Report() => new()
    {
        ["location"] = "Calle Real y Av. Giráldez",
        ["place_id"] = "W813046785",
        ["source"] = "traffic",
        ["description"] = "Bocinas constantes junto al paradero durante la tarde.",
        ["decibels"] = 74,
    };

    /// <summary>Respuesta de Nominatim (jsonv2) para una vía de Huancayo.</summary>
    /// <returns>Lugar.</returns>
    public static JsonObject OsmPlace() => new()
    {
        ["osm_type"] = "way",
        ["osm_id"] = 813046785,
        ["lat"] = "-12.0664834",
        ["lon"] = "-75.2062612",
        ["category"] = "highway",
        ["type"] = "tertiary",
        ["place_rank"] = 26,
        ["name"] = "Avenida Giraldez",
        ["display_name"] = "Avenida Giraldez, San Carlos, Huancayo, Junín, 12001, Perú",
        ["address"] = new JsonObject
        {
            ["road"] = "Avenida Giraldez",
            ["suburb"] = "San Carlos",
            ["city"] = "Huancayo",
            ["country"] = "Perú",
            ["country_code"] = "pe",
        },
    };

    /// <summary>Nominatim: /lookup devuelve [lugar] y /reverse devuelve el lugar.</summary>
    /// <returns>Doble HTTP.</returns>
    public static StubHttpHandler NominatimStub()
    {
        var stub = new StubHttpHandler();
        stub.Responder = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/lookup" => StubHttpHandler.Json(new JsonArray(OsmPlace())),
            "/reverse" => StubHttpHandler.Json(OsmPlace()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        return stub;
    }

    /// <summary>Photon con un resultado válido, uno genérico y uno fuera de Perú.</summary>
    /// <returns>Doble HTTP.</returns>
    public static StubHttpHandler PhotonStub()
    {
        var stub = new StubHttpHandler();
        stub.RespondJson(new JsonObject
        {
            ["features"] = new JsonArray(
                Feature("W", 813046785, "Avenida Giraldez", -12.0664834, -75.2062612, "highway", "tertiary", "PE"),
                Feature("R", 1944670, "Huancayo", -12.06, -75.21, "place", "city", "PE"),
                Feature("N", 1, "Lima", -12.04, -77.03, "place", "city", "PE"),
                Feature("N", 2, "Otro país", -12.06, -75.21, "amenity", "cafe", "BO")),
        });
        return stub;
    }

    /// <summary>Turnstile: éxito solo con <see cref="ValidTurnstileToken"/>.</summary>
    /// <returns>Doble HTTP.</returns>
    public static StubHttpHandler TurnstileStub()
    {
        var stub = new StubHttpHandler();
        stub.Responder = async (request, ct) =>
        {
            var form = await request.Content!.ReadAsStringAsync(ct);
            var success = form.Contains("response=" + ValidTurnstileToken, StringComparison.Ordinal);
            return StubHttpHandler.Json(success
                ? new JsonObject { ["success"] = true }
                : new JsonObject { ["success"] = false, ["error-codes"] = new JsonArray("invalid-input-response") });
        };
        return stub;
    }

    /// <summary>Telegram responde <c>{"ok": true}</c>.</summary>
    /// <returns>Doble HTTP.</returns>
    public static StubHttpHandler TelegramStub()
    {
        var stub = new StubHttpHandler();
        stub.RespondJson(new JsonObject { ["ok"] = true });
        return stub;
    }

    /// <summary>Open-Meteo con condiciones actuales (código WMO 2).</summary>
    /// <returns>Doble HTTP.</returns>
    public static StubHttpHandler OpenMeteoStub()
    {
        var stub = new StubHttpHandler();
        stub.RespondJson(new JsonObject
        {
            ["latitude"] = -12.07,
            ["longitude"] = -75.21,
            ["current"] = new JsonObject
            {
                ["time"] = "2026-11-20T07:45",
                ["interval"] = 900,
                ["temperature_2m"] = 16.4,
                ["relative_humidity_2m"] = 68,
                ["apparent_temperature"] = 15.8,
                ["precipitation"] = 0.0,
                ["weather_code"] = 2,
                ["wind_speed_10m"] = 12.5,
            },
        });
        return stub;
    }

    /// <summary>Envía JSON por POST.</summary>
    /// <param name="client">Cliente.</param>
    /// <param name="path">Ruta.</param>
    /// <param name="body">Cuerpo.</param>
    /// <param name="origin">Cabecera Origin opcional.</param>
    /// <returns>Respuesta.</returns>
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, JsonNode? body, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return client.SendAsync(request);
    }

    /// <summary>Lee la respuesta como JSON.</summary>
    /// <param name="response">Respuesta.</param>
    /// <returns>Elemento raíz.</returns>
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Registra una cuenta y deja la cookie en el cliente.</summary>
    /// <param name="client">Cliente con cookies.</param>
    /// <param name="username">Usuario.</param>
    /// <returns>Perfil.</returns>
    public static async Task<JsonElement> RegisterAsync(this HttpClient client, string username = "ana.quispe")
    {
        var response = await client.PostJsonAsync("/api/auth/register", Registration(username));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.JsonAsync();
    }

    private static JsonObject Feature(string type, long id, string name, double lat, double lng, string key, string value, string country) => new()
    {
        ["type"] = "Feature",
        ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(lng, lat) },
        ["properties"] = new JsonObject
        {
            ["osm_type"] = type,
            ["osm_id"] = id,
            ["osm_key"] = key,
            ["osm_value"] = value,
            ["name"] = name,
            ["countrycode"] = country,
            ["district"] = "San Carlos",
            ["city"] = "Huancayo",
        },
    };
}
