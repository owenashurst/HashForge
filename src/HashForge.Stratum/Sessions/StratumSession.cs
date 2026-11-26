using System.Net;
using HashForge.Domain;

namespace HashForge.Stratum.Sessions;

/// <summary>One miner connection: who it is, what was negotiated, and the parts that do the work.</summary>
public sealed class StratumSession : IAsyncDisposable
{
    public StratumSession(
        PoolDefinition pool,
        MinerChannel channel,
        byte[] extranonce1,
        VarDiffController varDiff,
        DateTimeOffset connectedAt,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(extranonce1);
        Pool = pool;
        Channel = channel;
        Extranonce1 = extranonce1;
        VarDiff = varDiff;
        ConnectedAt = connectedAt;
        Id = Convert.ToHexStringLower(extranonce1);
        Jobs = new JobDelivery(channel, timeProvider);
    }

    public string Id { get; }

    public PoolDefinition Pool { get; }

    public byte[] Extranonce1 { get; }

    public DateTimeOffset ConnectedAt { get; }

    public MinerChannel Channel { get; }

    public JobDelivery Jobs { get; }

    public WorkerRoster Workers { get; } = new();

    public ShareTally Shares { get; } = new();

    public VarDiffController VarDiff { get; }

    public IPAddress RemoteAddress => Channel.RemoteAddress;

    public bool IsSubscribed { get; set; }

    /// <summary>The version-rolling mask agreed with the miner; zero until negotiated.</summary>
    public uint VersionMask { get; set; }

    public SessionInfo ToInfo() => new(
        Id,
        Pool.Name,
        RemoteAddress,
        Workers.MinerAddress,
        Workers.WorkerNames,
        VarDiff.Difficulty,
        Shares.Accepted,
        Shares.Rejected,
        ConnectedAt);

    public ValueTask DisposeAsync() => Channel.DisposeAsync();
}