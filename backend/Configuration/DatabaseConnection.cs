using System.Text.RegularExpressions;
using Npgsql;

namespace DecibelCity.Api.Configuration;

/// <summary>Validación de nombres de esquema PostgreSQL (mismo patrón que el backend Python).</summary>
public static partial class SchemaName
{
    /// <summary>Indica si el nombre es un identificador PostgreSQL simple en minúsculas.</summary>
    /// <param name="schema">Nombre a comprobar.</param>
    /// <returns><c>true</c> si es seguro usarlo como <c>search_path</c>.</returns>
    public static bool IsValid(string schema) => SchemaPattern().IsMatch(schema);

    [GeneratedRegex(@"\A[a-z_][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaPattern();
}

/// <summary>Construye el <see cref="NpgsqlDataSource"/> a partir de una URL <c>postgresql://</c>.</summary>
public static class DatabaseConnection
{
    /// <summary>
    /// Convierte <c>postgresql://usuario:clave@host:puerto/base?sslmode=...</c> en una cadena Npgsql.
    /// Si no hay contraseña, Npgsql recurre a <c>PGPASSWORD</c> o al archivo <c>pgpass</c>.
    /// También acepta una cadena Npgsql (<c>Host=...;Database=...</c>).
    /// </summary>
    /// <param name="databaseUrl">URL o cadena de conexión.</param>
    /// <param name="schema">Esquema que se fija como <c>search_path</c>.</param>
    /// <returns>Cadena de conexión con zona horaria UTC y tiempo de conexión acotado.</returns>
    /// <exception cref="InvalidOperationException">Si la URL no utiliza PostgreSQL o el esquema no es válido.</exception>
    public static string BuildConnectionString(string databaseUrl, string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseUrl);
        if (!SchemaName.IsValid(schema))
        {
            throw new InvalidOperationException("El nombre del esquema PostgreSQL no es válido.");
        }

        var builder = databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                      || databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            ? FromUrl(databaseUrl)
            : databaseUrl.Contains('=', StringComparison.Ordinal)
                ? new NpgsqlConnectionStringBuilder(databaseUrl)
                : throw new InvalidOperationException("La conexión debe utilizar PostgreSQL.");

        builder.SearchPath = schema;
        builder.Timezone = "UTC";
        if (builder.Timeout is 15)
        {
            builder.Timeout = 5;
        }

        builder.ApplicationName ??= "DecibelCity.Api";
        return builder.ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder FromUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException("La URL de PostgreSQL no es válida.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length == 2)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            switch (parts[0].ToUpperInvariant())
            {
                case "SSLMODE" when Enum.TryParse<SslMode>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var mode):
                    builder.SslMode = mode;
                    break;
                case "CONNECT_TIMEOUT" when int.TryParse(value, out var seconds) && seconds > 0:
                    builder.Timeout = seconds;
                    break;
            }
        }

        return builder;
    }
}
