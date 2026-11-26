using System.Net;
using HashForge.Web.Api;
using Microsoft.Extensions.Options;

namespace HashForge.Web;

public sealed class WebOptionsValidator : IValidateOptions<WebOptions>
{
    public ValidateOptionsResult Validate(string? name, WebOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var invalid = options.ConfiguredProxies
            .Where(proxy => !IPAddress.TryParse(proxy, out _))
            .Select(proxy => $"Trusted proxy '{proxy}' is not a valid IP address.")
            .Concat(options.ConfiguredApiOrigins
                .Where(origin => !ApiOrigin.TryNormalise(origin, out _))
                .Select(origin => $"API allowed origin '{origin}' must be '{ApiOrigin.Any}' or a website's address with no path, such as https://stats.example.com."))
            .ToList();
        return invalid.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(invalid);
    }
}
