using HashForge.Domain;
using HashForge.Mining.History;
using HashForge.Mining.Shares;
using HashForge.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HashForge.Persistence.UnitTests;

/// <summary>Exercises the repositories against an in-memory relational database.</summary>
public sealed class DatabaseTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestContextFactory _factory;

    public DatabaseTests()
    {
        _connection.Open();
        _factory = new TestContextFactory(new DbContextOptionsBuilder<HashForgeDbContext>().UseSqlite(_connection).Options);
        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestContextFactory(DbContextOptions<HashForgeDbContext> options) : IDbContextFactory<HashForgeDbContext>
    {
        public HashForgeDbContext CreateDbContext() => new(options);
    }

    private static FoundBlock Block(int height, DateTimeOffset foundAt, bool accepted = true) => new(
        "pplns",
        PayoutScheme.Pplns,
        height,
        $"hash-{height}",
        new WorkerIdentity("bc1qfinder", "rig1"),
        312_500_000,
        [new PayoutEntry("bc1qa", 200_000_000), new PayoutEntry("bc1qb", 112_500_000)],
        87.5,
        foundAt,
        accepted,
        accepted ? null : "high-hash")
    {
        MinersPaid = 2,
    };

    private static PoolDefinition Pool(string name = "prop", int port = 3335) => new()
    {
        Name = name,
        Scheme = PayoutScheme.Prop,
        Port = port,
        FeeAddress = "bc1qfee",
        FeePercent = 1.25m,
        PplnsWindowMultiplier = 3,
        FinderBonusSatoshis = 50_000_000,
        MaxCoinbaseOutputs = 100,
        MinimumPayoutSatoshis = 5_000,
    };

    [Fact]
    public async Task GIVEN_BlockAddAsync_WHEN_BlockIsStored_THEN_GetRecentReturnsItWithPayoutsInOrder()
    {
        var repository = new EfBlockRepository(_factory);
        var block = Block(850_000, Now);

        await repository.AddAsync(block, CancellationToken.None);
        var stored = Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None));

        Assert.Equal(block.Payouts, stored.Payouts);
        Assert.Equal(2, stored.MinersPaid);
        Assert.Equal(block with { Payouts = stored.Payouts }, stored);
    }

    [Fact]
    public async Task GIVEN_BlockAddAsync_WHEN_BlockWasRejected_THEN_RejectReasonIsKept()
    {
        var repository = new EfBlockRepository(_factory);

        await repository.AddAsync(Block(1, Now, accepted: false), CancellationToken.None);
        var stored = Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None));

        Assert.False(stored.Accepted);
        Assert.Equal("high-hash", stored.RejectReason);
    }

    [Fact]
    public async Task GIVEN_GetRecentAsync_WHEN_MoreBlocksThanRequested_THEN_ReturnsNewestFirstUpToCount()
    {
        var repository = new EfBlockRepository(_factory);
        await repository.AddAsync(Block(1, Now), CancellationToken.None);
        await repository.AddAsync(Block(3, Now.AddHours(2)), CancellationToken.None);
        await repository.AddAsync(Block(2, Now.AddHours(1)), CancellationToken.None);

        var recent = await repository.GetRecentAsync(2, CancellationToken.None);

        Assert.Equal([3, 2], recent.Select(b => b.Height));
    }

    [Fact]
    public async Task GIVEN_HistoryGetSinceAsync_WHEN_SamplesSpanPoolsAndTimes_THEN_ReturnsMatchingSamplesOldestFirst()
    {
        var repository = new EfPoolHistoryRepository(_factory);
        var kept1 = new PoolHistorySample("solo", Now.AddMinutes(5), 2e12, 3, 4);
        var kept2 = new PoolHistorySample("solo", Now.AddMinutes(10), 3e12, 5, 6);
        await repository.AddAsync(
            [kept2, new PoolHistorySample("solo", Now.AddMinutes(-5), 1e12, 1, 1), kept1, new PoolHistorySample("prop", Now.AddMinutes(5), 9e12, 9, 9)],
            CancellationToken.None);

        var samples = await repository.GetSinceAsync("solo", Now, CancellationToken.None);

        Assert.Equal([kept1, kept2], samples);
    }

    [Fact]
    public async Task GIVEN_PoolSaveAsync_WHEN_PoolIsNew_THEN_GetAllReturnsItUnchanged()
    {
        var store = new EfPoolDefinitionStore(_factory);
        var pool = Pool();

        await store.SaveAsync(pool, CancellationToken.None);

        Assert.Equal(pool, Assert.Single(await store.GetAllAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task GIVEN_PoolSaveAsync_WHEN_PoolAlreadyExists_THEN_ItIsUpdatedNotDuplicated()
    {
        var store = new EfPoolDefinitionStore(_factory);
        await store.SaveAsync(Pool(), CancellationToken.None);
        var changed = Pool() with { Port = 4444, FeePercent = 2m, FeeAddress = "bc1qother" };

        await store.SaveAsync(changed, CancellationToken.None);

        Assert.Equal(changed, Assert.Single(await store.GetAllAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task GIVEN_PoolGetAllAsync_WHEN_SeveralStored_THEN_OrderedByPort()
    {
        var store = new EfPoolDefinitionStore(_factory);
        await store.SaveAsync(Pool("b", 20), CancellationToken.None);
        await store.SaveAsync(Pool("a", 10), CancellationToken.None);

        Assert.Equal(["a", "b"], (await store.GetAllAsync(CancellationToken.None)).Select(p => p.Name));
    }

    [Fact]
    public async Task GIVEN_PoolDeleteAsync_WHEN_PoolExists_THEN_OnlyThatPoolIsRemoved()
    {
        var store = new EfPoolDefinitionStore(_factory);
        await store.SaveAsync(Pool("a", 10), CancellationToken.None);
        await store.SaveAsync(Pool("b", 20), CancellationToken.None);

        await store.DeleteAsync("a", CancellationToken.None);
        await store.DeleteAsync("missing", CancellationToken.None);

        Assert.Equal("b", Assert.Single(await store.GetAllAsync(CancellationToken.None)).Name);
    }

    [Fact]
    public async Task GIVEN_StoppedAddAsync_WHEN_PoolsAreStopped_THEN_GetAllListsEachOnce()
    {
        var store = new EfStoppedPoolStore(_factory);

        await store.AddAsync("solo", CancellationToken.None);
        await store.AddAsync("bonus", CancellationToken.None);
        await store.AddAsync("solo", CancellationToken.None);

        Assert.Equal(["bonus", "solo"], await store.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GIVEN_StoppedRemoveAsync_WHEN_PoolIsStartedAgain_THEN_OnlyThatPoolIsForgotten()
    {
        var store = new EfStoppedPoolStore(_factory);
        await store.AddAsync("solo", CancellationToken.None);
        await store.AddAsync("bonus", CancellationToken.None);

        await store.RemoveAsync("solo", CancellationToken.None);
        await store.RemoveAsync("missing", CancellationToken.None);

        Assert.Equal(["bonus"], await store.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GIVEN_MigrationServiceStartAsync_WHEN_HostStarts_THEN_DatabaseIsMigratedOnce()
    {
        var migrator = Substitute.For<IDatabaseMigrator>();
        var service = new DatabaseMigrationService(migrator, NullLogger<DatabaseMigrationService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        await migrator.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GIVEN_DesignTimeFactory_WHEN_ContextCreated_THEN_ModelMatchesMigrationsWithNoPendingChanges()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains("InitialCreate", context.Database.GetMigrations().First());
        Assert.Equal(5, context.Database.GetMigrations().Count());
    }

    [Fact]
    public async Task GIVEN_FindAsync_WHEN_BlockWithThatHashIsStored_THEN_ReturnsItWithItsPayouts()
    {
        var repository = new EfBlockRepository(_factory);
        await repository.AddAsync(Block(1, Now), CancellationToken.None);
        await repository.AddAsync(Block(2, Now.AddHours(1)), CancellationToken.None);

        var found = await repository.FindAsync("hash-2", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(2, found.Height);
        Assert.Equal([200_000_000, 112_500_000], found.Payouts.Select(p => p.Satoshis));
    }

    [Fact]
    public async Task GIVEN_FindAsync_WHEN_NoBlockHasThatHash_THEN_ReturnsNull()
    {
        var repository = new EfBlockRepository(_factory);
        await repository.AddAsync(Block(1, Now), CancellationToken.None);

        Assert.Null(await repository.FindAsync("hash-9", CancellationToken.None));
    }

    [Fact]
    public async Task GIVEN_GetRecentForPoolAsync_WHEN_SeveralPoolsHaveBlocks_THEN_ReturnsOnlyThatPoolsNewestFirstUpToCount()
    {
        var repository = new EfBlockRepository(_factory);
        await repository.AddAsync(Block(1, Now), CancellationToken.None);
        await repository.AddAsync(Block(2, Now.AddHours(1)) with { PoolName = "solo" }, CancellationToken.None);
        await repository.AddAsync(Block(3, Now.AddHours(2)), CancellationToken.None);
        await repository.AddAsync(Block(4, Now.AddHours(3)), CancellationToken.None);

        var recent = await repository.GetRecentForPoolAsync("pplns", 2, CancellationToken.None);

        Assert.Equal([4, 3], recent.Select(b => b.Height));
    }

    [Fact]
    public async Task GIVEN_UpdateChainStateAsync_WHEN_BlockIsOrphaned_THEN_ItReadsBackAsOrphaned()
    {
        var repository = new EfBlockRepository(_factory);
        await repository.AddAsync(Block(1, Now), CancellationToken.None);
        await repository.AddAsync(Block(2, Now.AddHours(1)), CancellationToken.None);

        await repository.UpdateChainStateAsync("hash-1", 0, true, CancellationToken.None);
        await repository.UpdateChainStateAsync("hash-2", 7, false, CancellationToken.None);

        var first = await repository.FindAsync("hash-1", CancellationToken.None);
        var second = await repository.FindAsync("hash-2", CancellationToken.None);
        Assert.Equal(BlockStatus.Orphaned, first!.Status);
        Assert.Equal(7, second!.Confirmations);
        Assert.Equal(BlockStatus.Accepted, second.Status);
    }

    [Fact]
    public async Task GIVEN_GetUnsettledAsync_WHEN_BlocksAreRejectedMatureOrTooOld_THEN_TheyAreLeftOut()
    {
        var repository = new EfBlockRepository(_factory);
        await repository.AddAsync(Block(50, Now), CancellationToken.None);
        await repository.AddAsync(Block(100, Now), CancellationToken.None);
        await repository.AddAsync(Block(101, Now, accepted: false), CancellationToken.None);
        await repository.AddAsync(Block(102, Now), CancellationToken.None);
        await repository.AddAsync(Block(103, Now), CancellationToken.None);
        await repository.UpdateChainStateAsync("hash-102", FoundBlock.MaturityConfirmations, false, CancellationToken.None);
        await repository.UpdateChainStateAsync("hash-103", 0, true, CancellationToken.None);

        var unsettled = await repository.GetUnsettledAsync(100, CancellationToken.None);

        Assert.Equal([100, 103], unsettled.Select(b => b.Height));
    }
}
