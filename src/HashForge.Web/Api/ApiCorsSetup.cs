using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace HashForge.Web.Api;

/// <summary>
/// Lets pages on the listed websites read the API. A browser blocks a page from reading another site's
/// answers unless that site names the page's origin, which is what stops someone building their own
/// front end on the API. With nothing listed no origin is named and browsers keep blocking, as before.
/// </summary>
public sealed class ApiCorsSetup(IOptions<WebOptions> web) : IConfigureOptions<CorsOptions>
{
    public const string PolicyName = "HashForgeApi";

    public void Configure(CorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var origins = web.Value.ConfiguredApiOrigins
            .Select(entry => ApiOrigin.TryNormalise(entry, out var origin) ? origin : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        options.AddPolicy(PolicyName, policy =>
        {
            // The API only answers GET and never looks at cookies or sign-in details, so nothing more is offered.
            policy.WithMethods(HttpMethods.Get).AllowAnyHeader();
            if (origins.Contains(ApiOrigin.Any))
            {
                policy.AllowAnyOrigin();
            }
            else
            {
                policy.WithOrigins(origins);
            }
        });
    }
}
