using DecibelCity.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace DecibelCity.Api.Tests.Unit;

public sealed class DecibelCityOptionsTests
{
    [Fact]
    public void FromConfiguration_FallsBackToDatabaseUrl_WhenDecibelCityDatabaseUrlMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://user:pass@railway.internal:5432/railway",
            })
            .Build();

        var options = DecibelCityOptions.FromConfiguration(config);
        Assert.Equal("postgresql://user:pass@railway.internal:5432/railway", options.DatabaseUrl);
    }

    [Fact]
    public void FromConfiguration_TrimsTrailingSlashes_FromCorsOrigins()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DECIBELCITY_CORS_ORIGINS"] = "https://frontend.up.railway.app/,https://app.example.com",
            })
            .Build();

        var options = DecibelCityOptions.FromConfiguration(config);
        Assert.Equal(["https://frontend.up.railway.app", "https://app.example.com"], options.CorsOrigins);
    }

    [Fact]
    public void FromConfiguration_ParsesCookieSameSite_ValidValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DECIBELCITY_COOKIE_SAMESITE"] = "None",
            })
            .Build();

        var options = DecibelCityOptions.FromConfiguration(config);
        Assert.Equal("None", options.CookieSameSite);
    }

    [Fact]
    public void FromConfiguration_ThrowsOnInvalidCookieSameSite()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DECIBELCITY_COOKIE_SAMESITE"] = "InvalidMode",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => DecibelCityOptions.FromConfiguration(config));
    }
}
