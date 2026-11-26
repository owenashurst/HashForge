using HashForge.Domain;

namespace HashForge.Mining.Pools;

/// <summary>Creates, stops, starts and deletes pools at runtime on behalf of an administrator.</summary>
public interface IPoolProvisioner
{
    /// <summary>Every pool that exists, running or stopped, ordered by port.</summary>
    IReadOnlyList<ManagedPool> GetPools();

    /// <summary>Creates a pool and starts it.</summary>
    Task<ProvisionResult> AddAsync(PoolDefinition pool, CancellationToken cancellationToken);

    /// <summary>Stops a running pool. It is kept, and stays stopped across restarts until it is started again.</summary>
    Task<ProvisionResult> StopAsync(string name, CancellationToken cancellationToken);

    /// <summary>Starts a pool that was stopped.</summary>
    Task<ProvisionResult> StartAsync(string name, CancellationToken cancellationToken);

    /// <summary>Removes a pool that was created from the admin page, stopping it first if it is running.</summary>
    Task<ProvisionResult> DeleteAsync(string name, CancellationToken cancellationToken);
}
