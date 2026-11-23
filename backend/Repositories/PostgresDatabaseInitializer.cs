using System.Reflection;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace DecibelCity.Api.Repositories;

/// <summary>
/// Inicialización transaccional equivalente a <c>repositories/migrations.py</c> del backend Python retirado:
/// crea el esquema v5 en un esquema vacío, actualiza v3/v4 en el sitio y siembra los catálogos
/// con <c>ON CONFLICT DO NOTHING</c> (no sobrescribe IDs, nombres personalizados ni <c>activo</c>).
/// </summary>
/// <param name="dataSource">Origen de conexiones del esquema configurado.</param>
public sealed class PostgresDatabaseInitializer(NpgsqlDataSource dataSource) : IDatabaseInitializer
{
    /// <summary>Versión del esquema que entiende este backend.</summary>
    public const int SchemaVersion = 5;

    /// <summary>Las once tablas de dominio de la versión 5.</summary>
    public static IReadOnlySet<string> DomainTables { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "departamentos", "provincias", "distritos", "zonificaciones", "categorias_fuente_ruido", "roles_usuario",
        "tipos_fuente_ruido", "estados_reporte", "reportes_ciudadanos", "puntos_criticos", "usuarios",
    };

    private const string VersionTable = "decibelcity_schema";

    private static readonly Dictionary<int, string> Upgrades = new()
    {
        [3] = "upgrade_v4.sql",
        [4] = "upgrade_v5.sql",
    };

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await PrepareSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await SeedReferencesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await SeedSourcesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await SeedHotspotsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PrepareSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        // La cerradura dura hasta COMMIT/ROLLBACK y serializa arranques concurrentes.
        await ExecuteAsync(connection, transaction,
            "SELECT pg_advisory_xact_lock(hashtext(current_database()), hashtext(current_schema()))", ct).ConfigureAwait(false);

        var tables = await CurrentTablesAsync(connection, transaction, ct).ConfigureAwait(false);
        var v5Tables = new HashSet<string>(DomainTables, StringComparer.Ordinal) { VersionTable };
        var legacyTables = new HashSet<string>(DomainTables.Where(t => t != "usuarios"), StringComparer.Ordinal) { VersionTable };

        if (tables.Count == 0)
        {
            await ExecuteAsync(connection, transaction, ReadSql("schema.sql"), ct).ConfigureAwait(false);
        }
        else if (!tables.SetEquals(v5Tables) && !tables.SetEquals(legacyTables))
        {
            throw new DatabaseMigrationException("El esquema contiene tablas no reconocidas; no se modificará.");
        }

