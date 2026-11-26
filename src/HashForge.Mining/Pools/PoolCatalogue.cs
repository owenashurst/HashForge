using HashForge.Domain;

namespace HashForge.Mining.Pools;

public sealed class PoolCatalogue : IPoolCatalogue
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, KnownPool> _pools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<KnownPool> Pools
    {
        get
        {
            lock (_gate)
            {
                return [.. _pools.Values.OrderBy(p => p.Definition.Port)];
            }
        }
    }

    public KnownPool? Find(string name)
    {
        lock (_gate)
        {
            return _pools.GetValueOrDefault(name);
        }
    }

    public bool TryAdd(PoolDefinition pool, PoolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(pool);

        lock (_gate)
        {
            return _pools.TryAdd(pool.Name, new KnownPool(pool, origin));
        }
    }

    public bool Remove(string name)
    {
        lock (_gate)
        {
            return _pools.Remove(name);
        }
    }
}
