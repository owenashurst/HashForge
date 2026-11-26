using HashForge.Mining.History;
using HashForge.Mining.Pools;
using HashForge.Mining.State;
using HashForge.Persistence.Database;
using HashForge.Persistence.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HashForge.Persistence.UnitTests;

public sealed class RedisAndRegistrationTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisHotStateStore _store;

    public RedisAndRegistrationTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _store = new RedisHotStateStore(_redis, Options.Create(new PersistenceOptions { RedisKeyPrefix = "hf:" }));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .AddLogging()
            .AddHashForgePersistence()
            .BuildServiceProvider();

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Persistence:RedisConnectionString"] = "redis-host:6380,password=secret",
        ["Persistence:PostgresConnectionString"] = "Host=db;Database=hashforge;Username=u;Password=p",
    };

    [Fact]
    public async Task GIVEN_SaveAsync_WHEN_Called_THEN_StateIsWrittenUnderPrefixedKey()
    {
        await _store.SaveAsync("statistics", "{}", CancellationToken.None);

        var call = Assert.Single(_database.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IDatabase.StringSetAsync));
        Assert.Equal((RedisKey)"hf:statistics", call.GetArguments()[0]);
        Assert.Equal((RedisValue)"{}", call.GetArguments()[1]);
    }

    [Fact]
    public async Task GIVEN_LoadAsync_WHEN_KeyExists_THEN_ReturnsStoredState()
    {
        _database.StringGetAsync("hf:payouts", Arg.Any<CommandFlags>()).Returns((RedisValue)"saved");

        Assert.Equal("saved", await _store.LoadAsync("payouts", CancellationToken.None));
    }

    [Fact]
    public async Task GIVEN_LoadAsync_WHEN_KeyIsMissing_THEN_ReturnsNull()
    {
        _database.StringGetAsync("hf:payouts", Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        Assert.Null(await _store.LoadAsync("payouts", CancellationToken.None));
    }

    [Fact]
    public async Task GIVEN_SaveAndLoadAsync_WHEN_Cancelled_THEN_ThrowWithoutTouchingRedis()
    {
        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.SaveAsync("k", "v", cancelled));
        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.LoadAsync("k", cancelled));
        Assert.Empty(_database.ReceivedCalls());
    }

    [Fact]
    public void GIVEN_AddHashForgePersistence_WHEN_Resolved_THEN_StorageInterfacesMapToRedisAndPostgres()
    {
        using var provider = BuildProvider(ValidSettings());

        Assert.IsType<EfBlockRepository>(provider.GetRequiredService<IBlockRepository>());
        Assert.IsType<EfPoolHistoryRepository>(provider.GetRequiredService<IPoolHistoryRepository>());
        Assert.IsType<EfPoolDefinitionStore>(provider.GetRequiredService<IPoolDefinitionStore>());
        Assert.IsType<EfStoppedPoolStore>(provider.GetRequiredService<IStoppedPoolStore>());
        Assert.IsType<EfDatabaseMigrator>(provider.GetRequiredService<IDatabaseMigrator>());
        Assert.IsType<DatabaseMigrationService>(Assert.Single(provider.GetServices<IHostedService>()));
        using var context = provider.GetRequiredService<IDbContextFactory<HashForgeDbContext>>().CreateDbContext();
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Contains("Host=db", context.Database.GetConnectionString());
    }

    [Fact]
    public void GIVEN_AddHashForgePersistence_WHEN_Registered_THEN_HotStateStoreIsRedisBacked()
    {
        var services = new ServiceCollection().AddHashForgePersistence();

        Assert.Contains(services, d => d.ServiceType == typeof(IHotStateStore) && d.ImplementationType == typeof(RedisHotStateStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IConnectionMultiplexer) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void GIVEN_RedisConfiguration_WHEN_Built_THEN_UsesConfiguredEndpointAndKeepsRetrying()
    {
        using var provider = BuildProvider(ValidSettings());

        var configuration = ServiceCollectionExtensions.RedisConfiguration(provider);

        Assert.False(configuration.AbortOnConnectFail);
        Assert.Equal("secret", configuration.Password);
        Assert.Contains("redis-host:6380", configuration.ToString());
    }

    [Fact]
    public void GIVEN_AddHashForgePersistence_WHEN_ConnectionStringMissing_THEN_OptionsValidationFails()
    {
        var settings = ValidSettings();
        settings.Remove("Persistence:PostgresConnectionString");
        using var provider = BuildProvider(settings);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PersistenceOptions>>().Value);
    }
}
