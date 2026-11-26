using HashForge.Bitcoin.Notifications;
using HashForge.Bitcoin.Rpc;
using HashForge.Mining.History;
using HashForge.Mining.Hosting;
using HashForge.Mining.Jobs;
using HashForge.Mining.Network;
using HashForge.Mining.Notifications;
using HashForge.Mining.Payouts;
using HashForge.Mining.Pools;
using HashForge.Mining.Shares;
using HashForge.Mining.State;
using HashForge.Mining.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HashForge.Mining.UnitTests;

public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddSingleton(Substitute.For<IBlockTemplateSource>())
            .AddSingleton(Substitute.For<IBlockSubmitter>())
            .AddSingleton(Substitute.For<IChainInfoSource>())
            .AddSingleton(Substitute.For<IChainTipSource>())
            .AddSingleton(Substitute.For<IAddressValidator>())
            .AddSingleton(Substitute.For<IBlockHeaderSource>())
            .AddSingleton(Substitute.For<IBlockNotificationSource>())
            .AddSingleton(Substitute.For<IHotStateStore>())
            .AddSingleton(Substitute.For<IBlockRepository>())
            .AddSingleton(Substitute.For<IPoolHistoryRepository>())
            .AddSingleton(Substitute.For<IPoolDefinitionStore>())
            .AddSingleton(Substitute.For<IStoppedPoolStore>())
            .AddHashForgeMining()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_ContainerIsBuilt_THEN_EveryServiceCanBeConstructed()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IShareProcessor>());
        Assert.NotNull(provider.GetRequiredService<IPoolProvisioner>());
        Assert.IsType<ReliableBlockSubmitter>(provider.GetRequiredService<IBlockCandidateSubmitter>());
        Assert.IsType<FoundBlockRecorder>(provider.GetRequiredService<IFoundBlockRecorder>());
        Assert.IsType<TemplateRefresher>(provider.GetRequiredService<ITemplateRefresher>());
        Assert.IsType<NodeReadiness>(provider.GetRequiredService<INodeReadiness>());
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_Resolved_THEN_HostedServicesStartInTheRequiredOrder()
    {
        using var provider = BuildProvider();

        var hosted = provider.GetServices<IHostedService>().Select(s => s.GetType()).ToList();

        // State must be restored before pools are registered, and pools before the background jobs run.
        Assert.Equal(
            [
                typeof(HotStateLifecycleService), typeof(PoolBootstrapService), typeof(PeriodicJobService),
                typeof(NotificationDispatcher), typeof(BlockNotificationListener),
            ],
            hosted);
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_Resolved_THEN_ObserversAreSharedSingletonsOverOneStore()
    {
        using var provider = BuildProvider();

        var statistics = provider.GetRequiredService<StatisticsRecorder>();
        var payouts = provider.GetRequiredService<PayoutRecorder>();
        var notifier = provider.GetRequiredService<PoolEventNotifier>();

        Assert.Equal([statistics, payouts], provider.GetServices<IShareObserver>());
        Assert.Equal([notifier, statistics, payouts], provider.GetServices<IBlockObserver>());
        Assert.Equal([notifier], provider.GetServices<IBestShareListener>());
        Assert.Equal(["statistics", "payouts", "miner-history"], provider.GetServices<IStatePersistable>().Select(s => s.StateKey));
        Assert.IsType<StatisticsReader>(provider.GetRequiredService<IStatisticsReader>());
        Assert.Same(provider.GetRequiredService<MinerHistoryStore>(), provider.GetRequiredService<IMinerHistory>());
        Assert.IsType<NetworkDifficulty>(provider.GetRequiredService<INetworkDifficulty>());
        Assert.IsType<PayoutPlanSource>(provider.GetRequiredService<IPayoutPlanSource>());
        Assert.Same(provider.GetRequiredService<IStatisticsStore>(), provider.GetRequiredService<IStatisticsStore>());
        Assert.Same(provider.GetRequiredService<IPayoutSchemeRegistry>(), provider.GetRequiredService<IPayoutSchemeRegistry>());
        Assert.Same(provider.GetRequiredService<IJobSource>(), provider.GetRequiredService<ITemplateSink>());
        Assert.IsType<DiscordWebhookChannel>(Assert.Single(provider.GetServices<INotificationChannel>()));
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_Resolved_THEN_AllBackgroundJobsAreRegistered()
    {
        using var provider = BuildProvider();

        var jobs = provider.GetServices<IPeriodicJob>().Select(j => j.Name);

        Assert.Equal(["chain-tip-poller", "network-stats", "state-snapshot", "pool-history", "block-status", "miner-history"], jobs);
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_PoolsConfigured_THEN_OptionsBindPoolDefinitions()
    {
        using var provider = BuildProvider(new()
        {
            ["Mining:Pools:0:Name"] = "bonus",
            ["Mining:Pools:0:Scheme"] = "FinderBonus",
            ["Mining:Pools:0:Port"] = "3336",
            ["Mining:Pools:0:FeeAddress"] = "bc1qfee",
            ["Mining:Pools:0:FeePercent"] = "1.5",
            ["Mining:Pools:0:FinderBonusSatoshis"] = "50000000",
            ["Mining:CoinbaseTag"] = "/mine/",
        });

        var options = provider.GetRequiredService<IOptions<MiningOptions>>().Value;

        var pool = Assert.Single(options.Pools);
        Assert.Equal(
            ("bonus", Domain.PayoutScheme.FinderBonus, 3336, "bc1qfee", 1.5m, 50_000_000L),
            (pool.Name, pool.Scheme, pool.Port, pool.FeeAddress, pool.FeePercent, pool.FinderBonusSatoshis));
        Assert.Equal("/mine/", options.CoinbaseTag);
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_ConfigurationIsInvalid_THEN_OptionsValidationFails()
    {
        using var provider = BuildProvider(new() { ["Mining:Extranonce2Size"] = "99" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MiningOptions>>().Value);
    }

    [Fact]
    public void GIVEN_AddHashForgeMining_WHEN_DiscordUrlIsNotHttps_THEN_OptionsValidationFails()
    {
        using var provider = BuildProvider(new() { ["Notifications:DiscordWebhookUrl"] = "http://insecure.example/hook" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NotificationOptions>>().Value);
    }
}
