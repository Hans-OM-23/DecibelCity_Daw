using System.Security.Cryptography;
using System.Text;
using DecibelCity.Api.Services.Security;
using Microsoft.Extensions.Time.Testing;

namespace DecibelCity.Api.Tests.Unit;

/// <summary>Almacén de sesiones: digests, caducidad a las 8 h, rotación, revocación y límite.</summary>
public sealed class MemorySessionStoreTests
{
    [Fact]
    public void Create_StoresOnlyDigestAndExpiresAfterEightHours()
    {
        var time = new FakeTimeProvider();
        var store = new MemorySessionStore(time);

        var token = store.Create(17);

        Assert.Equal(43, token.Length);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        Assert.Equal([digest], store.Digests);
        Assert.DoesNotContain(token, store.Digests);

        time.Advance(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1));
        Assert.Equal(17, store.Resolve(token));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(store.Resolve(token));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Create_RotatesRevokesAndEvictsOldestWithoutRefreshingIt()
    {
        var store = new MemorySessionStore(TimeProvider.System, maxSessions: 2);
        var oldest = store.Create(1);
        var second = store.Create(2);
        Assert.Equal(1, store.Resolve(oldest)); // Consultar no la renueva.

        var newest = store.Create(3);
        Assert.Null(store.Resolve(oldest));
        Assert.Equal(2, store.Resolve(second));

        var rotated = store.Create(4, previousToken: newest);
        Assert.Null(store.Resolve(newest));
        Assert.Equal(2, store.Resolve(second));
        Assert.Equal(4, store.Resolve(rotated));

        store.Revoke(rotated);
        store.Revoke(rotated);
        Assert.Null(store.Resolve(rotated));
    }

    [Fact]
    public void Create_PurgesExpiredBeforeEnforcingLimit()
    {
        var time = new FakeTimeProvider();
        var store = new MemorySessionStore(time, TimeSpan.FromSeconds(10), maxSessions: 2);
        var expired = store.Create(1);
        time.Advance(TimeSpan.FromSeconds(5));
        var live = store.Create(2);
        time.Advance(TimeSpan.FromSeconds(5));
        var newest = store.Create(3);

        Assert.Null(store.Resolve(expired));
        Assert.Equal(2, store.Resolve(live));
        Assert.Equal(3, store.Resolve(newest));
        Assert.Equal(2, store.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inventada")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("token con espacios no válido para el formato esperado..")]
    public void Resolve_UnknownOrMalformedToken_ReturnsNull(string? token)
    {
        var store = new MemorySessionStore(TimeProvider.System);
        store.Create(1);
        Assert.Null(store.Resolve(token));
    }

    [Fact]
    public void Store_IsSafeBetweenThreads()
    {
        var store = new MemorySessionStore(TimeProvider.System);
        var tokens = new string[200];
        Parallel.For(0, 200, i => tokens[i] = store.Create(i));
        Assert.Equal(200, tokens.Distinct(StringComparer.Ordinal).Count());
        Parallel.For(0, 200, i => Assert.Equal(i, store.Resolve(tokens[i])));
        Parallel.For(0, 200, i => store.Revoke(tokens[i]));
        Assert.Equal(0, store.Count);

        var limited = new MemorySessionStore(TimeProvider.System, maxSessions: 10);
        Parallel.For(0, 100, i => limited.Create(i));
        Assert.Equal(10, limited.Count);
    }
}
