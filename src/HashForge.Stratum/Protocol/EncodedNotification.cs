using System.Text;
using HashForge.Mining.Jobs;

namespace HashForge.Stratum.Protocol;

/// <summary>
/// One job's <c>mining.notify</c> line, ready to put on the wire. A job on a shared pool goes to every miner
/// on it, and its coinbase can run to tens of kilobytes, so the line is built once and the same bytes are
/// sent to all of them rather than being built again for each miner.
/// </summary>
public sealed class EncodedNotification(MiningJob job)
{
    private const char LineEnd = '\n';

    private readonly Lazy<byte[]> _clean = new(() => Encode(job, cleanJobs: true));
    private readonly Lazy<byte[]> _continuing = new(() => Encode(job, cleanJobs: false));

    /// <summary>The whole line including its line ending. The two forms differ only in the clean-jobs flag.</summary>
    public ReadOnlyMemory<byte> Line(bool cleanJobs) => (cleanJobs ? _clean : _continuing).Value;

    /// <summary>Puts a notification on one line, as it goes on the wire.</summary>
    public static byte[] Encode(string notification) => Encoding.UTF8.GetBytes(notification + LineEnd);

    private static byte[] Encode(MiningJob job, bool cleanJobs) => Encode(JobNotification.Notify(job, cleanJobs));
}
