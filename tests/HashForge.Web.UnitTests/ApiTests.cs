using System.Net;
using System.Text.Json;
using HashForge.Domain;
using HashForge.Mining.History;
using HashForge.Mining.Network;
using HashForge.Mining.Shares;
using HashForge.Mining.Statistics;
using HashForge.Web.Api;
using HashForge.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace HashForge.Web.UnitTests;

/// <summary>Calls the API through a real request pipeline, so routing, JSON and error handling are all covered.</summary>
public sealed class ApiTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IPoolOverview _overview = Substitute.For<IPoolOverview>();
    private readonly IStatisticsReader _statistics = Substitute.For<IStatisticsReader>();
    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly INetworkStatsStore _networkStats = Substitute.For<INetworkStatsStore>();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddSingleton(_overview)
            .AddSingleton(_statistics)
            .AddSingleton(_blocks)
            .AddSingleton(_networkStats)
            .AddSingleton(Options.Create(new WebOptions { RecentBlockCount = 25 }))
            .AddHashForgeApi();
        _app = builder.Build();
        _app.UseExceptionHandler("/Error");
        _app.UseCors();
        _app.MapHashForgeApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static StatsSnapshot Stats() =>
        new(10, 1, new Dictionary<ShareOutcome, long> { [ShareOutcome.Duplicate] = 1 }, 5, 42.5, 2.5e12, new BestShare(900, "addr.rig", Now), Now);

    private static PoolSnapshot Pool(string name = "solo") => new(name, PayoutScheme.Solo, 3333, Stats(), 2, 3);

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static void AssertError(JsonElement body, string expectedCode)
    {
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        var error = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task GIVEN_GetPools_WHEN_PoolsAreRunning_THEN_ReturnsDataWithEmptyErrorsAndEnumsAsText()
    {
        _overview.GetPools().Returns([Pool("solo"), Pool("pplns")]);

        var (status, body) = await GetAsync("/api/pools");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
        var first = body.GetProperty("data")[0];
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());
        Assert.Equal("solo", first.GetProperty("name").GetString());
        Assert.Equal("Solo", first.GetProperty("scheme").GetString());
        Assert.Equal(42.5, first.GetProperty("stats").GetProperty("effortPercent").GetDouble());
        Assert.Equal("addr.rig", first.GetProperty("stats").GetProperty("bestShare").GetProperty("worker").GetString());
        Assert.Equal(1, first.GetProperty("stats").GetProperty("rejectedByReason").GetProperty("Duplicate").GetInt64());
    }

    [Fact]
    public async Task GIVEN_GetPool_WHEN_PoolExists_THEN_ReturnsThatPool()
    {
        _overview.FindPool("solo").Returns(Pool());

        var (status, body) = await GetAsync("/api/pools/solo");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3333, body.GetProperty("data").GetProperty("port").GetInt32());
    }

    [Fact]
    public async Task GIVEN_GetPool_WHEN_PoolIsUnknown_THEN_ReturnsStandardNotFoundError()
    {
        var (status, body) = await GetAsync("/api/pools/ghost");

        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertError(body, ApiError.NotFoundCode);
        Assert.Contains("ghost", body.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task GIVEN_GetMiner_WHEN_MinerHasShares_THEN_ReturnsMinerWithWorkers()
    {
        _statistics.GetMiner("solo", "bc1qalicealicealice").Returns(new MinerSnapshot("bc1qalicealicealice", Stats(), [new WorkerSnapshot("rig1", Stats())]));

        var (status, body) = await GetAsync("/api/pools/solo/miners/bc1qalicealicealice");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("rig1", body.GetProperty("data").GetProperty("workers")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GIVEN_GetMiner_WHEN_MinerIsUnknown_THEN_ReturnsStandardNotFoundError()
    {
        var (status, body) = await GetAsync("/api/pools/solo/miners/bc1qnobodynobodynobody");

        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertError(body, ApiError.NotFoundCode);
    }

    [Theory]
    [InlineData("%3Cscript%3Ealert(1)%3C%2Fscript%3E")]
    [InlineData("bc1q%27%3B%20DROP%20TABLE%20blocks")]
    [InlineData("short")]
    public async Task GIVEN_GetMiner_WHEN_AddressIsNotAddressShaped_THEN_ReturnsBadRequestWithoutEchoingOrLookingUp(string address)
    {
        var (status, body) = await GetAsync($"/api/pools/solo/miners/{address}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertError(body, ApiError.InvalidRequestCode);
        Assert.DoesNotContain("script", body.ToString());
        Assert.DoesNotContain("DROP", body.ToString());
        _statistics.DidNotReceiveWithAnyArgs().GetMiner(default!, default!);
    }

    [Fact]
    public async Task GIVEN_GetBlocks_WHEN_NoCountGiven_THEN_UsesConfiguredDefault()
    {
        var block = new FoundBlock("solo", PayoutScheme.Solo, 850_001, "00ab", new WorkerIdentity("addr", "rig"), 312_500_000, [new PayoutEntry("addr", 312_500_000)], 87.5, Now, true, null) { MinersPaid = 1 };
        _blocks.GetRecentAsync(25, Arg.Any<CancellationToken>()).Returns([block]);

        var (status, body) = await GetAsync("/api/blocks");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(850_001, body.GetProperty("data")[0].GetProperty("height").GetInt32());
        Assert.Equal(1, body.GetProperty("data")[0].GetProperty("minersPaid").GetInt32());
        Assert.Equal(312_500_000, body.GetProperty("data")[0].GetProperty("payouts")[0].GetProperty("satoshis").GetInt64());
    }

    [Fact]
    public async Task GIVEN_GetBlocks_WHEN_CountGiven_THEN_ThatManyAreRequested()
    {
        _blocks.GetRecentAsync(3, Arg.Any<CancellationToken>()).Returns([]);

        var (status, _) = await GetAsync("/api/blocks?count=3");

        Assert.Equal(HttpStatusCode.OK, status);
        await _blocks.Received(1).GetRecentAsync(3, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task GIVEN_GetBlocks_WHEN_CountIsOutOfRange_THEN_ReturnsStandardBadRequestError(int count)
    {
        var (status, body) = await GetAsync($"/api/blocks?count={count}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertError(body, ApiError.InvalidRequestCode);
    }

    [Fact]
    public async Task GIVEN_GetNetwork_WHEN_StatsAvailable_THEN_ReturnsThem()
    {
        _networkStats.Current.Returns(new NetworkStats("test4", 100, 100, 5.5, 1e9, "00", false, 1, true, 600, Now));

        var (status, body) = await GetAsync("/api/network");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("test4", body.GetProperty("data").GetProperty("chain").GetString());
        Assert.True(body.GetProperty("data").GetProperty("isSynced").GetBoolean());
    }

    [Fact]
    public async Task GIVEN_GetNetwork_WHEN_NodeHasNotAnswered_THEN_ReturnsStandardUnavailableError()
    {
        _networkStats.Current.Returns((NetworkStats?)null);

        var (status, body) = await GetAsync("/api/network");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        AssertError(body, "node_unavailable");
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_RouteDoesNotExist_THEN_ReturnsStandardNotFoundError()
    {
        var (status, body) = await GetAsync("/api/no/such/thing");

        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertError(body, ApiError.NotFoundCode);
    }

    [Fact]
    public async Task GIVEN_Api_WHEN_HandlerThrows_THEN_ReturnsStandardInternalErrorWithoutLeakingDetails()
    {
        _overview.GetPools().Throws(new InvalidOperationException("secret database password"));

        var (status, body) = await GetAsync("/api/pools");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        AssertError(body, ApiError.InternalErrorCode);
        Assert.DoesNotContain("secret", body.ToString());
    }

    private const string LongAddress = "bc1qalicealicealicealicealicealice";
    private const string LongAddressMasked = "bc1qalicea…ealice";

    [Fact]
    public async Task GIVEN_GetBlocks_WHEN_BlockHasAddresses_THEN_FinderAndPayoutAddressesAreShortened()
    {
        var block = new FoundBlock("solo", PayoutScheme.Solo, 850_001, "00ab", new WorkerIdentity(LongAddress, "rig"), 312_500_000, [new PayoutEntry(LongAddress, 312_500_000)], 87.5, Now, true, null) { MinersPaid = 1 };
        _blocks.GetRecentAsync(25, Arg.Any<CancellationToken>()).Returns([block]);

        var (_, body) = await GetAsync("/api/blocks");

        var data = body.GetProperty("data")[0];
        Assert.DoesNotContain(LongAddress, body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(LongAddressMasked, data.GetProperty("worker").GetProperty("address").GetString());
        Assert.Equal($"{LongAddressMasked}.rig", data.GetProperty("worker").GetProperty("fullName").GetString());
        Assert.Equal(LongAddressMasked, data.GetProperty("payouts")[0].GetProperty("address").GetString());
        Assert.Equal(312_500_000, data.GetProperty("payouts")[0].GetProperty("satoshis").GetInt64());
    }

    [Theory]
    [InlineData("/api/pools")]
    [InlineData("/api/pools/solo")]
    public async Task GIVEN_PoolEndpoints_WHEN_PoolHasABestShare_THEN_ItsWorkersAddressIsShortened(string path)
    {
        var pool = Pool() with { Stats = Stats() with { BestShare = new BestShare(900, $"{LongAddress}.rig", Now) } };
        _overview.GetPools().Returns([pool]);
        _overview.FindPool("solo").Returns(pool);

        var (status, body) = await GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain(LongAddress, body.GetRawText(), StringComparison.Ordinal);
        Assert.Contains($"{LongAddressMasked}.rig", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GIVEN_GetPool_WHEN_PoolHasNoBestShareYet_THEN_StillReturnsThePool()
    {
        _overview.FindPool("solo").Returns(Pool() with { Stats = Stats() with { BestShare = null } });

        var (status, body) = await GetAsync("/api/pools/solo");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("stats").GetProperty("bestShare").ValueKind);
    }

    [Theory]
    [InlineData("bc1qalicealicealicealicealicealice.rig.one", "bc1qalicea…ealice.rig.one")]
    [InlineData("bc1qalicealicealicealicealicealice", "bc1qalicea…ealice")]
    [InlineData("short.rig", "short.rig")]
    public void GIVEN_MaskWorker_WHEN_NameGiven_THEN_OnlyTheAddressIsShortened(string fullName, string expected)
    {
        Assert.Equal(expected, AddressMasking.MaskWorker(fullName));
    }
}
