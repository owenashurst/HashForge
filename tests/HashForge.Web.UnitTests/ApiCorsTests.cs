using System.Net;
using HashForge.Mining.History;
using HashForge.Mining.Network;
using HashForge.Mining.Statistics;
using HashForge.Web.Api;
using HashForge.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace HashForge.Web.UnitTests;

/// <summary>Sends requests as a browser would from another website, to see which ones it would be allowed to read.</summary>
public sealed class ApiCorsTests
{
    private const string AllowOrigin = "Access-Control-Allow-Origin";
    private const string Dashboard = "https://stats.example.com";

    private readonly IPoolOverview _overview = Substitute.For<IPoolOverview>();

    private async Task<WebApplication> StartAsync(params string[] allowedOrigins)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddSingleton(_overview)
            .AddSingleton(Substitute.For<IStatisticsReader>())
            .AddSingleton(Substitute.For<IBlockRepository>())
            .AddSingleton(Substitute.For<INetworkStatsStore>())
            .AddSingleton(Options.Create(new WebOptions { ApiAllowedOrigins = allowedOrigins }))
            .AddHashForgeApi();
        var app = builder.Build();
        app.UseExceptionHandler("/Error");
        app.UseCors();
        app.MapHashForgeApi();
        app.MapGet("/elsewhere", () => Results.Ok());
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> SendAsync(WebApplication app, HttpMethod method, string path, string origin)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", origin);
        if (method == HttpMethod.Options)
        {
            request.Headers.Add("Access-Control-Request-Method", "GET");
        }

        return await client.SendAsync(request);
    }

    private static string? Allowed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(AllowOrigin, out var values) ? values.Single() : null;

    [Fact]
    public async Task GIVEN_Api_WHEN_NoOriginsAreAllowed_THEN_NoWebsiteIsNamed()
    {
        await using var app = await StartAsync();

        using var response = await SendAsync(app, HttpMethod.Get, "/api/pools", Dashboard);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Allowed(response));
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_CalledFromAnAllowedWebsite_THEN_ThatWebsiteIsNamed()
    {
        await using var app = await StartAsync(Dashboard, "http://localhost:5173");

        using var response = await SendAsync(app, HttpMethod.Get, "/api/pools", "http://localhost:5173");

        Assert.Equal("http://localhost:5173", Allowed(response));
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_CalledFromAWebsiteThatIsNotListed_THEN_ItIsNotNamed()
    {
        await using var app = await StartAsync(Dashboard);

        using var response = await SendAsync(app, HttpMethod.Get, "/api/pools", "https://elsewhere.example.com");

        Assert.Null(Allowed(response));
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_OriginsShareOneEntryAndAreWrittenLoosely_THEN_EachIsStillMatched()
    {
        await using var app = await StartAsync(" https://Stats.Example.com/ , http://localhost:5173 ");

        using var first = await SendAsync(app, HttpMethod.Get, "/api/pools", Dashboard);
        using var second = await SendAsync(app, HttpMethod.Get, "/api/pools", "http://localhost:5173");

        Assert.Equal(Dashboard, Allowed(first));
        Assert.Equal("http://localhost:5173", Allowed(second));
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_EveryWebsiteIsAllowed_THEN_AnyOriginMayRead()
    {
        await using var app = await StartAsync(ApiOrigin.Any);

        using var response = await SendAsync(app, HttpMethod.Get, "/api/pools", "https://anything.example.com");

        Assert.Equal(ApiOrigin.Any, Allowed(response));
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_BrowserAsksPermissionFirst_THEN_ItIsToldGetIsAllowed()
    {
        await using var app = await StartAsync(Dashboard);

        using var response = await SendAsync(app, HttpMethod.Options, "/api/pools", Dashboard);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Dashboard, Allowed(response));
        Assert.Equal("GET", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Theory]
    [InlineData("/api/pools/ghost", HttpStatusCode.NotFound)]
    [InlineData("/api/nothing-here", HttpStatusCode.NotFound)]
    public async Task GIVEN_Api_WHEN_AnAllowedWebsiteGetsAnError_THEN_ItMayStillReadIt(string path, HttpStatusCode expected)
    {
        await using var app = await StartAsync(Dashboard);

        using var response = await SendAsync(app, HttpMethod.Get, path, Dashboard);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(Dashboard, Allowed(response));
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_HandlerThrowsForAnAllowedWebsite_THEN_ItMayStillReadTheError()
    {
        _overview.GetPools().Throws(new InvalidOperationException("boom"));
        await using var app = await StartAsync(Dashboard);

        using var response = await SendAsync(app, HttpMethod.Get, "/api/pools", Dashboard);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(Dashboard, Allowed(response));
    }

    [Fact]
    public async Task GIVEN_AddressOutsideTheApi_WHEN_CalledFromAnAllowedWebsite_THEN_ItIsNotOpenedUp()
    {
        await using var app = await StartAsync(ApiOrigin.Any);

        using var response = await SendAsync(app, HttpMethod.Get, "/elsewhere", Dashboard);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Allowed(response));
    }
}
