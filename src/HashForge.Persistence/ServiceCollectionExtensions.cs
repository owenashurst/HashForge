using HashForge.Mining.History;
using HashForge.Mining.Pools;
using HashForge.Mining.State;
using HashForge.Persistence.Database;
using HashForge.Persistence.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HashForge.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Redis and PostgreSQL storage. Call this before registering the mining engine
    /// so the database is migrated before the pool starts using it.
    /// </summary>
    public static IServiceCollection AddHashForgePersistence(this IServiceCollection services)
    {
        services.AddOptions<PersistenceOptions>()
            .BindConfiguration(PersistenceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(RedisConfiguration(sp)));
        services.AddSingleton<IHotStateStore, RedisHotStateStore>();

        services.AddDbContextFactory<HashForgeDbContext>((sp, builder) =>
            builder.UseNpgsql(sp.GetRequiredService<IOptions<PersistenceOptions>>().Value.PostgresConnectionString));
        services.AddSingleton<IBlockRepository, EfBlockRepository>();
        services.AddSingleton<IPoolHistoryRepository, EfPoolHistoryRepository>();
        services.AddSingleton<IPoolDefinitionStore, EfPoolDefinitionStore>();
        services.AddSingleton<IStoppedPoolStore, EfStoppedPoolStore>();
        services.AddSingleton<IDatabaseMigrator, EfDatabaseMigrator>();
        services.AddHostedService<DatabaseMigrationService>();
        return services;
    }

    internal static ConfigurationOptions RedisConfiguration(IServiceProvider services)
    {
        var configuration = ConfigurationOptions.Parse(
            services.GetRequiredService<IOptions<PersistenceOptions>>().Value.RedisConnectionString);

        // Keep retrying in the background rather than failing startup when Redis is briefly unavailable.
        configuration.AbortOnConnectFail = false;
        return configuration;
    }
}
