using DecibelCity.Api.Models.Dtos;
using Npgsql;
using NpgsqlTypes;

namespace DecibelCity.Api.Repositories;

/// <summary>Persistencia PostgreSQL de puntos, fuentes y reportes (una conexión por operación).</summary>
/// <param name="dataSource">Origen de conexiones.</param>
public sealed class PostgresNoiseRepository(NpgsqlDataSource dataSource) : INoiseRepository
{
    private const string HotspotSelect = """
        SELECT p.id_punto, p.nombre, p.area, COALESCE(d.nombre_distrito, p.distrito_heredado) AS district,
               p.sector, p.nivel_db, f.codigo, p.etiqueta_fuente, p.descripcion, p.franja_horaria, p.x, p.y,
               p.limite_referencia, p.ilustrativo, p.latitud, p.longitud
        FROM puntos_criticos p JOIN tipos_fuente_ruido f USING (id_tipo_fuente)
        LEFT JOIN distritos d USING (id_distrito)
        ORDER BY p.ilustrativo DESC, p.id_punto ASC
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Hotspot>> ListHotspotsAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(HotspotSelect);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var hotspots = new List<Hotspot>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            hotspots.Add(new Hotspot(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Area: reader.GetString(2),
                District: reader.GetString(3),
                Sector: reader.GetString(4),
                Decibels: reader.GetDouble(5),
                Source: reader.GetString(6),
                SourceLabel: reader.GetString(7),
                Description: reader.GetString(8),
                TimeLabel: reader.GetString(9),
                X: reader.GetDouble(10),
                Y: reader.GetDouble(11),
                ReferenceLimit: reader.GetDouble(12),
                Illustrative: reader.GetBoolean(13),
                Latitude: reader.IsDBNull(14) ? null : reader.GetDouble(14),
                Longitude: reader.IsDBNull(15) ? null : reader.GetDouble(15)));
        }

        return hotspots;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NoiseSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("SELECT codigo, nombre_fuente FROM tipos_fuente_ruido ORDER BY id_tipo_fuente");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sources = new List<NoiseSourceOption>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sources.Add(new NoiseSourceOption(reader.GetString(0), reader.GetString(1)));
        }

        return sources;
    }

    /// <inheritdoc />
    public async Task SaveReportAsync(NoiseReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Sin columnas de cuenta: el reporte almacenado no se relaciona con la persona que lo envió.
        await using (var command = new NpgsqlCommand("""
            INSERT INTO reportes_ciudadanos
                (id_reporte, id_tipo_fuente, descripcion, nivel_estimado_db, fecha_hora, id_estado, id_lugar, proveedor_lugar)
            VALUES (@id, (SELECT id_tipo_fuente FROM tipos_fuente_ruido WHERE codigo = @fuente), @descripcion, @nivel,
                    @fecha, (SELECT id_estado FROM estados_reporte WHERE codigo_estado = @estado), @lugar, 'osm')
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", report.Id);
            command.Parameters.AddWithValue("fuente", report.Source);
            command.Parameters.AddWithValue("descripcion", report.Description);
            command.Parameters.Add(new NpgsqlParameter("nivel", NpgsqlDbType.Double) { Value = (object?)report.Decibels ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("fecha", NpgsqlDbType.TimestampTz) { Value = report.CreatedAt.ToUniversalTime() });
            command.Parameters.AddWithValue("estado", report.Status);
            command.Parameters.AddWithValue("lugar", report.PlaceId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Registrar en puntos_criticos para que el reporte sea visible en el mapa y en el listado cívico.
        await using (var command = new NpgsqlCommand("""
            INSERT INTO puntos_criticos
                (id_punto, id_tipo_fuente, nombre, area, id_distrito, distrito_heredado,
                 sector, nivel_db, etiqueta_fuente, descripcion, franja_horaria,
                 x, y, limite_referencia, ilustrativo, latitud, longitud)
            VALUES (@id,
                    (SELECT id_tipo_fuente FROM tipos_fuente_ruido WHERE codigo = @fuente),
                    @nombre,
                    @area,
                    COALESCE(
                        (SELECT id_distrito FROM distritos
                         WHERE @nombre ILIKE '%' || nombre_distrito || '%'
                           AND id_provincia = (SELECT id_provincia FROM provincias WHERE ubigeo = '1201' LIMIT 1)
                         LIMIT 1),
                        (SELECT id_distrito FROM distritos WHERE ubigeo = '120101' LIMIT 1)
                    ),
                    NULL,
                    @sector,
                    @nivel,
                    (SELECT nombre_fuente FROM tipos_fuente_ruido WHERE codigo = @fuente),
                    @descripcion,
                    @franja,
                    50.0,
                    50.0,
                    70.0,
                    FALSE,
                    @latitud,
                    @longitud)
            ON CONFLICT (id_punto) DO NOTHING
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", report.Id);
            command.Parameters.AddWithValue("fuente", report.Source);
            command.Parameters.AddWithValue("nombre", report.Location);
            command.Parameters.AddWithValue("area", "Huancayo");
            command.Parameters.AddWithValue("sector", "Reporte ciudadano");
            command.Parameters.AddWithValue("nivel", report.Decibels ?? 70.0);
            command.Parameters.AddWithValue("descripcion", report.Description);
            command.Parameters.AddWithValue("franja", $"Reportado {report.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}");
            command.Parameters.AddWithValue("latitud", report.Latitude);
            command.Parameters.AddWithValue("longitud", report.Longitude);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
