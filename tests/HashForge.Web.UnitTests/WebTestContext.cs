using Bunit;
using HashForge.Domain;
using HashForge.Mining.History;
using HashForge.Mining.Jobs;
using HashForge.Mining.Network;
using HashForge.Mining.Pools;
using HashForge.Mining.Shares;
using HashForge.Mining.Statistics;
using HashForge.Web.Admin;
using HashForge.Web.Branding;
using HashForge.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HashForge.Web.UnitTests;

/// <summary>A bUnit context with every service the pages inject replaced by a substitute.</summary>
public abstract class WebTestContext : BunitContext
{
    protected static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    protected WebTestContext()
    {
        Blocks.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        Blocks.GetRecentForPoolAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        History.GetSinceAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        Overview.GetPools().Returns([]);
        Overview.GetTotals().Returns(new ShareTotals(0, 0));
        Statistics.GetMiners(Arg.Any<string>()).Returns([]);
        Miners.GetSessions().Returns([]);
        Miners.GetAddresses().Returns([]);
        Registry.Pools.Returns([]);
        Provisioner.GetPools().Returns([]);

        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton(Options.Create(WebOptions));
        Services.AddSingleton(Options.Create(Branding));
        Services.AddSingleton(Overview);
        Services.AddSingleton(NetworkStats);
        Services.AddSingleton(Blocks);
        Services.AddSingleton(History);
        Services.AddSingleton(Statistics);
        Services.AddSingleton(Miners);
        Services.AddSingleton(Provisioner);
        Services.AddSingleton(Registry);
        Services.AddSingleton(AdminAccess);
        Templates.Status.Returns(new TemplateStatus(null, DateTimeOffset.MinValue));
        Services.AddSingleton(Templates);
        Services.AddSingleton(MinerHistory);
        Services.AddSingleton(Options.Create(new HashForge.Mining.MiningOptions()));
        Services.AddSingleton<INetworkDifficulty>(new NetworkDifficulty(Templates, NetworkStats));
    }

    protected FakeTimeProvider Time { get; } = new(Now);

    protected WebOptions WebOptions { get; } = new() { StratumHost = "pool.example", RefreshSeconds = 5 };

    protected BrandingOptions Branding { get; } = new();

    protected IPoolOverview Overview { get; } = Substitute.For<IPoolOverview>();

    protected INetworkStatsStore NetworkStats { get; } = Substitute.For<INetworkStatsStore>();

    protected IBlockRepository Blocks { get; } = Substitute.For<IBlockRepository>();

    protected IPoolHistoryRepository History { get; } = Substitute.For<IPoolHistoryRepository>();

    protected IStatisticsReader Statistics { get; } = Substitute.For<IStatisticsReader>();

    protected IConnectedMiners Miners { get; } = Substitute.For<IConnectedMiners>();

    protected IPoolProvisioner Provisioner { get; } = Substitute.For<IPoolProvisioner>();

    protected IPoolRegistry Registry { get; } = Substitute.For<IPoolRegistry>();

    protected ITemplateRefresher Templates { get; } = Substitute.For<ITemplateRefresher>();

    protected IMinerHistory MinerHistory { get; } = Substitute.For<IMinerHistory>();

    protected IAdminAccessPolicy AdminAccess { get; } = Substitute.For<IAdminAccessPolicy>();

    protected static StatsSnapshot Stats(long accepted = 120, long rejected = 3, double hashrate = 2.5e12, double effort = 42.5, BestShare? best = null) =>
        new(accepted, rejected, new Dictionary<ShareOutcome, long>(), 1000, effort, hashrate, best ?? new BestShare(8.1e9, "bc1qalice.rig1", Now), Now.AddSeconds(-30));

    protected static PoolSnapshot PoolSnapshot(string name = "pplns", PayoutScheme scheme = PayoutScheme.Pplns, int port = 3334) =>
        new(name, scheme, port, Stats(), MinerCount: 7, WorkerCount: 9);

    protected static PoolDefinition PoolDefinition(string name = "pplns", PayoutScheme scheme = PayoutScheme.Pplns, int port = 3334) =>
        new() { Name = name, Scheme = scheme, Port = port, FeeAddress = "bc1qfee", FeePercent = 1.5m };

    protected static NetworkStats Network(bool synced = true) =>
        new("main", 850_000, synced ? 850_000 : 900_000, 9.2e13, 6.5e20, "00ab", !synced, synced ? 1 : 0.5, true, 12_000_000_000, Now.AddSeconds(-10)) { LastBlockAt = Now.AddMinutes(-4) };

    protected static FoundBlock Block(bool accepted = true) => new(
        "pplns", PayoutScheme.Pplns, 850_001, new string('a', 64), new WorkerIdentity("bc1qalicealicealicealice", "rig1"),
        312_500_000, [new PayoutEntry("a", 1), new PayoutEntry("b", 2)], 87.5, Now.AddMinutes(-3), accepted, accepted ? null : "high-hash") { MinersPaid = 34 };
}
