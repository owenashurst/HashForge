namespace HashForge.Web.Branding;

/// <summary>Lets whoever runs the pool present it under their own name, colour and logo.</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";
    public const string DefaultName = "HashForge";
    public const string RepositoryUrl = "https://github.com/owenashurst/HashForge";
    public const int MaxNameLength = 40;

    /// <summary>HashForge's own logo, shipped with the pool. Relative, so it follows the page's base address.</summary>
    public const string BuiltInLogo = "logo.svg";
    public const int MaxTaglineLength = 120;

    /// <summary>The pool's name, shown in the header, page titles and footer.</summary>
    public string Name { get; set; } = DefaultName;

    /// <summary>A short line shown beside the name in the footer.</summary>
    public string Tagline { get; set; } = "Open-source Bitcoin mining pool";

    /// <summary>The highlight colour as #RRGGBB, used for the logo, the active tab and primary buttons.</summary>
    public string AccentColor { get; set; } = "#f7931a";

    /// <summary>
    /// An image to use as the logo (an https:// URL or a path starting with /). Empty shows HashForge's
    /// logo while the pool keeps the name HashForge, and the name's initial once it has a name of its own.
    /// </summary>
    public string LogoUrl { get; set; } = string.Empty;

    public bool IsDefaultName => string.Equals(Name, DefaultName, StringComparison.Ordinal);

    /// <summary>
    /// The image to show as the logo, or empty to show the initial. A pool that has been given its own
    /// name does not carry HashForge's logo: that would present someone else's pool as this project.
    /// </summary>
    public string EffectiveLogoUrl =>
        !string.IsNullOrWhiteSpace(LogoUrl) ? LogoUrl : IsDefaultName ? BuiltInLogo : string.Empty;

    /// <summary>The first letter of the name, shown as the logo when there is no image to show.</summary>
    public string Initial => Name.Trim() is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";

    /// <summary>The browser tab title for a page, e.g. "Blocks – My Pool".</summary>
    public string PageTitle(string? page) => string.IsNullOrWhiteSpace(page) ? Name : $"{page} – {Name}";
}
