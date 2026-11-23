using DecibelCity.Api.Configuration;
using DecibelCity.Api.Tests.Infrastructure;
using Npgsql;

namespace DecibelCity.Api.Tests.Postgres;

/// <summary>
/// Esquema temporal <c>test_&lt;guid&gt;</c> en una base PostgreSQL exclusiva de pruebas (nombre terminado en
/// <c>_test</c>). Se omite la prueba si <c>DECIBELCITY_TEST_DATABASE_URL</c> no está definida o la base no responde.
/// Solo crea y elimina su propio esquema.
/// </summary>
public sealed class PostgresSchemaScope : IAsyncDisposable
{
    private const string Variable = "DECIBELCITY_TEST_DATABASE_URL";

    private PostgresSchemaScope(string databaseUrl, string schema)
    {
        DatabaseUrl = databaseUrl;
        Schema = schema;
        DataSource = NpgsqlDataSource.Create(DatabaseConnection.BuildConnectionString(databaseUrl, schema));
    }

    /// <summary>URL de la base de pruebas.</summary>
    public string DatabaseUrl { get; }

    /// <summary>Esquema temporal.</summary>
    public string Schema { get; }

    /// <summary>Conexiones con <c>search_path</c> en el esquema temporal.</summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>Crea el esquema o marca la prueba como omitida.</summary>
    /// <returns>Ámbito listo.</returns>
    public static async Task<PostgresSchemaScope> CreateOrSkipAsync()
    {
        var url = Environment.GetEnvironmentVariable(Variable)?.Trim();
        Assert.SkipWhen(string.IsNullOrEmpty(url), $"{Variable} no está definida: se omiten las pruebas de PostgreSQL.");

        var schema = "test_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(DatabaseConnection.BuildConnectionString(url!, "public"));
        NpgsqlConnection connection;
        try
        {
            connection = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or System.Net.Sockets.SocketException)
        {
            Assert.Skip($"La base de {Variable} no está disponible ({exception.GetType().Name}).");
            throw;
        }

        await using (connection)
        {
            await using var name = new NpgsqlCommand("SELECT current_database()", connection);
            var database = (string)(await name.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
            Assert.True(database.EndsWith("_test", StringComparison.Ordinal),
                "La base de pruebas debe terminar en _test; no se utilizará la base de la aplicación.");

            // Nombre generado aquí, nunca tomado del entorno ni de entradas externas.
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return new PostgresSchemaScope(url!, schema);
    }

    /// <summary>Crea una aplicación conectada a este esquema (inicializa el esquema v5 al arrancar).</summary>
    /// <param name="productionArgon2">Usar los parámetros Argon2 reales.</param>
    /// <returns>Fábrica.</returns>
    public ApiFactory CreateFactory(bool productionArgon2 = false) => new()
    {
        UseRealDatabase = true,
        UseProductionArgon2 = productionArgon2,
        Options = TestOptions.Create() with { DatabaseUrl = DatabaseUrl, DatabaseSchema = Schema, InitializeDatabase = true },
    };

    /// <summary>Ejecuta SQL de preparación en el esquema temporal.</summary>
    /// <param name="sql">Sentencia.</param>
    /// <returns>Tarea.</returns>
    public async Task ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Lee un valor escalar.</summary>
    /// <typeparam name="T">Tipo.</typeparam>
    /// <param name="sql">Consulta.</param>
    /// <returns>Valor.</returns>
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Lee filas como listas de valores.</summary>
    /// <param name="sql">Consulta.</param>
    /// <returns>Filas.</returns>
    public async Task<List<object?[]>> RowsAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = NpgsqlDataSource.Create(DatabaseConnection.BuildConnectionString(DatabaseUrl, "public"));
        await using var drop = admin.CreateCommand($"DROP SCHEMA IF EXISTS {Schema} CASCADE");
        await drop.ExecuteNonQueryAsync();
    }
}
