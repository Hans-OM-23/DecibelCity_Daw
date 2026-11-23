using DecibelCity.Api.Services.Security;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Unit;

/// <summary>Paridad con hashes reales emitidos por argon2-cffi 25.1.0 en Python.</summary>
public sealed class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ArgonCffiDefaultHash_AcceptsCorrectPassword()
    {
        Assert.True(_hasher.Verify(TestData.ArgonCffiDefaultHash, TestData.Password));
    }

    [Theory]
    [InlineData(TestData.Password + "x")]
    [InlineData("ruido seguro 2026!")]
    [InlineData("")]
    public void Verify_ArgonCffiDefaultHash_RejectsWrongPassword(string password)
    {
        Assert.False(_hasher.Verify(TestData.ArgonCffiDefaultHash, password));
    }

    [Fact]
    public void Verify_ArgonCffiUnicodeHash_UsesUtf8AndKeepsSurroundingSpaces()
    {
        Assert.True(_hasher.Verify(TestData.ArgonCffiUnicodeHash, TestData.UnicodePassword));
        Assert.False(_hasher.Verify(TestData.ArgonCffiUnicodeHash, TestData.UnicodePassword.Trim()));
    }

    [Fact]
    public void Verify_ArgonCffiWeakHash_AcceptsPasswordAndRequiresRehash()
    {
        Assert.True(_hasher.Verify(TestData.ArgonCffiWeakHash, TestData.Password));
        Assert.True(_hasher.NeedsRehash(TestData.ArgonCffiWeakHash));
        Assert.False(_hasher.NeedsRehash(TestData.ArgonCffiDefaultHash));
    }

    [Fact]
    public void Hash_UsesArgonCffiPhcFormatAndRandomSalt()
    {
        var first = _hasher.Hash(TestData.Password);
        var second = _hasher.Hash(TestData.Password);

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", first, StringComparison.Ordinal);
        Assert.DoesNotContain(TestData.Password, first, StringComparison.Ordinal);
        var parts = first.Split('$');
        Assert.Equal(22, parts[4].Length); // 16 bytes de sal en Base64 sin relleno.
        Assert.Equal(43, parts[5].Length); // 32 bytes de hash en Base64 sin relleno.
        Assert.DoesNotContain("=", parts[4] + parts[5], StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify(first, TestData.Password));
        Assert.False(_hasher.NeedsRehash(first));
    }

    [Theory]
    [InlineData("no-es-un-hash")]
    [InlineData("")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$sal$")]
    [InlineData("$argon2id$v=16$m=65536,t=3,p=4$31KWXXrKM3XAj9YuxMR4Dg$e8/it5zJhubtw/q+InXmdypU5JP0SxGYv1J4occyq0o")]
    [InlineData("$scrypt$ln=16,r=8,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=999999999,t=3,p=4$31KWXXrKM3XAj9YuxMR4Dg$e8/it5zJhubtw/q+InXmdypU5JP0SxGYv1J4occyq0o")]
    public void Verify_CorruptOrUnsupportedHash_FailsClosed(string hash)
    {
        Assert.False(_hasher.Verify(hash, TestData.Password));
        Assert.True(_hasher.NeedsRehash(hash));
    }
}
