namespace HashForge.Mining.Pools;

/// <summary>Where a pool's definition is kept, which decides who can remove it.</summary>
public enum PoolOrigin
{
    /// <summary>Set in the pool's configuration. It exists for as long as it is enabled there.</summary>
    Configuration,

    /// <summary>Created from the admin page and kept in the database until it is deleted there.</summary>
    Administrator,
}
