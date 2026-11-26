using System.Buffers.Binary;
using HashForge.Domain;
using HashForge.Mining.Jobs;
using HashForge.Stratum.Transport;
using Microsoft.Extensions.Options;

namespace HashForge.Stratum.Sessions;

/// <summary>Creates sessions, giving each a unique extranonce1 so no two miners search the same space.</summary>
public sealed class SessionFactory(IOptions<StratumOptions> options, TimeProvider timeProvider) : ISessionFactory
{
    private uint _lastExtranonce = (uint)Random.Shared.Next();

    public StratumSession Create(PoolDefinition pool, IStratumConnection connection)
    {
        var extranonce1 = new byte[JobFactory.Extranonce1Size];
        BinaryPrimitives.WriteUInt32BigEndian(extranonce1, Interlocked.Increment(ref _lastExtranonce));
        var now = timeProvider.GetUtcNow();
        var channel = new MinerChannel(connection, TimeSpan.FromSeconds(options.Value.SendTimeoutSeconds));
        return new StratumSession(pool, channel, extranonce1, new VarDiffController(options.Value, now), now, timeProvider);
    }
}
