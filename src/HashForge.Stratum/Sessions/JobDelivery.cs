using System.Collections.Concurrent;
using HashForge.Mining.Jobs;
using HashForge.Mining.Shares;
using HashForge.Stratum.Protocol;

namespace HashForge.Stratum.Sessions;

/// <summary>
/// Sends jobs to one miner in strict order. Several senders can race (a new block, a routine refresh,
/// a difficulty change), so the decision to send is made while holding the wire: a job built from an older
/// template than the last one sent is dropped, and the miner can never be moved backwards.
/// </summary>
public sealed class JobDelivery(MinerChannel channel, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, bool> _sentJobIds = new(StringComparer.OrdinalIgnoreCase);
    private long _lastSequence = -1;
    private string? _lastPreviousBlockHash;
    private uint _lastBlockTime;

    /// <summary>
    /// Whether this job was sent on this connection and is still on the chain tip the miner was last given.
    /// Work for any other job was not built for this connection, so it cannot be valid here.
    /// </summary>
    public bool HasSent(string jobId) => _sentJobIds.ContainsKey(jobId);

    public Task SendAsync(MiningJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return channel.SendEncodedAsync(() => Compose(job), cancellationToken);
    }

    /// <summary>Runs under the channel's write lock, so no other job can be sent between the check and the write.</summary>
    private ReadOnlyMemory<byte>? Compose(MiningJob job)
    {
        if (job.TemplateSequence < _lastSequence)
        {
            return null;
        }

        // The miner is told to drop its current work exactly when the chain tip it is building on has changed.
        var cleanJobs = job.Template.PreviousBlockHash != _lastPreviousBlockHash;
        if (cleanJobs)
        {
            _sentJobIds.Clear();
        }

        var isRepeat = !_sentJobIds.TryAdd(job.Id, true);
        _lastSequence = job.TemplateSequence;
        _lastPreviousBlockHash = job.Template.PreviousBlockHash;
        if (!isRepeat)
        {
            _lastBlockTime = job.Template.CurrentTime;
            return JobNotification.NotifyLine(job, cleanJobs);
        }

        // The same job goes out again when this miner's difficulty changes between templates. A miner starts
        // a job it is handed from the beginning, so an identical copy would have it find, and hand in, the
        // shares it has already been paid for. A later block time makes the copy different work, while it
        // stays the same job here, so nothing can be credited twice.
        _lastBlockTime = LaterBlockTime(job);
        return JobNotification.NotifyLine(job, cleanJobs, _lastBlockTime);
    }

    /// <summary>The time now, but always after the one this miner was last given and never past what a share may carry.</summary>
    private uint LaterBlockTime(MiningJob job)
    {
        var now = (uint)Math.Clamp(timeProvider.GetUtcNow().ToUnixTimeSeconds(), 0, uint.MaxValue);
        var later = Math.Max(now, _lastBlockTime + 1);
        return Math.Min(later, job.Template.CurrentTime + ShareTimeLimits.MaxFutureSeconds);
    }
}