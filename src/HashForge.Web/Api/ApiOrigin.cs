namespace HashForge.Web.Api;

/// <summary>
/// A website's origin (scheme, host and port), which is what a browser sends when a page on that site
/// calls the API and what it compares the pool's answer against.
/// </summary>
public static class ApiOrigin
{
    /// <summary>Stands for every website.</summary>
    public const string Any = "*";

    private const string RootPath = "/";

    /// <summary>
    /// Writes an origin the way a browser does: lower case, no trailing slash and no default port. A browser
    /// compares origins as text, so an entry pasted as "https://Example.com/" would otherwise never match.
    /// </summary>
    public static bool TryNormalise(string value, out string origin)
    {
        ArgumentNullException.ThrowIfNull(value);

        origin = value.Trim();
        if (origin == Any)
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !IsWebAddress(uri) || !IsBareOrigin(uri))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool IsWebAddress(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>An origin has no path, query or sign-in details; one that does is most likely a page address pasted by mistake.</summary>
    private static bool IsBareOrigin(Uri uri) =>
        uri.AbsolutePath == RootPath && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;
}
