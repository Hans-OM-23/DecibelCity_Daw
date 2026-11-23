using System.Collections.Concurrent;
using System.Diagnostics;
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Services.Osm;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Unit;

/// <summary>Uso justo de Nominatim: al menos 1 s entre peticiones, compartido por /lookup y /reverse.</summary>
public sealed class NominatimThrottleTests
{
    [Fact]
    public async Task ConsecutiveLookupAndReverse_AreSeparatedByAtLeastOneSecond()
    {
        var stub = TestData.NominatimStub();
        var options = TestOptions.Create() with { NominatimMinInterval = TimeSpan.FromSeconds(1) };
        using var throttle = new NominatimThrottle(TimeProvider.System, options.NominatimMinInterval);
        using var http = new HttpClient(stub);
        var client = new NominatimClient(http, options, throttle);

        await client.VerifyAsync("W813046785", TestContext.Current.CancellationToken);
        await client.ReverseAsync(-12.0664834, -75.2062612, TestContext.Current.CancellationToken);
        await client.VerifyAsync("W813046785", TestContext.Current.CancellationToken);

        var requests = stub.Requests;
        Assert.Equal(3, requests.Count);
        Assert.Equal(["/lookup", "/reverse", "/lookup"], requests.Select(r => r.Uri.AbsolutePath));
        for (var i = 1; i < requests.Count; i++)
        {
            // Medido donde la petición llega al "servidor" (el doble HTTP), no donde se concede el turno.
            var gap = Stopwatch.GetElapsedTime(requests[i - 1].Timestamp, requests[i].Timestamp);
            Assert.True(gap >= TimeSpan.FromSeconds(1), $"Separación insuficiente: {gap.TotalMilliseconds} ms");
        }

        Assert.All(requests, r => Assert.Equal(TestData.UserAgent, r.UserAgent));
    }

    [Fact]
    public async Task ConcurrentCallers_AreSerializedAndSeparated()
    {
        using var throttle = new NominatimThrottle(TimeProvider.System, TimeSpan.FromMilliseconds(300));
        var running = 0;
        var maxRunning = 0;
        var starts = new ConcurrentBag<long>();

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => throttle.RunAsync(async ct =>
        {
            starts.Add(Stopwatch.GetTimestamp());
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref running);
            return true;
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(1, maxRunning);
        var ordered = starts.Order().ToArray();
        Assert.True(Stopwatch.GetElapsedTime(ordered[0], ordered[2]) >= TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task ZeroInterval_DoesNotWait()
    {
        using var throttle = new NominatimThrottle(TimeProvider.System, TimeSpan.Zero);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            await throttle.RunAsync(_ => Task.FromResult(i), TestContext.Current.CancellationToken);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Options_DefaultIntervalIsOneSecond()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new DecibelCityOptions().NominatimMinInterval);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
