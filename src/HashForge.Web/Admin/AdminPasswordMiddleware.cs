using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HashForge.Web.Admin;

/// <summary>
/// Asks for the admin password, when one is configured, before the admin area is served. It runs after the
/// network check, so it adds to the allow-list rather than replacing it. It matters most where the
/// allow-list cannot tell visitors apart: behind a proxy or on a network shared with other software.
/// </summary>
public sealed partial class AdminPasswordMiddleware(
    RequestDelegate next,
    IOptions<AdminOptions> options,
    ILogger<AdminPasswordMiddleware> logger)
{
    // The browser shows its own sign-in prompt when it receives this, so the pool needs no login page.
    private const string Challenge = """Basic realm="HashForge admin", charset="UTF-8" """;

    private const string Explanation = "The admin page needs its password.";

    // Kept as a hash so that every comparison is between values of one length, which is what lets the
    // comparison take the same time whether the attempt is nearly right or entirely wrong.
    private readonly byte[]? _expected = options.Value.Password.Length > 0 ? Hash(options.Value.Password) : null;

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_expected is null || !context.Request.Path.StartsWithSegments(AdminAccessMiddleware.AdminPath, StringComparison.OrdinalIgnoreCase))
        {
            return next(context);
        }

        var attempted = BasicCredentials.TryReadPassword(context.Request.Headers.Authorization, out var password);
        if (attempted && CryptographicOperations.FixedTimeEquals(Hash(password!), _expected))
        {
            return next(context);
        }

        if (attempted)
        {
            LogWrongPassword(context.Connection.RemoteIpAddress);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = Challenge.TrimEnd();
        context.Response.Headers.CacheControl = "no-store";
        // Writing a body of our own keeps the "not found" page from being shown in place of the prompt.
        return context.Response.WriteAsync(Explanation, context.RequestAborted);
    }

    private static byte[] Hash(string password) => SHA256.HashData(Encoding.UTF8.GetBytes(password));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin page request from {RemoteAddress} refused: wrong password")]
    private partial void LogWrongPassword(IPAddress? remoteAddress);
}
