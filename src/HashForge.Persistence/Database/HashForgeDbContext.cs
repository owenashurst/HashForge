using HashForge.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HashForge.Persistence.Database;

public sealed class HashForgeDbContext(DbContextOptions<HashForgeDbContext> options) : DbContext(options)
{
    private const int NameLength = 64;
    private const int AddressLength = 128;
    private const int HashLength = 64;

    public DbSet<BlockEntity> Blocks => Set<BlockEntity>();

    public DbSet<PoolHistoryEntity> PoolHistory => Set<PoolHistoryEntity>();

    public DbSet<PoolDefinitionEntity> Pools => Set<PoolDefinitionEntity>();

    public DbSet<StoppedPoolEntity> StoppedPools => Set<StoppedPoolEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Stored as Unix milliseconds so timestamps sort and compare identically on every database engine.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UnixMillisecondsConverter>();
        configurationBuilder.Properties<PayoutScheme>().HaveConversion<string>().HaveMaxLength(NameLength);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlockEntity>(block =>
        {
            block.Property(b => b.PoolName).HasMaxLength(NameLength);
            block.Property(b => b.Hash).HasMaxLength(HashLength);
            block.Property(b => b.WorkerAddress).HasMaxLength(AddressLength);
            block.Property(b => b.WorkerName).HasMaxLength(AddressLength);
            block.HasIndex(b => b.FoundAt);
            block.HasMany(b => b.Payouts).WithOne().HasForeignKey(p => p.BlockId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<BlockPayoutEntity>().Property(p => p.Address).HasMaxLength(AddressLength);
        modelBuilder.Entity<PoolHistoryEntity>(sample =>
        {
            sample.Property(s => s.PoolName).HasMaxLength(NameLength);
            sample.HasIndex(s => new { s.PoolName, s.Timestamp });
        });
        modelBuilder.Entity<PoolDefinitionEntity>(pool =>
        {
            pool.HasKey(p => p.Name);
            pool.Property(p => p.Name).HasMaxLength(NameLength);
            pool.Property(p => p.FeeAddress).HasMaxLength(AddressLength);
            pool.Property(p => p.FeePercent).HasPrecision(6, 3);
        });
        modelBuilder.Entity<StoppedPoolEntity>(stopped =>
        {
            stopped.HasKey(p => p.Name);
            stopped.Property(p => p.Name).HasMaxLength(NameLength);
        });
    }

    private sealed class UnixMillisecondsConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));
}
