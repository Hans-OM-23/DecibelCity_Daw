using DecibelCity.Api.Models.Domain;
using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Repositories;

/// <summary>Crea o verifica el esquema v5 y siembra los catálogos (idempotente).</summary>
public interface IDatabaseInitializer
{
    /// <summary>Prepara la base en una sola transacción serializada por base y esquema.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Tarea asíncrona.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persistencia de puntos críticos, fuentes y reportes.</summary>
public interface INoiseRepository
{
    /// <summary>Lista los puntos críticos ilustrativos.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Puntos ordenados por identificador.</returns>
    Task<IReadOnlyList<Hotspot>> ListHotspotsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lista el catálogo de fuentes de ruido.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Fuentes en orden de siembra.</returns>
    Task<IReadOnlyList<NoiseSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Guarda el reporte sin relación con la cuenta (solo <c>id_lugar</c> y <c>proveedor_lugar='osm'</c>).</summary>
    /// <param name="report">Reporte verificado.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Tarea asíncrona.</returns>
    Task SaveReportAsync(NoiseReport report, CancellationToken cancellationToken = default);
}

/// <summary>Resultado del alta de una cuenta.</summary>
public enum CreateUserStatus
{
    /// <summary>Cuenta creada.</summary>
    Created,

    /// <summary>El distrito no existe, no pertenece a la provincia/departamento o algún nivel está inactivo.</summary>
    DistrictUnavailable,

    /// <summary>Nombre de usuario ya registrado (sin distinguir mayúsculas).</summary>
    UsernameTaken,

    /// <summary>No existe un rol base activo.</summary>
    DefaultRoleUnavailable,
}

/// <summary>Resultado del alta: estado y perfil si se creó.</summary>
/// <param name="Status">Estado.</param>
/// <param name="Profile">Perfil creado o <c>null</c>.</param>
public sealed record CreateUserResult(CreateUserStatus Status, UserProfile? Profile)
{
    /// <summary>Resultado fallido sin perfil.</summary>
    /// <param name="status">Motivo.</param>
    /// <returns>Resultado.</returns>
    public static CreateUserResult Failed(CreateUserStatus status) => new(status, null);
}

/// <summary>Cuentas de usuario y catálogo territorial. Nunca recibe la contraseña, solo el hash.</summary>
public interface IUserRepository
{
    /// <summary>Catálogo territorial con jerarquía activa completa.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Catálogo.</returns>
    Task<TerritoryCatalog> ListTerritoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserta la cuenta con el rol base en una sola transacción.</summary>
    /// <param name="registration">Datos validados.</param>
    /// <param name="passwordHash">Hash Argon2id.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Resultado del alta.</returns>
    Task<CreateUserResult> CreateUserAsync(UserRegistration registration, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>Busca credenciales sin distinguir mayúsculas.</summary>
    /// <param name="nombreUsuario">Nombre de usuario.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Credenciales o <c>null</c>.</returns>
    Task<StoredCredentials?> FindCredentialsAsync(string nombreUsuario, CancellationToken cancellationToken = default);

    /// <summary>Sustituye el hash (rehash con parámetros actuales).</summary>
    /// <param name="idUsuario">Cuenta.</param>
    /// <param name="passwordHash">Hash nuevo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Tarea asíncrona.</returns>
    Task UpdatePasswordHashAsync(int idUsuario, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>Lee el perfil y el estado actual de cuenta y rol.</summary>
    /// <param name="idUsuario">Cuenta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Perfil con estado o <c>null</c> si la cuenta no existe.</returns>
    Task<ProfileWithStatus?> GetProfileAsync(int idUsuario, CancellationToken cancellationToken = default);
}
