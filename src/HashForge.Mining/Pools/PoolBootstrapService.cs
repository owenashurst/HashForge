using HashForge.Domain;
using HashForge.Mining.Payouts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HashForge.Mining.Pools;

/// <summary>
/// Works out which pools exist (those enabled in the configuration, then those an administrator created)
/// and starts every one that an administrator has not stopped.
/// </summary>
public sealed partial class PoolBootstrapService(
    IPoolRegistry registry,
    IPoolCatalogue catalogue,
    IPoolDefinitionStore store,
    IStoppedPoolStore stoppedPools,
    IAddressScriptResolver addressResolver,
    IOptions<MiningOptions> options,
    ILogger<PoolBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Configured pools come first so that, where two clash, the configuration wins.
        var pools = new List<PoolDefinition>();
        foreach (var pool in options.Value.Pools)
        {
            if (pool.Enabled)
            {
                Catalogue(pool, PoolOrigin.Configuration, pools);
            }
            else
            {
                LogPoolDisabled(pool.Name);
            }
        }

        foreach (var pool in await store.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Catalogue(pool, PoolOrigin.Administrator, pools);
        }

        var stopped = (await stoppedPools.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pool in pools)
        {
            if (stopped.Contains(pool.Name))
            {
                LogPoolLeftStopped(pool.Name);
            }
            else
            {
                await RegisterAsync(pool, cancellationToken).ConfigureAwait(false);
            }
        }

        // A pool that no longer exists (switched off in the configuration, say) is forgotten entirely, so
        // that switching it back on later brings it up running rather than mysteriously stopped.
        foreach (var name in stopped.Where(name => catalogue.Find(name) is null))
        {
            await stoppedPools.RemoveAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Catalogue(PoolDefinition pool, PoolOrigin origin, List<PoolDefinition> pools)
    {
        if (catalogue.TryAdd(pool, origin))
        {
            pools.Add(pool);
        }
        else
        {
            LogPoolSkipped(pool.Name, $"A pool named '{pool.Name}' already exists.");
        }
    }

    private async Task RegisterAsync(PoolDefinition pool, CancellationToken cancellationToken)
    {
        // A fee address the node will not pay to would break every job, so the pool is refused up front.
        var feeProblem = await addressResolver.FindProblemAsync(pool, cancellationToken).ConfigureAwait(false);
        if (feeProblem is not null)
        {
            LogPoolRefused(pool.Name, pool.FeeAddress, feeProblem);
        }
        else if (registry.TryAdd(pool, out var error))
        {
            LogPoolRegistered(pool.Name, pool.Scheme, pool.Port);
        }
        else
        {
            LogPoolSkipped(pool.Name, error);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} ({Scheme}) registered on port {Port}")]
    private partial void LogPoolRegistered(string pool, PayoutScheme scheme, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} is disabled and was not started")]
    private partial void LogPoolDisabled(string pool);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pool {Pool} was stopped by an administrator and was not started; start it from the admin page")]
    private partial void LogPoolLeftStopped(string pool);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pool {Pool} was NOT started: the node rejected its fee address '{FeeAddress}': {Reason}")]
    private partial void LogPoolRefused(string pool, string feeAddress, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pool {Pool} was not started: {Reason}")]
    private partial void LogPoolSkipped(string pool, string reason);
}
