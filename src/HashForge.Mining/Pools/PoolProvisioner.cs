using HashForge.Domain;
using HashForge.Mining.Payouts;
using Microsoft.Extensions.Logging;

namespace HashForge.Mining.Pools;

public sealed partial class PoolProvisioner(
    IPoolRegistry registry,
    IPoolCatalogue catalogue,
    IPoolDefinitionStore store,
    IStoppedPoolStore stoppedPools,
    IAddressScriptResolver addressResolver,
    IPayoutSchemeRegistry schemes,
    ILogger<PoolProvisioner> logger) : IPoolProvisioner
{
    public IReadOnlyList<ManagedPool> GetPools() =>
        [.. catalogue.Pools.Select(pool => new ManagedPool(pool.Definition, pool.Origin, IsRunning(pool.Definition.Name)))];

    public async Task<ProvisionResult> AddAsync(PoolDefinition pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pool);

        // A stopped pool is not in the registry, so the registry alone would let its name or port be taken.
        var definitionError = PoolDefinitionValidator.FirstError(pool) ?? FindConflict(pool);
        if (definitionError is not null)
        {
            return ProvisionResult.Failure(definitionError);
        }

        var startError = await TryStartAsync(pool, cancellationToken).ConfigureAwait(false);
        if (startError is not null)
        {
            return ProvisionResult.Failure(startError);
        }

        catalogue.TryAdd(pool, PoolOrigin.Administrator);
        await store.SaveAsync(pool, cancellationToken).ConfigureAwait(false);
        LogPoolAdded(pool.Name, pool.Scheme, pool.Port);
        return ProvisionResult.Success;
    }

    public async Task<ProvisionResult> StopAsync(string name, CancellationToken cancellationToken)
    {
        if (catalogue.Find(name) is null)
        {
            return ProvisionResult.Failure(Unknown(name));
        }

        if (!registry.Remove(name))
        {
            return ProvisionResult.Failure($"Pool '{name}' is not running.");
        }

        await stoppedPools.AddAsync(name, cancellationToken).ConfigureAwait(false);
        LogPoolStopped(name);
        return ProvisionResult.Success;
    }

    public async Task<ProvisionResult> StartAsync(string name, CancellationToken cancellationToken)
    {
        if (catalogue.Find(name) is not { } known)
        {
            return ProvisionResult.Failure(Unknown(name));
        }

        if (IsRunning(name))
        {
            return ProvisionResult.Failure($"Pool '{name}' is already running.");
        }

        var startError = await TryStartAsync(known.Definition, cancellationToken).ConfigureAwait(false);
        if (startError is not null)
        {
            return ProvisionResult.Failure(startError);
        }

        await stoppedPools.RemoveAsync(name, cancellationToken).ConfigureAwait(false);
        LogPoolStarted(name, known.Definition.Port);
        return ProvisionResult.Success;
    }

    public async Task<ProvisionResult> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (catalogue.Find(name) is not { } known)
        {
            return ProvisionResult.Failure(Unknown(name));
        }

        if (known.Origin != PoolOrigin.Administrator)
        {
            // It would simply come back at the next start, which is worse than refusing.
            return ProvisionResult.Failure($"Pool '{name}' is set in the pool's configuration. Turn it off there to remove it.");
        }

        registry.Remove(name);
        catalogue.Remove(name);
        schemes.Forget(name);
        await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        await stoppedPools.RemoveAsync(name, cancellationToken).ConfigureAwait(false);
        LogPoolDeleted(name);
        return ProvisionResult.Success;
    }

    private static string Unknown(string name) => $"There is no pool called '{name}'.";

    private bool IsRunning(string name) => registry.Find(name) is not null;

    private string? FindConflict(PoolDefinition pool)
    {
        if (catalogue.Find(pool.Name) is not null)
        {
            return $"A pool named '{pool.Name}' already exists.";
        }

        var samePort = catalogue.Pools.FirstOrDefault(known => known.Definition.Port == pool.Port);
        return samePort is null ? null : $"Port {pool.Port} is already used by pool '{samePort.Definition.Name}'.";
    }

    /// <summary>Starts the pool, or says why it could not be started.</summary>
    private async Task<string?> TryStartAsync(PoolDefinition pool, CancellationToken cancellationToken)
    {
        var feeProblem = await addressResolver.FindProblemAsync(pool, cancellationToken).ConfigureAwait(false);
        if (feeProblem is not null)
        {
            return $"Fee address rejected by the node: {feeProblem}";
        }

        return registry.TryAdd(pool, out var error) ? null : error;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} ({Scheme}) created on port {Port} by an administrator")]
    private partial void LogPoolAdded(string pool, PayoutScheme scheme, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} stopped by an administrator; it stays stopped until it is started from the admin page")]
    private partial void LogPoolStopped(string pool);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} started on port {Port} by an administrator")]
    private partial void LogPoolStarted(string pool, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} deleted by an administrator")]
    private partial void LogPoolDeleted(string pool);
}
