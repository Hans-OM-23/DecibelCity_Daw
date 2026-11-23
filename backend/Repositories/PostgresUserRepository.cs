using System.Data.Common;
using DecibelCity.Api.Models.Domain;
using DecibelCity.Api.Models.Dtos;
using Npgsql;

namespace DecibelCity.Api.Repositories;

/// <summary>
/// Cuentas y catálogo territorial (equivalente a <c>repositories/user_repository.py</c> del backend Python retirado).
/// Recibe siempre el hash ya calculado: nunca ve ni guarda la contraseña.
/// </summary>
/// <param name="dataSource">Origen de conexiones.</param>
public sealed class PostgresUserRepository(NpgsqlDataSource dataSource) : IUserRepository
{
    // Un distrito está disponible solo si él, su provincia y su departamento están activos.
    // FOR SHARE impide que otra transacción lo desactive o borre mientras se registra.
    private const string AvailableDistrict = """
        SELECT d.id_distrito FROM distritos d
        JOIN provincias p USING (id_provincia) JOIN departamentos a USING (id_departamento)
        WHERE d.id_distrito = @distrito AND p.id_provincia = @provincia AND a.id_departamento = @departamento
          AND d.activo AND p.activo AND a.activo
        FOR SHARE OF d, p, a
        """;

    private const string ProfileSelect = """
        SELECT u.id_usuario, u.nombre_usuario, u.nombres, u.apellidos, u.telefono, u.direccion,
               d.id_distrito, d.nombre_distrito, p.id_provincia, p.nombre_provincia,
               a.id_departamento, a.nombre_departamento, r.nombre_rol, u.fecha_registro,
               u.activo, r.activo
        FROM usuarios u
        JOIN roles_usuario r USING (id_rol)
        JOIN distritos d USING (id_distrito)
        JOIN provincias p ON p.id_provincia = d.id_provincia
        JOIN departamentos a ON a.id_departamento = p.id_departamento
        WHERE u.id_usuario = @id
        """;

    private const string UniqueViolation = "23505";

    /// <inheritdoc />
    public async Task<TerritoryCatalog> ListTerritoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // Lecturas coherentes entre sí dentro de una sola instantánea.
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken)
            .ConfigureAwait(false);

        var departamentos = await ReadAllAsync(connection, transaction, """
            SELECT id_departamento, ubigeo, nombre_departamento FROM departamentos
            WHERE activo ORDER BY nombre_departamento, id_departamento
            """, r => new Departamento(r.GetInt32(0), r.GetString(1), r.GetString(2)), cancellationToken).ConfigureAwait(false);

        var provincias = await ReadAllAsync(connection, transaction, """
            SELECT p.id_provincia, p.id_departamento, p.ubigeo, p.nombre_provincia
            FROM provincias p JOIN departamentos a USING (id_departamento)
            WHERE p.activo AND a.activo ORDER BY p.nombre_provincia, p.id_provincia
            """, r => new Provincia(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3)), cancellationToken).ConfigureAwait(false);

        var distritos = await ReadAllAsync(connection, transaction, """
            SELECT d.id_distrito, d.id_provincia, d.ubigeo, d.nombre_distrito
            FROM distritos d JOIN provincias p USING (id_provincia) JOIN departamentos a USING (id_departamento)
            WHERE d.activo AND p.activo AND a.activo ORDER BY d.nombre_distrito, d.id_distrito
            """, r => new Distrito(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3)), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new TerritoryCatalog(departamentos, provincias, distritos);
    }

    /// <inheritdoc />
    public async Task<CreateUserResult> CreateUserAsync(
        UserRegistration registration, string passwordHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var district = new NpgsqlCommand(AvailableDistrict, connection, transaction))
        {
            district.Parameters.AddWithValue("distrito", registration.IdDistrito);
            district.Parameters.AddWithValue("provincia", registration.IdProvincia);
            district.Parameters.AddWithValue("departamento", registration.IdDepartamento);
            if (await district.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                return CreateUserResult.Failed(CreateUserStatus.DistrictUnavailable);
            }
        }

        int roleId;
        await using (var role = new NpgsqlCommand(
            "SELECT id_rol FROM roles_usuario WHERE nombre_rol = @rol AND activo FOR SHARE", connection, transaction))
        {
            role.Parameters.AddWithValue("rol", JuninCatalog.DefaultRole);
            if (await role.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not int id)
            {
                return CreateUserResult.Failed(CreateUserStatus.DefaultRoleUnavailable);
            }

            roleId = id;
        }

        int userId;
        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO usuarios (id_rol, id_distrito, nombres, apellidos, telefono, direccion, nombre_usuario, password_hash)
                VALUES (@rol, @distrito, @nombres, @apellidos, @telefono, @direccion, @usuario, @hash)
                RETURNING id_usuario
                """, connection, transaction);
            insert.Parameters.AddWithValue("rol", roleId);
            insert.Parameters.AddWithValue("distrito", registration.IdDistrito);
            insert.Parameters.AddWithValue("nombres", registration.Nombres);
            insert.Parameters.AddWithValue("apellidos", registration.Apellidos);
            insert.Parameters.AddWithValue("telefono", registration.Telefono);
            insert.Parameters.AddWithValue("direccion", registration.Direccion);
            insert.Parameters.AddWithValue("usuario", registration.NombreUsuario);
            insert.Parameters.AddWithValue("hash", passwordHash);
            userId = (int)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            // La transacción queda abortada; se revierte al desecharla.
            return CreateUserResult.Failed(CreateUserStatus.UsernameTaken);
        }

        var profile = await ReadProfileAsync(connection, transaction, userId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CreateUserResult(CreateUserStatus.Created, profile!.Profile);
    }

    /// <inheritdoc />
    public async Task<StoredCredentials?> FindCredentialsAsync(string nombreUsuario, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT id_usuario, password_hash, activo FROM usuarios WHERE lower(nombre_usuario) = lower(@usuario)");
        command.Parameters.AddWithValue("usuario", nombreUsuario);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredCredentials(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2))
            : null;
    }

    /// <inheritdoc />
    public async Task UpdatePasswordHashAsync(int idUsuario, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("UPDATE usuarios SET password_hash = @hash WHERE id_usuario = @id");
        command.Parameters.AddWithValue("hash", passwordHash);
        command.Parameters.AddWithValue("id", idUsuario);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProfileWithStatus?> GetProfileAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadProfileAsync(connection, null, idUsuario, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProfileWithStatus?> ReadProfileAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int idUsuario, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(ProfileSelect, connection, transaction);
        command.Parameters.AddWithValue("id", idUsuario);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var profile = new UserProfile(
            IdUsuario: reader.GetInt32(0),
            NombreUsuario: reader.GetString(1),
            Nombres: reader.GetString(2),
            Apellidos: reader.GetString(3),
            Telefono: reader.GetString(4),
            Direccion: reader.GetString(5),
            IdDistrito: reader.GetInt32(6),
            NombreDistrito: reader.GetString(7),
            IdProvincia: reader.GetInt32(8),
            NombreProvincia: reader.GetString(9),
            IdDepartamento: reader.GetInt32(10),
            NombreDepartamento: reader.GetString(11),
            Rol: reader.GetString(12),
            FechaRegistro: reader.GetFieldValue<DateTimeOffset>(13));
        return new ProfileWithStatus(profile, reader.GetBoolean(14), reader.GetBoolean(15));
    }

    private static async Task<List<T>> ReadAllAsync<T>(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Func<DbDataReader, T> map, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
