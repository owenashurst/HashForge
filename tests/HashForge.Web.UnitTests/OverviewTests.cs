using System.Net;
using HashForge.Domain;
using HashForge.Mining.Pools;
using HashForge.Mining.Statistics;
using HashForge.Stratum;
using HashForge.Stratum.Sessions;
using HashForge.Stratum.Transport;
using HashForge.Web.Admin;
using HashForge.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HashForge.Web.UnitTests;

public sealed class OverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IPoolRegistry _registry = Substitute.For<IPoolRegistry>();
    private readonly IStatisticsReader _statistics = Substitute.For<IStatisticsReader>();
    private readonly ISessionRegistry _sessions = Substitute.For<ISessionRegistry>();

    private static PoolDefinition Pool(string name, int port) =>
        new() { Name = name, Scheme = PayoutScheme.Pplns, Port = port, FeeAddress = "fee" };

    private void AddPool(string name, int port, long accepted, long rejected, double? bestDifficulty)
    {
        var pool = Pool(name, port);
        var best = bestDifficulty is { } difficulty ? new BestShare(difficulty, $"{name}-worker", Now) : null;
        var stats = new StatsSnapshot(accepted, rejected, new Dictionary<ShareOutcome, long>(), 0, 0, 0, best, null);
        _registry.Find(name).Returns(pool);
        _statistics.GetPool(pool).Returns(new PoolSnapshot(name, pool.Scheme, port, stats, 1, 1));
        _pools.Add(pool);
    }

    private readonly List<PoolDefinition> _pools = [];

    private PoolOverview Overview()
    {
        _registry.Pools.Returns(_pools);
        return new PoolOverview(_registry, _statistics);
    }

    private static StratumSession Session(string ip, byte id, string poolName, DateTimeOffset connectedAt)
    {
        var connection = Substitute.For<IStratumConnection>();
        connection.RemoteAddress.Returns(IPAddress.Parse(ip));
        return new StratumSession(
            Pool(poolName, 1), new MinerChannel(connection, TimeSpan.FromSeconds(30)), [0, 0, 0, id], new VarDiffController(new StratumOptions(), Now), connectedAt, TimeProvider.System);
    }

    [Fact]
    public void GIVEN_GetPools_WHEN_PoolsAreRunning_THEN_ReturnsSnapshotPerPool()
    {
        AddPool("solo", 3333, 10, 1, 500);
        AddPool("pplns", 3334, 20, 2, 900);

        Assert.Equal(["solo", "pplns"], Overview().GetPools().Select(p => p.Name));
    }

    [Fact]
    public void GIVEN_FindPool_WHEN_NameIsKnownOrUnknown_THEN_ReturnsSnapshotOrNull()
    {
        AddPool("solo", 3333, 10, 1, 500);
        var overview = Overview();

        Assert.Equal(3333, overview.FindPool("solo")!.Port);
        Assert.Null(overview.FindPool("missing"));
    }

    [Fact]
    public void GIVEN_GetTotals_WHEN_SeveralPools_THEN_SumsAcceptedAndRejected()
    {
        AddPool("solo", 3333, 10, 1, 500);
        AddPool("pplns", 3334, 20, 2, 900);

        Assert.Equal(new ShareTotals(30, 3), Overview().GetTotals());
    }

    [Fact]
    public void GIVEN_GetBestShare_WHEN_SeveralPools_THEN_ReturnsHighestWithItsWorker()
    {
        AddPool("solo", 3333, 10, 1, 500);
        AddPool("pplns", 3334, 20, 2, 900);
        AddPool("prop", 3335, 0, 0, null);

        Assert.Equal(new BestShare(900, "pplns-worker", Now), Overview().GetBestShare());
    }

    [Fact]
    public void GIVEN_GetBestShare_WHEN_NoSharesAnywhere_THEN_ReturnsNull()
    {
        AddPool("prop", 3335, 0, 0, null);

        Assert.Null(Overview().GetBestShare());
        Assert.Equal(new ShareTotals(0, 0), new PoolOverview(Substitute.For<IPoolRegistry>(), _statistics).GetTotals());
    }

    [Fact]
    public void GIVEN_GetSessions_WHEN_MinersConnected_THEN_OrderedByPoolThenConnectionTime()
    {
        // Built first: creating a substitute inside another substitute's Returns() call confuses NSubstitute.
        StratumSession[] connected =
        [
            Session("10.0.0.3", 3, "solo", Now),
            Session("10.0.0.2", 2, "pplns", Now.AddMinutes(1)),
            Session("10.0.0.1", 1, "pplns", Now),
        ];
        _sessions.Sessions.Returns(connected);

        var sessions = new ConnectedMiners(_sessions).GetSessions();

        Assert.Equal(["10.0.0.1", "10.0.0.2", "10.0.0.3"], sessions.Select(s => s.RemoteAddress.ToString()));
    }

    [Fact]
    public void GIVEN_GetAddresses_WHEN_SeveralConnectionsShareAnIp_THEN_EachIpListedOnceWithCount()
    {
        StratumSession[] connected =
        [
            Session("10.0.0.9", 1, "solo", Now),
            Session("10.0.0.5", 2, "solo", Now),
            Session("10.0.0.5", 3, "pplns", Now),
        ];
        _sessions.Sessions.Returns(connected);

        var addresses = new ConnectedMiners(_sessions).GetAddresses();

        Assert.Equal(
            [new ConnectedAddress(IPAddress.Parse("10.0.0.5"), 2), new ConnectedAddress(IPAddress.Parse("10.0.0.9"), 1)],
            addresses);
    }

    [Fact]
    public void GIVEN_AddHashForgeWeb_WHEN_Resolved_THEN_ServicesAndOptionsAreAvailable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Web:StratumHost"] = "pool.example", ["Admin:AllowedNetworks:0"] = "10.0.0.0/8" })
            .Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(_registry)
            .AddSingleton(_statistics)
            .AddSingleton(_sessions)
            .AddSingleton(Substitute.For<HashForge.Mining.History.IBlockRepository>())
            .AddSingleton(Substitute.For<HashForge.Mining.Network.INetworkStatsStore>())
            .AddLogging()
            .AddHashForgeWeb()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.IsType<PoolOverview>(provider.GetRequiredService<IPoolOverview>());
        Assert.IsType<ConnectedMiners>(provider.GetRequiredService<IConnectedMiners>());
        Assert.Equal(5, provider.GetServices<HashForge.Web.Api.IApiEndpoint>().Count());
        Assert.Equal("pool.example", provider.GetRequiredService<IOptions<WebOptions>>().Value.StratumHost);
        Assert.Equal("HashForge", provider.GetRequiredService<IOptions<HashForge.Web.Branding.BrandingOptions>>().Value.Name);
        Assert.True(provider.GetRequiredService<IAdminAccessPolicy>().IsAllowed(IPAddress.Parse("10.1.2.3")));
        Assert.False(provider.GetRequiredService<IAdminAccessPolicy>().IsAllowed(IPAddress.Parse("192.168.0.1")));
    }

    [Fact]
    public void GIVEN_AddHashForgeWeb_WHEN_AdminNetworkIsInvalid_THEN_OptionsValidationFails()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:AllowedNetworks:0"] = "not-a-network" })
            .Build();
        using var provider = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddHashForgeWeb().BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AdminOptions>>().Value);
    }
}
