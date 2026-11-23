using System.Text;

namespace DecibelCity.Api.Models.Dtos;

/// <summary>Departamento disponible para el registro.</summary>
/// <param name="IdDepartamento">Identificador.</param>
/// <param name="Ubigeo">UBIGEO de dos dígitos.</param>
/// <param name="NombreDepartamento">Nombre.</param>
public sealed record Departamento(int IdDepartamento, string Ubigeo, string NombreDepartamento);

/// <summary>Provincia disponible para el registro.</summary>
/// <param name="IdProvincia">Identificador.</param>
/// <param name="IdDepartamento">Departamento al que pertenece.</param>
/// <param name="Ubigeo">UBIGEO de cuatro dígitos.</param>
/// <param name="NombreProvincia">Nombre.</param>
public sealed record Provincia(int IdProvincia, int IdDepartamento, string Ubigeo, string NombreProvincia);

/// <summary>Distrito disponible para el registro.</summary>
/// <param name="IdDistrito">Identificador.</param>
/// <param name="IdProvincia">Provincia a la que pertenece.</param>
/// <param name="Ubigeo">UBIGEO de seis dígitos.</param>
/// <param name="NombreDistrito">Nombre.</param>
public sealed record Distrito(int IdDistrito, int IdProvincia, string Ubigeo, string NombreDistrito);

/// <summary>Catálogo territorial: solo elementos activos ellos y toda su jerarquía superior.</summary>
/// <param name="Departamentos">Departamentos activos.</param>
/// <param name="Provincias">Provincias activas con departamento activo.</param>
/// <param name="Distritos">Distritos activos con provincia y departamento activos.</param>
public sealed record TerritoryCatalog(
    IReadOnlyList<Departamento> Departamentos,
    IReadOnlyList<Provincia> Provincias,
    IReadOnlyList<Distrito> Distritos);

/// <summary>Datos del registro de una cuenta (nueve campos).</summary>
public sealed record UserRegistration
{
    /// <summary>Nombres (2–80 caracteres, sin espacios extremos).</summary>
    public required string Nombres { get; init; }

    /// <summary>Apellidos (2–80 caracteres).</summary>
    public required string Apellidos { get; init; }

    /// <summary>Teléfono (<c>^[0-9+() -]{6,20}$</c>).</summary>
    public required string Telefono { get; init; }

    /// <summary>Dirección declarada (5–200 caracteres).</summary>
    public required string Direccion { get; init; }

    /// <summary>Departamento seleccionado (se valida, no se guarda).</summary>
    public required int IdDepartamento { get; init; }

    /// <summary>Provincia seleccionada (se valida, no se guarda).</summary>
    public required int IdProvincia { get; init; }

    /// <summary>Distrito seleccionado (único dato territorial guardado).</summary>
    public required int IdDistrito { get; init; }

    /// <summary>Nombre de usuario (<c>^[a-zA-Z0-9_.-]{3,40}$</c>).</summary>
    public required string NombreUsuario { get; init; }

    /// <summary>Contraseña (8–128 caracteres; se conserva exactamente como se escribió).</summary>
    public required string Contrasena { get; init; }

    // La contraseña nunca aparece en ToString ni en registros.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("NombreUsuario = ").Append(NombreUsuario).Append(", IdDistrito = ").Append(IdDistrito);
        return true;
    }
}

/// <summary>Credenciales de inicio de sesión.</summary>
public sealed record LoginCredentials
{
    /// <summary>Nombre de usuario (sin distinguir mayúsculas).</summary>
    public required string NombreUsuario { get; init; }

    /// <summary>Contraseña.</summary>
    public required string Contrasena { get; init; }

    /// <summary>Token de Cloudflare Turnstile; obligatorio solo si Turnstile está activo.</summary>
    public string? TurnstileToken { get; init; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("NombreUsuario = ").Append(NombreUsuario);
        return true;
    }
}

/// <summary>Datos públicos de la cuenta. No incluye el hash ni la contraseña.</summary>
/// <param name="IdUsuario">Identificador.</param>
/// <param name="NombreUsuario">Nombre de usuario.</param>
/// <param name="Nombres">Nombres.</param>
/// <param name="Apellidos">Apellidos.</param>
/// <param name="Telefono">Teléfono.</param>
/// <param name="Direccion">Dirección declarada.</param>
/// <param name="IdDistrito">Distrito.</param>
/// <param name="NombreDistrito">Nombre del distrito.</param>
/// <param name="IdProvincia">Provincia (derivada del distrito).</param>
/// <param name="NombreProvincia">Nombre de la provincia.</param>
/// <param name="IdDepartamento">Departamento (derivado de la provincia).</param>
/// <param name="NombreDepartamento">Nombre del departamento.</param>
/// <param name="Rol">Nombre del rol.</param>
/// <param name="FechaRegistro">Fecha de registro (UTC).</param>
public sealed record UserProfile(
    int IdUsuario,
    string NombreUsuario,
    string Nombres,
    string Apellidos,
    string Telefono,
    string Direccion,
    int IdDistrito,
    string NombreDistrito,
    int IdProvincia,
    string NombreProvincia,
    int IdDepartamento,
    string NombreDepartamento,
    string Rol,
    DateTimeOffset FechaRegistro);

/// <summary>Configuración pública de autenticación para el navegador.</summary>
/// <param name="TurnstileEnabled">Indica si el inicio de sesión exige Turnstile.</param>
/// <param name="TurnstileSiteKey">Clave pública del widget (vacía si está desactivado).</param>
public sealed record AuthConfigResponse(bool TurnstileEnabled, string TurnstileSiteKey);
