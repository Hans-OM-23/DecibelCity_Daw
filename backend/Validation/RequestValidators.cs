using System.Text.Json;
using System.Text.RegularExpressions;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Services;

namespace DecibelCity.Api.Validation;

/// <summary>
/// Validadores de los cuerpos de entrada. Replican las restricciones de <c>schemas</c> del backend Python retirado
/// y los CHECK de PostgreSQL para responder 422 antes de llegar a la base.
/// </summary>
public static partial class RequestValidators
{
    /// <summary>Longitud mínima de la contraseña en el registro.</summary>
    public const int PasswordMinLength = 8;

    /// <summary>Límite superior para acotar el coste de Argon2 ante entradas desmesuradas.</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Longitud máxima del token de Turnstile (documentada por Cloudflare).</summary>
    public const int TurnstileTokenMaxLength = 2048;

    /// <summary>Valida el registro (nueve campos, sin extras).</summary>
    /// <param name="root">Cuerpo JSON.</param>
    /// <returns>Registro válido.</returns>
    public static UserRegistration Registration(JsonElement root)
    {
        var body = new JsonObjectValidator(root);
        var nombres = body.Text("nombres", 2, 80);
        var apellidos = body.Text("apellidos", 2, 80);
        var telefono = body.Text("telefono", 0, int.MaxValue, PhonePattern());
        var direccion = body.Text("direccion", 5, 200);
        var idDepartamento = body.PositiveStrictInt("id_departamento");
        var idProvincia = body.PositiveStrictInt("id_provincia");
        var idDistrito = body.PositiveStrictInt("id_distrito");
        var nombreUsuario = body.Text("nombre_usuario", 0, int.MaxValue, UsernamePattern());
        // Sin recorte: la contraseña se conserva exactamente como se escribió.
        var contrasena = body.Text("contrasena", PasswordMinLength, PasswordMaxLength, trim: false);
        if (contrasena is not null && string.IsNullOrWhiteSpace(contrasena))
        {
            body.ValueError("contrasena");
        }

        body.ThrowIfInvalid();
        return new UserRegistration
        {
            Nombres = nombres!,
            Apellidos = apellidos!,
            Telefono = telefono!,
            Direccion = direccion!,
            IdDepartamento = idDepartamento!.Value,
            IdProvincia = idProvincia!.Value,
            IdDistrito = idDistrito!.Value,
            NombreUsuario = nombreUsuario!,
            Contrasena = contrasena!,
        };
    }

    /// <summary>Valida las credenciales (y el token opcional de Turnstile).</summary>
    /// <param name="root">Cuerpo JSON.</param>
    /// <returns>Credenciales válidas.</returns>
    public static LoginCredentials Login(JsonElement root)
    {
        var body = new JsonObjectValidator(root);
        var nombreUsuario = body.Text("nombre_usuario", 1, 40);
        var contrasena = body.Text("contrasena", 1, PasswordMaxLength, trim: false);
        var token = body.Text("turnstile_token", 0, TurnstileTokenMaxLength, trim: false, required: false);
        body.ThrowIfInvalid();
        return new LoginCredentials { NombreUsuario = nombreUsuario!, Contrasena = contrasena!, TurnstileToken = token };
    }

    /// <summary>Valida el formulario de reporte (sin extras ni coordenadas libres).</summary>
    /// <param name="root">Cuerpo JSON.</param>
    /// <returns>Reporte válido.</returns>
    public static NoiseReportCreate Report(JsonElement root)
    {
        var body = new JsonObjectValidator(root);
        var location = body.Text("location", 5, 1000);
        var placeId = body.Text("place_id", 2, 20, OsmPlaceId.Pattern);
        var source = body.Enum("source", NoiseCatalog.SourceCodes);
        var description = body.Text("description", 10, 500);
        var decibels = body.OptionalStrictNumber("decibels", 30, 120);
        body.ThrowIfInvalid();
        return new NoiseReportCreate
        {
            Location = location!,
            PlaceId = placeId!,
            Source = source!,
            Description = description!,
            Decibels = decibels,
        };
    }

    [GeneratedRegex(@"\A[0-9+() -]{6,20}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\A[a-zA-Z0-9_.-]{3,40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}

/// <summary>Identificador OpenStreetMap compacto: N (nodo), W (vía) o R (relación) y su número.</summary>
public static partial class OsmPlaceId
{
    /// <summary>Patrón <c>^[NWR][0-9]{1,19}$</c>.</summary>
    public static Regex Pattern => PlaceIdPattern();

    /// <summary>Indica si el texto es un identificador OSM válido.</summary>
    /// <param name="value">Texto a comprobar.</param>
    /// <returns><c>true</c> si coincide exactamente.</returns>
    public static bool IsValid(string? value) => value is not null && PlaceIdPattern().IsMatch(value);

    [GeneratedRegex(@"\A[NWR][0-9]{1,19}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceIdPattern();
}
