using System.Collections.Concurrent;
using HashForge.Domain;
using HashForge.Mining.Pools;

namespace HashForge.Mining.Payouts;

public sealed class PayoutSchemeRegistry : IPayoutSchemeRegistry, IDisposable
{
    // Lazy guarantees each pool's scheme is created exactly once, even when many miners arrive together.
    private readonly ConcurrentDictionary<string, Lazy<IPayoutScheme>> _schemes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ShareBucket>> _pendingHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPoolRegistry _pools;
    private readonly IPayoutSchemeFactory _factory;

    public PayoutSchemeRegistry(IPoolRegistry pools, IPayoutSchemeFactory factory)
    {
        _pools = pools;
        _factory = factory;
        _pools.PoolRemoved += OnPoolRemoved;
    }

    public void Dispose() => _pools.PoolRemoved -= OnPoolRemoved;

    public IPayoutScheme SchemeFor(PoolDefinition pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        return _schemes.GetOrAdd(pool.Name, _ => new Lazy<IPayoutScheme>(() => Create(pool))).Value;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ShareBucket>> ExportHistory()
    {
        // History still waiting for its pool to start is carried forward so a snapshot never discards it.
        var history = new Dictionary<string, IReadOnlyList<ShareBucket>>(_pendingHistory, StringComparer.OrdinalIgnoreCase);
        foreach (var (poolName, scheme) in _schemes)
        {
            if (scheme.Value is IPersistentPayoutScheme persistent)
            {
                history[poolName] = persistent.ExportState();
            }
        }

        return history;
    }

    /// <summary>History is held until its pool starts, because pools are registered after state is loaded.</summary>
    public void ImportHistory(IReadOnlyDictionary<string, IReadOnlyList<ShareBucket>> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        foreach (var (poolName, buckets) in history)
        {
            if (_schemes.TryGetValue(poolName, out var scheme))
            {
                Restore(scheme.Value, buckets);
            }
            else
            {
                _pendingHistory[poolName] = buckets;
            }
        }
    }

    private static void Restore(IPayoutScheme scheme, IReadOnlyList<ShareBucket> buckets)
    {
        if (scheme is IPersistentPayoutScheme persistent)
        {
            persistent.ImportState(buckets);
        }
    }

    public void Forget(string poolName)
    {
        _schemes.TryRemove(poolName, out _);
        _pendingHistory.TryRemove(poolName, out _);
    }

    /// <summary>
    /// A stopped pool may be started again, so the work its miners have handed in is set aside rather than
    /// thrown away: they are still owed their part of the next block.
    /// </summary>
    private void OnPoolRemoved(object? sender, PoolChangedEventArgs args)
    {
        if (_schemes.TryRemove(args.Pool.Name, out var scheme) && scheme.Value is IPersistentPayoutScheme persistent)
        {
            _pendingHistory[args.Pool.Name] = persistent.ExportState();
        }
    }

    private IPayoutScheme Create(PoolDefinition pool)
    {
        var scheme = _factory.Create(pool);
        if (_pendingHistory.TryRemove(pool.Name, out var buckets))
        {
            Restore(scheme, buckets);
        }

        return scheme;
    }
}