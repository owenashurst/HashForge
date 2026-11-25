using System.Net;
using System.Text;
using HashForge.Web.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HashForge.Web.UnitTests;

public sealed class AdminPasswordTests
{
    private const string Password = "correct horse: battery staple";

    private static string Basic(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    private static async Task<(HttpContext Context, bool ReachedPage)> RequestAsync(
        string path, string configuredPassword, string? authorization, ILogger<AdminPasswordMiddleware>? logger = null)
    {
        var reachedPage = false;
        var middleware = new AdminPasswordMiddleware(
            _ =>
            {
                reachedPage = true;
                return Task.CompletedTask;
            },
            Options.Create(new AdminOptions { Password = configuredPassword }),
            logger ?? Substitute.For<ILogger<AdminPasswordMiddleware>>());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        await middleware.InvokeAsync(context);

        return (context, reachedPage);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/anything")]
    [InlineData("/ADMIN")]
    public async Task GIVEN_InvokeAsync_WHEN_NoPasswordIsConfigured_THEN_AdminPageIsServedWithoutAsking(string path)
    {
        var (context, reachedPage) = await RequestAsync(path, configuredPassword: string.Empty, authorization: null);

        Assert.True(reachedPage);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pool/solo")]
    [InlineData("/api/pools")]
    [InlineData("/administrator")]
    public async Task GIVEN_InvokeAsync_WHEN_PageIsNotTheAdminArea_THEN_NoPasswordIsAsked(string path)
    {
        var (_, reachedPage) = await RequestAsync(path, Password, authorization: null);

        Assert.True(reachedPage);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/Admin/pools")]
    public async Task GIVEN_InvokeAsync_WHEN_PasswordIsConfiguredAndNoneIsSent_THEN_BrowserIsToldToAsk(string path)
    {
        var (context, reachedPage) = await RequestAsync(path, Password, authorization: null);

        Assert.False(reachedPage);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.StartsWith("Basic realm=", context.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.True(context.Response.Body.Length > 0);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData("anyone at all")]
    public async Task GIVEN_InvokeAsync_WHEN_PasswordIsCorrect_THEN_AdminPageIsServedWhateverTheUserName(string user)
    {
        var (context, reachedPage) = await RequestAsync("/admin", Password, Basic(user, Password));

        Assert.True(reachedPage);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData("correct horse: battery stapl")]
    [InlineData("correct horse: battery staple ")]
    [InlineData("CORRECT HORSE: BATTERY STAPLE")]
    public async Task GIVEN_InvokeAsync_WHEN_PasswordIsWrong_THEN_RequestIsRefusedAndLogged(string attempt)
    {
        var logger = Substitute.For<ILogger<AdminPasswordMiddleware>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);

        var (context, reachedPage) = await RequestAsync("/admin", Password, Basic("admin", attempt), logger);

        Assert.False(reachedPage);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var logged = Assert.Single(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log));
        Assert.Equal(LogLevel.Warning, logged.GetArguments()[0]);
        Assert.Contains("192.168.1.20", logged.GetArguments()[2]!.ToString());
    }

    [Fact]
    public async Task GIVEN_InvokeAsync_WHEN_NoPasswordIsSent_THEN_NothingIsLogged()
    {
        var logger = Substitute.For<ILogger<AdminPasswordMiddleware>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);

        await RequestAsync("/admin", Password, authorization: null, logger);

        Assert.DoesNotContain(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log));
    }

    [Theory]
    [InlineData("Bearer abcdef")]
    [InlineData("Basic")]
    [InlineData("Basic not-base64!!")]
    [InlineData("Basic bm9jb2xvbg==")]
    public async Task GIVEN_InvokeAsync_WHEN_AuthorizationIsNotAPasswordSignIn_THEN_BrowserIsToldToAsk(string authorization)
    {
        var (context, reachedPage) = await RequestAsync("/admin", Password, authorization);

        Assert.False(reachedPage);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("admin", "secret", "secret")]
    [InlineData("", "secret", "secret")]
    [InlineData("admin", "has:colons:inside", "has:colons:inside")]
    [InlineData("admin", "", "")]
    [InlineData("admin", "lösenord", "lösenord")]
    public void GIVEN_TryReadPassword_WHEN_HeaderIsABasicSignIn_THEN_PasswordIsEverythingAfterTheFirstColon(
        string user, string password, string expected)
    {
        var read = BasicCredentials.TryReadPassword(Basic(user, password), out var actual);

        Assert.True(read);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GIVEN_TryReadPassword_WHEN_SchemeIsWrittenInAnotherCase_THEN_ItIsStillRead()
    {
        var header = Basic("admin", "secret").Replace("Basic", "basic", StringComparison.Ordinal);

        Assert.True(BasicCredentials.TryReadPassword(header, out var password));
        Assert.Equal("secret", password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abcdef")]
    [InlineData("Basic")]
    [InlineData("Basic not-base64!!")]
    [InlineData("Basic bm9jb2xvbg==")]
    public void GIVEN_TryReadPassword_WHEN_HeaderIsMissingOrNotABasicSignIn_THEN_ReturnsFalse(string? header)
    {
        Assert.False(BasicCredentials.TryReadPassword(header, out var password));
        Assert.Null(password);
    }
}
