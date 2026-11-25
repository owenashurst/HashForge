namespace HashForge.Web;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Used when no networks are configured: common home LAN ranges plus the machine itself.</summary>
    public static readonly IReadOnlyList<string> DefaultNetworks = ["192.168.0.0/24", "192.168.1.0/24", "127.0.0.0/8", "::1/128"];

    /// <summary>CIDR ranges allowed to see and use the admin page. Leave empty to use the defaults.</summary>
    public IList<string> AllowedNetworks { get; init; } = [];

    /// <summary>
    /// A password the browser must give before the admin page opens, on top of the network check. Leave
    /// empty for none. Set one wherever the network check cannot tell visitors apart.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    public IEnumerable<string> EffectiveNetworks => AllowedNetworks.Count > 0 ? AllowedNetworks : DefaultNetworks;
}
