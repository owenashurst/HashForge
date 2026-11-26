using System.Net;
using HashForge.Domain;
using HashForge.Web.Admin;
using HashForge.Web.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HashForge.Web.UnitTests;

public sealed class WebOptionsAndFormTests
{
    private static ServiceProvider Provider(params (string Key, string Value)[] settings) =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build())
            .AddHashForgeWeb()
            .BuildServiceProvider();

    [Fact]
    public void GIVEN_ForwardedHeaders_WHEN_TrustedProxyConfigured_THEN_OnlyThatProxyIsTrusted()
    {
        using var provider = Provider(("Web:TrustedProxies:0", "10.0.0.5"), ("Web:TrustedProxies:1", ""));

        var forwarded = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Contains(IPAddress.Parse("10.0.0.5"), forwarded.KnownProxies);
        Assert.True(forwarded.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
    }

    [Fact]
    public void GIVEN_WebOptionsValidate_WHEN_ProxiesAreBlankOrValid_THEN_Succeeds()
    {
        var options = new WebOptions { TrustedProxies = ["", "  ", "192.168.0.2", "fd00::1"] };

        Assert.True(new WebOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(["192.168.0.2", "fd00::1"], options.ConfiguredProxies);
    }

    [Fact]
    public void GIVEN_WebOptionsValidate_WHEN_ProxyIsNotAnIpAddress_THEN_FailsNamingIt()
    {
        var result = new WebOptionsValidator().Validate(null, new WebOptions { TrustedProxies = ["proxy.local"] });

        Assert.Contains("proxy.local", result.FailureMessage);
    }

    [Fact]
    public void GIVEN_AddHashForgeWeb_WHEN_TrustedProxyIsInvalid_THEN_OptionsValidationFails()
    {
        using var provider = Provider(("Web:TrustedProxies:0", "not-an-ip"));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WebOptions>>().Value);
    }

    [Fact]
    public void GIVEN_WebOptionsValidate_WHEN_ApiOriginsAreBlankOrValid_THEN_Succeeds()
    {
        var options = new WebOptions { ApiAllowedOrigins = ["", " https://stats.example.com , http://localhost:5173/", "*"] };

        Assert.True(new WebOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(["https://stats.example.com", "http://localhost:5173/", "*"], options.ConfiguredApiOrigins);
    }

    [Theory]
    [InlineData("stats.example.com")]
    [InlineData("https://stats.example.com/dashboard")]
    [InlineData("https://stats.example.com/?page=1")]
    [InlineData("https://user@stats.example.com")]
    [InlineData("ftp://stats.example.com")]
    [InlineData("https://*.example.com")]
    public void GIVEN_WebOptionsValidate_WHEN_ApiOriginIsNotAWebsitesOrigin_THEN_FailsNamingIt(string origin)
    {
        var result = new WebOptionsValidator().Validate(null, new WebOptions { ApiAllowedOrigins = [origin] });

        Assert.Contains($"'{origin}'", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void GIVEN_AddHashForgeWeb_WHEN_ApiOriginIsInvalid_THEN_OptionsValidationFails()
    {
        using var provider = Provider(("Web:ApiAllowedOrigins:0", "https://ok.example.com,not-an-origin"));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WebOptions>>().Value);
    }

    [Theory]
    [InlineData("https://Stats.Example.com/", "https://stats.example.com")]
    [InlineData("https://stats.example.com:443", "https://stats.example.com")]
    [InlineData(" http://localhost:5173 ", "http://localhost:5173")]
    [InlineData("*", "*")]
    public void GIVEN_TryNormalise_WHEN_OriginIsWrittenLoosely_THEN_ItIsWrittenAsABrowserSendsIt(string written, string expected)
    {
        Assert.True(ApiOrigin.TryNormalise(written, out var origin));
        Assert.Equal(expected, origin);
    }

    [Fact]
    public void GIVEN_ToDefinition_WHEN_FieldsHaveSurroundingSpaces_THEN_TheyAreTrimmed()
    {
        var form = new PoolForm { Name = " pplns-2 ", FeeAddress = " bc1qfee ", Port = 3337, FeePercent = 2, Scheme = PayoutScheme.Pplns };

        var pool = form.ToDefinition();

        Assert.Equal(("pplns-2", "bc1qfee", 3337, 2m, PayoutScheme.Pplns), (pool.Name, pool.FeeAddress, pool.Port, pool.FeePercent, pool.Scheme));
    }

    [Theory]
    [InlineData(PayoutScheme.FinderBonus, 0.5, 50_000_000)]
    [InlineData(PayoutScheme.FinderBonus, 0.123456789, 12_345_679)]
    [InlineData(PayoutScheme.Prop, 0.5, PoolDefinition.DefaultFinderBonusSatoshis)]
    public void GIVEN_ToDefinition_WHEN_BonusEntered_THEN_ItAppliesOnlyToBonusPoolsRoundedToWholeSatoshis(
        PayoutScheme scheme, double bonusBitcoin, long expectedSatoshis)
    {
        var form = new PoolForm { Scheme = scheme, FinderBonusBitcoin = (decimal)bonusBitcoin };

        Assert.Equal(expectedSatoshis, form.ToDefinition().FinderBonusSatoshis);
    }
}
