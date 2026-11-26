using HashForge.Domain;

namespace HashForge.Mining.Pools;

/// <summary>A pool that exists, whether or not it is running at the moment.</summary>
public sealed record KnownPool(PoolDefinition Definition, PoolOrigin Origin);
