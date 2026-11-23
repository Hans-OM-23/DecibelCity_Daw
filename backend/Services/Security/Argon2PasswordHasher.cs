using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace DecibelCity.Api.Services.Security;

/// <summary>Cálculo y verificación de hashes de contraseñas.</summary>
public interface IPasswordHasher
{
    /// <summary>Calcula un hash nuevo (sal aleatoria) en formato PHC.</summary>
    /// <param name="password">Contraseña en texto plano.</param>
    /// <returns>Hash PHC (<c>$argon2id$v=19$m=...,t=...,p=...$sal$hash</c>).</returns>
    string Hash(string password);

    /// <summary>Verifica en tiempo constante. Un hash corrupto o desconocido devuelve <c>false</c>.</summary>
    /// <param name="passwordHash">Hash almacenado.</param>
    /// <param name="password">Contraseña recibida.</param>
    /// <returns><c>true</c> si coincide.</returns>
    bool Verify(string passwordHash, string password);

    /// <summary>Indica si el hash usa parámetros distintos de los actuales.</summary>
    /// <param name="passwordHash">Hash almacenado.</param>
    /// <returns><c>true</c> si debe recalcularse.</returns>
    bool NeedsRehash(string passwordHash);
}

/// <summary>Parámetros Argon2 (por defecto los de <c>argon2-cffi</c> 25.x, RFC 9106 "low memory").</summary>
/// <param name="TimeCost">Iteraciones.</param>
/// <param name="MemoryCostKib">Memoria en KiB.</param>
/// <param name="Parallelism">Carriles.</param>
/// <param name="HashLength">Bytes del hash.</param>
/// <param name="SaltLength">Bytes de la sal.</param>
public sealed record Argon2Parameters(int TimeCost, int MemoryCostKib, int Parallelism, int HashLength, int SaltLength)
{
    /// <summary>Valores de <c>argon2.PasswordHasher()</c>: t=3, m=65536, p=4, hash 32, sal 16.</summary>
    public static Argon2Parameters ArgonCffiDefaults { get; } = new(3, 65536, 4, 32, 16);
}

/// <summary>
/// Argon2id compatible con hashes PHC emitidos por <c>argon2-cffi</c> en Python
/// (Base64 estándar sin relleno, versión 19). Implementado con <c>Isopoh.Cryptography.Argon2</c>.
/// Nunca registra contraseñas ni hashes.
/// </summary>
/// <param name="parameters">Parámetros para hashes nuevos y para decidir el rehash.</param>
public sealed class Argon2PasswordHasher(Argon2Parameters parameters) : IPasswordHasher
{
    private const int MaxMemoryKib = 1024 * 1024;
    private const int MaxTimeCost = 64;
    private const int MaxParallelism = 64;

    /// <summary>Crea el hasher con los parámetros por defecto de <c>argon2-cffi</c>.</summary>
    public Argon2PasswordHasher()
        : this(Argon2Parameters.ArgonCffiDefaults)
    {
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(parameters.SaltLength);
        var hash = Compute(Argon2Type.HybridAddressing, parameters.TimeCost, parameters.MemoryCostKib,
            parameters.Parallelism, salt, Encoding.UTF8.GetBytes(password), parameters.HashLength);
        return string.Create(CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={parameters.MemoryCostKib},t={parameters.TimeCost},p={parameters.Parallelism}${EncodeB64(salt)}${EncodeB64(hash)}");
    }

    /// <inheritdoc />
    public bool Verify(string passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!PhcHash.TryParse(passwordHash, out var phc))
        {
            return false;
        }

        if (phc.MemoryCostKib > MaxMemoryKib || phc.TimeCost > MaxTimeCost || phc.Parallelism > MaxParallelism)
        {
            return false;
        }

        var actual = Compute(phc.Type, phc.TimeCost, phc.MemoryCostKib, phc.Parallelism, phc.Salt,
            Encoding.UTF8.GetBytes(password), phc.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, phc.Hash);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string passwordHash)
    {
        if (!PhcHash.TryParse(passwordHash, out var phc))
        {
            return true;
        }

        return phc.Type != Argon2Type.HybridAddressing
               || phc.TimeCost != parameters.TimeCost
               || phc.MemoryCostKib != parameters.MemoryCostKib
               || phc.Parallelism != parameters.Parallelism
               || phc.Hash.Length != parameters.HashLength
               || phc.Salt.Length != parameters.SaltLength;
    }

    private static byte[] Compute(Argon2Type type, int timeCost, int memoryKib, int lanes, byte[] salt, byte[] password, int hashLength)
    {
        var config = new Argon2Config
        {
            Type = type,
            Version = Argon2Version.Nineteen,
            TimeCost = timeCost,
            MemoryCost = memoryKib,
            Lanes = lanes,
            Threads = Math.Min(lanes, Environment.ProcessorCount),
            Password = password,
            Salt = salt,
            HashLength = hashLength,
            ClearPassword = true,
        };

        using var argon2 = new Argon2(config);
        using var result = argon2.Hash();
        return result.Buffer.ToArray();
    }

    private static string EncodeB64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    /// <summary>Hash PHC analizado.</summary>
    private sealed record PhcHash(Argon2Type Type, int TimeCost, int MemoryCostKib, int Parallelism, byte[] Salt, byte[] Hash)
    {
        public static bool TryParse(string? encoded, out PhcHash phc)
        {
            phc = null!;
            if (string.IsNullOrEmpty(encoded) || encoded.Length > 512)
            {
                return false;
            }

            // $argon2id$v=19$m=65536,t=3,p=4$<sal>$<hash>
            var parts = encoded.Split('$');
            if (parts is not ["", var typeName, var version, var settings, var salt64, var hash64])
            {
                return false;
            }

            Argon2Type? type = typeName switch
            {
                "argon2id" => Argon2Type.HybridAddressing,
                "argon2i" => Argon2Type.DataIndependentAddressing,
                "argon2d" => Argon2Type.DataDependentAddressing,
                _ => null,
            };

            if (type is null || version != "v=19")
            {
                return false;
            }

            int? memory = null, time = null, lanes = null;
            foreach (var setting in settings.Split(','))
            {
                var pair = setting.Split('=', 2);
                if (pair.Length != 2 || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                {
                    return false;
                }

                switch (pair[0])
                {
                    case "m": memory = value; break;
                    case "t": time = value; break;
                    case "p": lanes = value; break;
                    default: return false;
                }
            }

            if (memory is null || time is null || lanes is null || memory < 8 * lanes
                || !TryDecodeB64(salt64, out var saltBytes) || saltBytes.Length < 8
                || !TryDecodeB64(hash64, out var hashBytes) || hashBytes.Length < 4)
            {
                return false;
            }

            phc = new PhcHash(type.Value, time.Value, memory.Value, lanes.Value, saltBytes, hashBytes);
            return true;
        }

        private static bool TryDecodeB64(string value, out byte[] bytes)
        {
            bytes = [];
            if (value.Length == 0 || value.Length % 4 == 1)
            {
                return false;
            }

            var padded = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
            var buffer = new byte[padded.Length];
            if (!Convert.TryFromBase64String(padded, buffer, out var written))
            {
                return false;
            }

            bytes = buffer[..written];
            return true;
        }
    }
}
