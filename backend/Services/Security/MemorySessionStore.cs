using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DecibelCity.Api.Services.Security;

/// <summary>Sesiones opacas de un único proceso.</summary>
public interface ISessionStore
{
    /// <summary>Emite un token nuevo y revoca el anterior (si lo hay) en una sola sección crítica.</summary>
    /// <param name="idUsuario">Cuenta autenticada.</param>
    /// <param name="previousToken">Cookie anterior a revocar.</param>
    /// <returns>Token opaco (43 caracteres Base64 URL).</returns>
    string Create(int idUsuario, string? previousToken = null);

    /// <summary>Resuelve un token vigente.</summary>
    /// <param name="token">Valor de la cookie.</param>
    /// <returns>Cuenta o <c>null</c> si no existe o caducó.</returns>
    int? Resolve(string? token);

    /// <summary>Revoca un token (idempotente).</summary>
    /// <param name="token">Valor de la cookie.</param>
    void Revoke(string? token);
}

/// <summary>Constantes de la cookie de sesión.</summary>
public static class SessionCookie
{
    /// <summary>Nombre de la cookie.</summary>
    public const string Name = "decibelcity_session";

    /// <summary>Ruta de la cookie.</summary>
    public const string Path = "/api";

    /// <summary>Duración: ocho horas.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
}

/// <summary>
/// Almacén en memoria equivalente a <c>services/session_service.py</c> del backend Python retirado: guarda solo el
/// digest SHA-256 del token y la cuenta. Reiniciar el proceso invalida todas las cookies; no
/// debe compartirse entre varios workers. Al alcanzar el límite expulsa la sesión creada hace
/// más tiempo (sin renovar su posición al consultarla), tras purgar las caducadas.
/// </summary>
public sealed partial class MemorySessionStore : ISessionStore
{
    /// <summary>Límite por defecto de sesiones simultáneas.</summary>
    public const int DefaultMaxSessions = 10_000;

    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly int _maxSessions;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byDigest = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _byCreation = new();

    /// <summary>Crea el almacén.</summary>
    /// <param name="time">Reloj (sustituible en pruebas).</param>
    /// <param name="ttl">Duración de cada sesión.</param>
    /// <param name="maxSessions">Máximo de sesiones simultáneas.</param>
    public MemorySessionStore(TimeProvider time, TimeSpan? ttl = null, int maxSessions = DefaultMaxSessions)
    {
        ArgumentNullException.ThrowIfNull(time);
        _ttl = ttl ?? SessionCookie.Lifetime;
        if (_ttl <= TimeSpan.Zero || maxSessions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSessions), "La duración y el límite de sesiones deben ser positivos.");
        }

        _time = time;
        _maxSessions = maxSessions;
    }

    /// <summary>Número de sesiones almacenadas (diagnóstico y pruebas).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byDigest.Count;
            }
        }
    }

    /// <summary>Digests almacenados (pruebas: demuestra que no se guardan tokens).</summary>
    internal IReadOnlyCollection<string> Digests
    {
        get
        {
            lock (_gate)
            {
                return [.. _byDigest.Keys];
            }
        }
    }

    /// <inheritdoc />
    public string Create(int idUsuario, string? previousToken = null)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            PurgeExpired(now);
            if (Digest(previousToken) is { } previous)
            {
                Remove(previous);
            }

            while (_byDigest.Count >= _maxSessions && _byCreation.First is { } oldest)
            {
                Remove(oldest.Value.Digest);
            }

            string token, digest;
            do
            {
                token = NewToken();
                digest = Digest(token)!;
            }
            while (_byDigest.ContainsKey(digest));

            _byDigest[digest] = _byCreation.AddLast(new Entry(digest, idUsuario, now + _ttl));
            return token;
        }
    }

    /// <inheritdoc />
    public int? Resolve(string? token)
    {
        if (Digest(token) is not { } digest)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_byDigest.TryGetValue(digest, out var node))
            {
                return null;
            }

            if (node.Value.ExpiresAt <= _time.GetUtcNow())
            {
                Remove(digest);
                return null;
            }

            return node.Value.IdUsuario;
        }
    }

    /// <inheritdoc />
    public void Revoke(string? token)
    {
        if (Digest(token) is not { } digest)
        {
            return;
        }

        lock (_gate)
        {
            Remove(digest);
        }
    }

    /// <summary>Digest SHA-256 en hexadecimal; <c>null</c> si el token no tiene el formato esperado.</summary>
    /// <param name="token">Token recibido.</param>
    /// <returns>Digest o <c>null</c>.</returns>
    internal static string? Digest(string? token)
    {
        if (token is null || !TokenPattern().IsMatch(token))
        {
            return null;
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    }

    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private void PurgeExpired(DateTimeOffset now)
    {
        var node = _byCreation.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.ExpiresAt <= now)
            {
                _byDigest.Remove(node.Value.Digest);
                _byCreation.Remove(node);
            }

            node = next;
        }
    }

    private void Remove(string digest)
    {
        if (_byDigest.Remove(digest, out var node))
        {
            _byCreation.Remove(node);
        }
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{43}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    private sealed record Entry(string Digest, int IdUsuario, DateTimeOffset ExpiresAt);
}
