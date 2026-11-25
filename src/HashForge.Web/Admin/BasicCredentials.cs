using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace HashForge.Web.Admin;

/// <summary>Reads the password a browser sends after its built-in "sign in" prompt (HTTP Basic).</summary>
public static class BasicCredentials
{
    private const string Scheme = "Basic ";
    private const char UserPasswordSeparator = ':';

    /// <summary>
    /// Takes the password out of an <c>Authorization</c> header. The user name is ignored: the admin page
    /// has one password and no accounts, so whatever the visitor types as a name is accepted.
    /// </summary>
    public static bool TryReadPassword(string? authorizationHeader, [NotNullWhen(true)] out string? password)
    {
        password = null;
        if (authorizationHeader is null || !authorizationHeader.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = authorizationHeader.AsSpan(Scheme.Length).Trim();
        var decoded = new byte[encoded.Length];
        if (!Convert.TryFromBase64Chars(encoded, decoded, out var length))
        {
            return false;
        }

        var credentials = Encoding.UTF8.GetString(decoded, 0, length);
        var separator = credentials.IndexOf(UserPasswordSeparator, StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        // Only the first colon separates the two; a password may contain colons of its own.
        password = credentials[(separator + 1)..];
        return true;
    }
}
