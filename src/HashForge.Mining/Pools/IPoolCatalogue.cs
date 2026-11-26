using HashForge.Domain;

namespace HashForge.Mining.Pools;

/// <summary>
/// Every pool that exists, running or stopped. The registry holds only the running ones, so without this a
/// stopped pool would be forgotten and could not be started again.
/// </summary>
public interface IPoolCatalogue
{
    /// <summary>Every known pool, ordered by port.</summary>
    IReadOnlyList<KnownPool> Pools { get; }

    KnownPool? Find(string name);

    /// <summary>False when a pool of that name is already known.</summary>
    bool TryAdd(PoolDefinition pool, PoolOrigin origin);

    bool Remove(string name);
}
