using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace DecibelCity.Api.Configuration;

/// <summary>
/// Carga un archivo <c>.env</c> local (si existe) con menor prioridad que las variables de
/// entorno reales. Solo admite líneas <c>CLAVE=valor</c>; no ejecuta ni expande nada.
/// </summary>
public static class DotEnvConfiguration
{
    /// <summary>Inserta el contenido del <c>.env</c> antes de las variables de entorno.</summary>
    /// <param name="configuration">Administrador de configuración de la aplicación.</param>
    /// <param name="path">Ruta del archivo <c>.env</c>.</param>
    public static void AddDotEnvFile(this ConfigurationManager configuration, string path)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!File.Exists(path))
        {
            return;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        var source = new MemoryConfigurationSource { InitialData = values };
        var sources = configuration.Sources;
        // Justo antes de las variables de entorno sin prefijo: el .env supera a appsettings.json,
        // pero cualquier variable de entorno real lo sustituye.
        var environmentIndex = sources.ToList().FindLastIndex(
            s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        sources.Insert(environmentIndex >= 0 ? environmentIndex : sources.Count, source);
    }
}