        while (true)
        {
            var versions = new List<int>();
            await using (var command = new NpgsqlCommand($"SELECT version FROM {VersionTable}", connection, transaction))
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    versions.Add(reader.GetInt32(0));
                }
            }

            if (versions is [SchemaVersion])
            {
                // Una base marcada como actual sin las tablas de la versión no es coherente.
                if (!(await CurrentTablesAsync(connection, transaction, ct).ConfigureAwait(false)).SetEquals(v5Tables))
                {
                    throw new DatabaseMigrationException("La estructura no coincide con la versión registrada; no se modificará.");
                }

                return;
            }

            if (versions is not [var version] || !Upgrades.TryGetValue(version, out var script))
            {
                throw new DatabaseMigrationException("La versión de la base de datos no es compatible con este backend.");
            }

            // Misma transacción que el resto del arranque: un fallo revierte la actualización completa.
            await ExecuteAsync(connection, transaction, ReadSql(script), ct).ConfigureAwait(false);
        }
    }

    private static async Task SeedReferencesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using (var command = new NpgsqlCommand(
            "INSERT INTO departamentos(ubigeo, nombre_departamento) VALUES (@ubigeo, 'Junín') ON CONFLICT (ubigeo) DO NOTHING",
            connection, transaction))
        {
            command.Parameters.AddWithValue("ubigeo", JuninCatalog.DepartmentUbigeo);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Los IDs dependen de la base existente: las relaciones se resuelven por UBIGEO.
        // WITH ORDINALITY + ORDER BY conserva el orden de inserción (IDs nuevos en el orden del catálogo).
        await using (var command = new NpgsqlCommand("""
            INSERT INTO provincias(id_departamento, ubigeo, nombre_provincia)
            SELECT (SELECT id_departamento FROM departamentos WHERE ubigeo = @departamento), p.ubigeo, p.nombre
            FROM unnest(@ubigeos, @nombres) WITH ORDINALITY AS p(ubigeo, nombre, orden)
            ORDER BY p.orden
            ON CONFLICT (ubigeo) DO NOTHING
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("departamento", JuninCatalog.DepartmentUbigeo);
            command.Parameters.AddWithValue("ubigeos", JuninCatalog.Provinces.Select(p => p.Ubigeo).ToArray());
            command.Parameters.AddWithValue("nombres", JuninCatalog.Provinces.Select(p => p.Name).ToArray());
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand("""
            INSERT INTO distritos(id_provincia, ubigeo, nombre_distrito)
            SELECT (SELECT id_provincia FROM provincias WHERE ubigeo = left(d.ubigeo, 4)), d.ubigeo, d.nombre
            FROM unnest(@ubigeos, @nombres) WITH ORDINALITY AS d(ubigeo, nombre, orden)
            ORDER BY d.orden
            ON CONFLICT (ubigeo) DO NOTHING
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("ubigeos", JuninCatalog.Districts.Select(d => d.Ubigeo).ToArray());
            command.Parameters.AddWithValue("nombres", JuninCatalog.Districts.Select(d => d.Name).ToArray());
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertCodedAsync(connection, transaction, "categorias_fuente_ruido", JuninCatalog.Categories, ct).ConfigureAwait(false);
        await InsertCodedAsync(connection, transaction, "zonificaciones", JuninCatalog.Zonifications, ct).ConfigureAwait(false);

        // Si el rol se desactiva o cambia su descripción, el arranque lo respeta.
        await using (var command = new NpgsqlCommand("""
            INSERT INTO roles_usuario(nombre_rol, descripcion, activo)
            VALUES (@rol, 'Persona registrada que puede enviar reportes ciudadanos', TRUE)
            ON CONFLICT (nombre_rol) DO NOTHING
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("rol", JuninCatalog.DefaultRole);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task InsertCodedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        IReadOnlyList<(string Code, string Name, string Description)> rows,
        CancellationToken ct)
    {
        // Nombre de tabla constante del código, nunca de una entrada externa.
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {table}(codigo, nombre, descripcion)
            SELECT c.codigo, c.nombre, c.descripcion
            FROM unnest(@codigos, @nombres, @descripciones) WITH ORDINALITY AS c(codigo, nombre, descripcion, orden)
            ORDER BY c.orden
            ON CONFLICT (codigo) DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue("codigos", rows.Select(r => r.Code).ToArray());
        command.Parameters.AddWithValue("nombres", rows.Select(r => r.Name).ToArray());
        command.Parameters.AddWithValue("descripciones", rows.Select(r => r.Description).ToArray());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedSourcesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using (var command = new NpgsqlCommand("""
            INSERT INTO tipos_fuente_ruido(codigo, nombre_fuente, id_categoria)
            SELECT s.codigo, s.nombre, (SELECT id_categoria FROM categorias_fuente_ruido WHERE codigo = s.categoria)
            FROM unnest(@codigos, @nombres, @categorias) WITH ORDINALITY AS s(codigo, nombre, categoria, orden)
            ORDER BY s.orden
            ON CONFLICT (codigo) DO NOTHING
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("codigos", NoiseCatalog.Sources.Select(s => s.Value).ToArray());
            command.Parameters.AddWithValue("nombres", NoiseCatalog.Sources.Select(s => s.Label).ToArray());
            command.Parameters.AddWithValue("categorias", NoiseCatalog.Sources.Select(s => NoiseCatalog.SourceCategories[s.Value]).ToArray());
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, transaction, """
            INSERT INTO estados_reporte(codigo_estado, descripcion)
            VALUES ('received', 'Reporte recibido; no constituye una denuncia formal')
            ON CONFLICT (codigo_estado) DO NOTHING
            """, ct).ConfigureAwait(false);
    }

    private static async Task SeedHotspotsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        foreach (var point in NoiseCatalog.DemoHotspots)
        {
            int? districtId = null;
            if (JuninCatalog.HotspotDistrictCodes.TryGetValue(point.District, out var ubigeo))
            {
                await using var lookup = new NpgsqlCommand("SELECT id_distrito FROM distritos WHERE ubigeo = @ubigeo", connection, transaction);
                lookup.Parameters.AddWithValue("ubigeo", ubigeo);
                districtId = await lookup.ExecuteScalarAsync(ct).ConfigureAwait(false) as int?;
            }

            await using var command = new NpgsqlCommand("""
                INSERT INTO puntos_criticos
                    (id_punto, id_tipo_fuente, nombre, area, id_distrito, distrito_heredado, sector, nivel_db,
                     etiqueta_fuente, descripcion, franja_horaria, x, y, limite_referencia, ilustrativo, latitud, longitud)
                VALUES (@id, (SELECT id_tipo_fuente FROM tipos_fuente_ruido WHERE codigo = @fuente), @nombre, @area,
                        @distrito, @heredado, @sector, @nivel, @etiqueta, @descripcion, @franja, @x, @y, @limite,
                        @ilustrativo, @latitud, @longitud)
                ON CONFLICT (id_punto) DO NOTHING
                """, connection, transaction);
            command.Parameters.AddWithValue("id", point.Id);
            command.Parameters.AddWithValue("fuente", point.Source);
            command.Parameters.AddWithValue("nombre", point.Name);
            command.Parameters.AddWithValue("area", point.Area);
            command.Parameters.Add(new NpgsqlParameter("distrito", NpgsqlDbType.Integer) { Value = (object?)districtId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("heredado", NpgsqlDbType.Text) { Value = districtId is null ? point.District : DBNull.Value });
            command.Parameters.AddWithValue("sector", point.Sector);
            command.Parameters.AddWithValue("nivel", point.Decibels);
            command.Parameters.AddWithValue("etiqueta", point.SourceLabel);
            command.Parameters.AddWithValue("descripcion", point.Description);
            command.Parameters.AddWithValue("franja", point.TimeLabel);
            command.Parameters.AddWithValue("x", point.X);
            command.Parameters.AddWithValue("y", point.Y);
            command.Parameters.AddWithValue("limite", point.ReferenceLimit);
            command.Parameters.AddWithValue("ilustrativo", point.Illustrative);
            command.Parameters.Add(new NpgsqlParameter("latitud", NpgsqlDbType.Double) { Value = (object?)point.Latitude ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("longitud", NpgsqlDbType.Double) { Value = (object?)point.Longitude ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<HashSet<string>> CurrentTablesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("""
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string ReadSql(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"DecibelCity.Sql.{name}")
            ?? throw new InvalidOperationException($"No se encontró el script SQL incrustado {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
