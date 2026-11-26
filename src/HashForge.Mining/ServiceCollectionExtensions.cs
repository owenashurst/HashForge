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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HashForge.Mining;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mining engine. The host must also register an <see cref="IHotStateStore"/>,
    /// <see cref="IBlockRepository"/>, <see cref="IPoolHistoryRepository"/>, <see cref="IPoolDefinitionStore"/> and <see cref="IStoppedPoolStore"/>.
    /// </summary>
    public static IServiceCollection AddHashForgeMining(this IServiceCollection services)
    {
        services.AddOptions<MiningOptions>().BindConfiguration(MiningOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MiningOptions>, MiningOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IPoolRegistry, PoolRegistry>();
        services.AddSingleton<IPoolCatalogue, PoolCatalogue>();
        services.AddSingleton<IPoolProvisioner, PoolProvisioner>();
        services.AddSingleton<INetworkStatsStore, NetworkStatsStore>();
        services.AddSingleton<IAddressScriptResolver, AddressScriptResolver>();
        services.AddSingleton<IPayoutSchemeFactory, PayoutSchemeFactory>();
        services.AddSingleton<IJobFactory, JobFactory>();
        services.AddSingleton<IShareValidator, ShareValidator>();
        services.AddSingleton<IShareProcessor, ShareProcessor>();
        services.AddSingleton<IBlockCandidateSubmitter, ReliableBlockSubmitter>();
        services.AddSingleton<IFoundBlockRecorder, FoundBlockRecorder>();
        services.AddSingleton<IHotStateCoordinator, HotStateCoordinator>();

        services.AddSingleton<JobManager>();
        services.AddSingleton<IJobSource>(sp => sp.GetRequiredService<JobManager>());
        services.AddSingleton<ITemplateSink>(sp => sp.GetRequiredService<JobManager>());

        services.AddOptions<NotificationOptions>().BindConfiguration(NotificationOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddHttpClient(DiscordWebhookChannel.HttpClientName);
        services.AddSingleton<INotificationOutbox, NotificationOutbox>();
        services.AddSingleton<INotificationChannel, DiscordWebhookChannel>();
        services.AddSingleton<PoolEventNotifier>();
        services.AddSingleton<IBestShareListener>(sp => sp.GetRequiredService<PoolEventNotifier>());
        services.AddSingleton<IBlockObserver>(sp => sp.GetRequiredService<PoolEventNotifier>());

        services.AddSingleton<IStatisticsStore, StatisticsStore>();
        services.AddSingleton<IStatisticsReader, StatisticsReader>();
        services.AddObserver<StatisticsRecorder>();
        services.AddSingleton<IStatePersistable, StatisticsState>();

        services.AddSingleton<IPayoutSchemeRegistry, PayoutSchemeRegistry>();
        services.AddSingleton<IPayoutPlanSource, PayoutPlanSource>();
        services.AddObserver<PayoutRecorder>();
        services.AddSingleton<IStatePersistable, PayoutState>();

        services.AddSingleton<INodeReadiness, NodeReadiness>();
        services.AddSingleton<INetworkDifficulty, NetworkDifficulty>();
        services.AddSingleton<ITemplateRefresher, TemplateRefresher>();
        services.AddSingleton<IPeriodicJob, ChainTipPoller>();
        services.AddSingleton<IPeriodicJob, NetworkStatsJob>();
        services.AddSingleton<IPeriodicJob, HotStateSnapshotJob>();
        services.AddSingleton<IPeriodicJob, PoolHistorySampler>();

        services.AddSingleton<IPeriodicJob, BlockStatusJob>();
        services.AddSingleton<MinerHistoryStore>();
        services.AddSingleton<IMinerHistory>(sp => sp.GetRequiredService<MinerHistoryStore>());
        services.AddSingleton<IStatePersistable, MinerHistoryState>();
        services.AddSingleton<IPeriodicJob, MinerHistorySampler>();

        // Order matters: state is restored first, then pools are registered, then the background jobs start.
        services.AddHostedService<HotStateLifecycleService>();
        services.AddHostedService<PoolBootstrapService>();
        services.AddHostedService<PeriodicJobService>();
        services.AddHostedService<NotificationDispatcher>();
        services.AddHostedService<BlockNotificationListener>();
        return services;
    }

    /// <summary>Registers one singleton as both a share observer and a block observer.</summary>
    private static void AddObserver<T>(this IServiceCollection services)
        where T : class, IShareObserver, IBlockObserver
    {
        services.AddSingleton<T>();
        services.AddSingleton<IShareObserver>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IBlockObserver>(sp => sp.GetRequiredService<T>());
    }
}