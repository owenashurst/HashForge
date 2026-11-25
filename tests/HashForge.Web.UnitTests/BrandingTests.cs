using Bunit;
using HashForge.Web.Branding;
using HashForge.Web.Components.Layout;
using Microsoft.AspNetCore.Components.Web;

namespace HashForge.Web.UnitTests;

public sealed class BrandingTests : WebTestContext
{
    private static readonly BrandingOptionsValidator Validator = new();

    [Fact]
    public void GIVEN_OptionsValidate_WHEN_Defaults_THEN_Succeeds()
    {
        Assert.True(Validator.Validate(null, new BrandingOptions()).Succeeded);
    }

    [Theory]
    [InlineData("", "#f7931a", "", "name")]
    [InlineData("A name that is far too long to fit in a header bar", "#f7931a", "", "name")]
    [InlineData("Pool", "orange", "", "accent")]
    [InlineData("Pool", "#f7931", "", "accent")]
    [InlineData("Pool", "#f7931a;}body{display:none", "", "accent")]
    [InlineData("Pool", "#f7931a", "http://insecure.example/logo.png", "logo")]
    [InlineData("Pool", "#f7931a", "javascript:alert(1)", "logo")]
    [InlineData("Pool", "#f7931a", "//evil.example/logo.png", "logo")]
    public void GIVEN_OptionsValidate_WHEN_SettingIsInvalid_THEN_FailsNamingIt(string name, string accent, string logo, string expectedFragment)
    {
        var result = Validator.Validate(null, new BrandingOptions { Name = name, AccentColor = accent, LogoUrl = logo });

        Assert.True(result.Failed);
        Assert.Contains(expectedFragment, result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://cdn.example/logo.svg")]
    [InlineData("/logo.png")]
    public void GIVEN_OptionsValidate_WHEN_LogoIsHttpsOrLocalPath_THEN_Succeeds(string logo)
    {
        Assert.True(Validator.Validate(null, new BrandingOptions { LogoUrl = logo }).Succeeded);
    }

    [Fact]
    public void GIVEN_OptionsValidate_WHEN_TaglineTooLong_THEN_Fails()
    {
        var options = new BrandingOptions { Tagline = new string('x', BrandingOptions.MaxTaglineLength + 1) };

        Assert.Contains("tagline", Validator.Validate(null, options).FailureMessage);
    }

    [Theory]
    [InlineData(null, "Cornish Miners")]
    [InlineData("  ", "Cornish Miners")]
    [InlineData("Blocks", "Blocks – Cornish Miners")]
    public void GIVEN_PageTitle_WHEN_PageGivenOrNot_THEN_CombinesPageAndPoolName(string? page, string expected)
    {
        Assert.Equal(expected, new BrandingOptions { Name = "Cornish Miners" }.PageTitle(page));
    }

    [Theory]
    [InlineData("HashForge", "H", true)]
    [InlineData("  cornish miners", "C", false)]
    [InlineData("   ", "?", false)]
    public void GIVEN_Initial_WHEN_NameGiven_THEN_ReturnsUppercaseFirstLetter(string name, string expectedInitial, bool expectedDefault)
    {
        var options = new BrandingOptions { Name = name };

        Assert.Equal(expectedInitial, options.Initial);
        Assert.Equal(expectedDefault, options.IsDefaultName);
    }

    [Theory]
    [InlineData("#f7931a", "#1b1204")]
    [InlineData("#ffffff", "#1b1204")]
    [InlineData("#0b3d91", "#ffffff")]
    [InlineData("#000000", "#ffffff")]
    public void GIVEN_TextColorOn_WHEN_AccentIsLightOrDark_THEN_PicksReadableTextColour(string accent, string expected)
    {
        Assert.Equal(expected, BrandTheme.TextColorOn(accent));
    }

    [Fact]
    public void GIVEN_CssOverrides_WHEN_AccentGiven_THEN_SetsAccentSoftAndTextVariables()
    {
        Assert.Equal(
            ":root{--accent:#0b3d91;--accent-soft:rgba(11,61,145,0.14);--on-accent:#ffffff}",
            BrandTheme.CssOverrides("#0b3d91"));
    }

    [Fact]
    public void GIVEN_CssOverrides_WHEN_ColourIsNotHex_THEN_Throws()
    {
        Assert.Throws<ArgumentException>(() => BrandTheme.CssOverrides("red;}"));
        Assert.False(BrandTheme.IsHexColor(null));
    }

    [Fact]
    public void GIVEN_BrandStyle_WHEN_Rendered_THEN_EmitsAccentOverrides()
    {
        Branding.AccentColor = "#0b3d91";

        Assert.Contains("--accent:#0b3d91", Render<BrandStyle>().Find("style").TextContent);
    }

    [Fact]
    public void GIVEN_BrandTitle_WHEN_Rendered_THEN_SetsBrandedPageTitle()
    {
        Branding.Name = "Cornish Miners";

        var title = Render<BrandTitle>(p => p.Add(t => t.Page, "Blocks")).FindComponent<PageTitle>();

        Assert.NotNull(title.Instance.ChildContent);
    }

    [Fact]
    public void GIVEN_SiteFooter_WHEN_DefaultBrand_THEN_HashForgeLinksToRepository()
    {
        var footer = Render<SiteFooter>();

        var link = footer.Find("a[target=_blank]");
        Assert.Equal("HashForge", link.TextContent);
        Assert.Equal(BrandingOptions.RepositoryUrl, link.GetAttribute("href"));
        Assert.DoesNotContain("Powered by", footer.Markup);
        Assert.Contains("Open-source Bitcoin mining pool", footer.Markup);
    }

    [Fact]
    public void GIVEN_SiteFooter_WHEN_CustomBrand_THEN_ShowsBrandAndPoweredByHashForgeLink()
    {
        Branding.Name = "Cornish Miners";
        Branding.Tagline = "Mining by the sea";

        var footer = Render<SiteFooter>();

        Assert.Contains("Cornish Miners", footer.Markup);
        Assert.Contains("Mining by the sea", footer.Markup);
        Assert.Contains("Powered by", footer.Markup);
        Assert.Equal(BrandingOptions.RepositoryUrl, footer.Find("a[target=_blank]").GetAttribute("href"));
    }

    [Fact]
    public void GIVEN_MainLayout_WHEN_CustomBrandWithLogo_THEN_ShowsNameAndLogoImage()
    {
        Branding.Name = "Cornish Miners";
        Branding.LogoUrl = "https://cdn.example/logo.svg";

        var layout = Render<MainLayout>();

        Assert.Equal("Cornish Miners", layout.Find(".brand-name").TextContent);
        Assert.Equal("https://cdn.example/logo.svg", layout.Find("img.brand-mark").GetAttribute("src"));
        Assert.Empty(layout.FindAll("span.brand-mark"));
    }

    [Fact]
    public void GIVEN_MainLayout_WHEN_PoolHasItsOwnNameAndNoLogo_THEN_ShowsTheInitialNotHashForgesLogo()
    {
        Branding.Name = "Cornish Miners";

        var layout = Render<MainLayout>();

        Assert.Equal("C", layout.Find("span.brand-mark").TextContent);
        Assert.Empty(layout.FindAll("img.brand-mark"));
    }

    [Theory]
    [InlineData("HashForge", "", BrandingOptions.BuiltInLogo)]
    [InlineData("HashForge", "/img/mine.png", "/img/mine.png")]
    [InlineData("Cornish Miners", "https://cdn.example/logo.svg", "https://cdn.example/logo.svg")]
    [InlineData("Cornish Miners", "", "")]
    [InlineData("Cornish Miners", "  ", "")]
    public void GIVEN_EffectiveLogoUrl_WHEN_NameAndLogoVary_THEN_OwnLogoWinsAndBuiltInOneNeedsTheDefaultName(
        string name, string logoUrl, string expected)
    {
        var options = new BrandingOptions { Name = name, LogoUrl = logoUrl };

        Assert.Equal(expected, options.EffectiveLogoUrl);
    }

    [Fact]
    public void GIVEN_Favicon_WHEN_PoolUsesTheDefaultName_THEN_TabIconIsHashForgesLogo()
    {
        var icon = Render<Favicon>().Find("link[rel=icon]");

        Assert.Equal(BrandingOptions.BuiltInLogo, icon.GetAttribute("href"));
    }

    [Fact]
    public void GIVEN_Favicon_WHEN_PoolHasItsOwnLogo_THEN_TabIconIsThatLogo()
    {
        Branding.Name = "Cornish Miners";
        Branding.LogoUrl = "https://cdn.example/logo.svg";

        var icon = Render<Favicon>().Find("link[rel=icon]");

        Assert.Equal("https://cdn.example/logo.svg", icon.GetAttribute("href"));
    }

    [Fact]
    public void GIVEN_Favicon_WHEN_PoolHasItsOwnNameAndNoLogo_THEN_NoTabIconIsDeclared()
    {
        Branding.Name = "Cornish Miners";

        Assert.Empty(Render<Favicon>().FindAll("link[rel=icon]"));
    }
}
