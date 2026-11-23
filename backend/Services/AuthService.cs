using DecibelCity.Api.Errors;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Repositories;
using DecibelCity.Api.Services.Security;

namespace DecibelCity.Api.Services;

/// <summary>Registro, inicio de sesión, perfiles y catálogo territorial.</summary>
public interface IAuthService
{
    /// <summary>Catálogo territorial disponible.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Catálogo con jerarquía activa.</returns>
    Task<TerritoryCatalog> ListTerritoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Registra la cuenta con el rol base.</summary>
    /// <param name="registration">Datos validados.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Perfil creado.</returns>
    /// <exception cref="AuthException">422 distrito, 409 duplicado o 503 sin rol base.</exception>
    Task<UserProfile> RegisterAsync(UserRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Comprueba credenciales (y hace rehash si los parámetros quedaron desfasados).</summary>
    /// <param name="credentials">Credenciales validadas.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Perfil de la cuenta.</returns>
    /// <exception cref="AuthException">401 credenciales o 403 cuenta desactivada.</exception>
    Task<UserProfile> LoginAsync(LoginCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Relee la cuenta y su rol en cada operación protegida.</summary>
    /// <param name="idUsuario">Cuenta de la sesión.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Perfil activo.</returns>
    /// <exception cref="AuthException">401 si no existe, 403 si cuenta o rol están desactivados.</exception>
    Task<UserProfile> GetActiveProfileAsync(int idUsuario, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación equivalente a <c>services/auth_service.py</c> del backend Python retirado. La contraseña en texto plano
/// solo existe durante el cálculo o la verificación del hash.
/// </summary>
/// <param name="repository">Cuentas.</param>
/// <param name="passwordHasher">Argon2id.</param>
/// <param name="dummyHash">Hash de referencia para igualar el tiempo cuando el usuario no existe.</param>
public sealed class AuthService(IUserRepository repository, IPasswordHasher passwordHasher, DummyPasswordHash dummyHash) : IAuthService
{
    /// <inheritdoc />
    public Task<TerritoryCatalog> ListTerritoriesAsync(CancellationToken cancellationToken = default) =>
        repository.ListTerritoriesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<UserProfile> RegisterAsync(UserRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var hash = await Task.Run(() => passwordHasher.Hash(registration.Contrasena), cancellationToken).ConfigureAwait(false);
        var result = await repository.CreateUserAsync(registration, hash, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            CreateUserStatus.Created => result.Profile!,
            CreateUserStatus.DistrictUnavailable => throw new AuthException(StatusCodes.Status422UnprocessableEntity, ErrorMessages.DistrictUnavailable),
            CreateUserStatus.UsernameTaken => throw new AuthException(StatusCodes.Status409Conflict, ErrorMessages.UsernameTaken),
            CreateUserStatus.DefaultRoleUnavailable => throw new AuthException(StatusCodes.Status503ServiceUnavailable, ErrorMessages.RegistrationUnavailable),
            _ => throw new InvalidOperationException("Resultado de registro no reconocido."),
        };
    }

    /// <inheritdoc />
    public async Task<UserProfile> LoginAsync(LoginCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var password = credentials.Contrasena;
        var stored = await repository.FindCredentialsAsync(credentials.NombreUsuario, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            // Mismo coste que una cuenta existente: no revela qué nombres están registrados.
            await Task.Run(() => passwordHasher.Verify(dummyHash.Value, password), cancellationToken).ConfigureAwait(false);
            throw new AuthException(StatusCodes.Status401Unauthorized, ErrorMessages.InvalidCredentials);
        }

        var valid = await Task.Run(() => passwordHasher.Verify(stored.PasswordHash, password), cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, ErrorMessages.InvalidCredentials);
        }

        // El estado se revela solo a quien demostró conocer la contraseña.
        if (!stored.Activo)
        {
            throw new AuthException(StatusCodes.Status403Forbidden, ErrorMessages.AccountDisabled);
        }

        if (passwordHasher.NeedsRehash(stored.PasswordHash))
        {
            var rehashed = await Task.Run(() => passwordHasher.Hash(password), cancellationToken).ConfigureAwait(false);
            await repository.UpdatePasswordHashAsync(stored.IdUsuario, rehashed, cancellationToken).ConfigureAwait(false);
        }

        var profile = await repository.GetProfileAsync(stored.IdUsuario, cancellationToken).ConfigureAwait(false);
        // Borrada entre la verificación y la lectura del perfil.
        return profile?.Profile ?? throw new AuthException(StatusCodes.Status401Unauthorized, ErrorMessages.InvalidCredentials);
    }

    /// <inheritdoc />
    public async Task<UserProfile> GetActiveProfileAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        var current = await repository.GetProfileAsync(idUsuario, cancellationToken).ConfigureAwait(false)
                      ?? throw new AuthException(StatusCodes.Status401Unauthorized, ErrorMessages.InvalidSession);
        if (!current.UsuarioActivo || !current.RolActivo)
        {
            throw new AuthException(StatusCodes.Status403Forbidden, ErrorMessages.AccountOrRoleDisabled);
        }

        return current.Profile;
    }
}

/// <summary>Hash de referencia calculado una sola vez (perezosamente) con los parámetros actuales.</summary>
/// <param name="passwordHasher">Hasher configurado.</param>
public sealed class DummyPasswordHash(IPasswordHasher passwordHasher)
{
    private readonly Lazy<string> _value = new(() => passwordHasher.Hash("decibelcity-usuario-inexistente"));

    /// <summary>Hash de una contraseña que nunca se usa.</summary>
    public string Value => _value.Value;
}
