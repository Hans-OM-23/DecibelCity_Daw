namespace DecibelCity.Api.Services.Osm;

/// <summary>
/// Uso justo de Nominatim (singleton compartido por <c>/lookup</c> y <c>/reverse</c>): las peticiones se
/// envían de una en una y cada una empieza al menos el intervalo configurado (1 s en servidores públicos)
/// después de que terminó la anterior. Así la separación se garantiza en el punto de envío, sin depender
/// del coste de preparar la petición.
/// </summary>
/// <param name="time">Reloj (sustituible en pruebas).</param>
/// <param name="interval">Separación mínima; cero desactiva la espera y la serialización.</param>
public sealed class NominatimThrottle(TimeProvider time, TimeSpan interval) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long? _lastCompletedTimestamp;

    /// <summary>Intervalo configurado.</summary>
    public TimeSpan Interval => interval;

    /// <summary>Espera el turno, ejecuta la petición y registra cuándo terminó.</summary>
    /// <typeparam name="T">Resultado de la petición.</typeparam>
    /// <param name="send">Envío de la petición.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Resultado de <paramref name="send"/>.</returns>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> send, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);
        if (interval <= TimeSpan.Zero)
        {
            return await send(cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastCompletedTimestamp is { } last)
            {
                // Bucle: los temporizadores del sistema pueden despertar unos microsegundos antes.
                TimeSpan delay;
                while ((delay = interval - time.GetElapsedTime(last)) > TimeSpan.Zero)
                {
                    await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                return await send(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lastCompletedTimestamp = time.GetTimestamp();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
