using HashForge.Domain;

namespace HashForge.Mining.Pools;

/// <summary>A pool as an administrator sees it: what it is, where it came from and whether it is running.</summary>
public sealed record ManagedPool(PoolDefinition Definition, PoolOrigin Origin, bool IsRunning)
{
    /// <summary>Only pools created from the admin page can be deleted there; the others are removed in the configuration.</summary>
    public bool CanBeDeleted => Origin == PoolOrigin.Administrator;
}
