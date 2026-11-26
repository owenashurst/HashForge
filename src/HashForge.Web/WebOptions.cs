using System.ComponentModel.DataAnnotations;

namespace HashForge.Web;

public sealed class WebOptions
{
    public const string SectionName = "Web";

    /// <summary>The first port Docker publishes for pools started from the admin page.</summary>
    public const int FirstSparePoolPort = 3337;

    private const char OriginSeparator = ',';

    /// <summary>How often live pages re-read the statistics.</summary>
    [Range(1, 3600)]
    public double RefreshSeconds { get; set; } = 5;

    /// <summary>The host name miners should connect to, shown on the "connect" page.</summary>
    public string StratumHost { get; set; } = "your-pool-host";

    [Range(1, 500)]
    public int RecentBlockCount { get; set; } = 25;

    [Range(1, 720)]
    public double ChartHours { get; set; } = 24;

    /// <summary>
    /// IP addresses of reverse proxies in front of the pool. Their X-Forwarded-For header is trusted, so the
    /// admin allow-list sees the visitor's real address. Leave empty when clients connect directly.
    /// </summary>
    public IList<string> TrustedProxies { get; init; } = [];

    /// <summary>
    /// The request header in which a reverse proxy names the sub-path it serves the pool under, for example
    /// <c>X-Ingress-Path</c> for Home Assistant or <c>X-Forwarded-Prefix</c>. Leave empty when the pool is
    /// served from the root of its address.
    /// </summary>
    public string PathBaseHeader { get; set; } = string.Empty;

    /// <summary>
    /// Websites whose pages may read the JSON API from a browser, each written as an origin such as
    /// <c>https://stats.example.com</c>, or <c>*</c> for any website. An entry may hold several, separated
    /// by commas, so one environment variable can carry the whole list. Leave empty to allow none.
    /// </summary>
    public IList<string> ApiAllowedOrigins { get; init; } = [];

    /// <summary>The proxies actually configured; blank entries (an unset environment variable) are ignored.</summary>
    public IEnumerable<string> ConfiguredProxies => TrustedProxies.Where(proxy => !string.IsNullOrWhiteSpace(proxy));

    /// <summary>The origins actually configured, one per item; blank entries are ignored.</summary>
    public IEnumerable<string> ConfiguredApiOrigins => ApiAllowedOrigins
        .Where(entry => entry is not null)
        .SelectMany(entry => entry.Split(OriginSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
