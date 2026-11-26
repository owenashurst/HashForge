namespace HashForge.Domain;

/// <summary>
/// How much of the work miners handed in was turned down. A count of rejected shares only ever grows,
/// so on its own it says nothing about whether a pool or a miner is healthy; the rate does.
/// </summary>
public static class RejectRate
{
    private const double WholePercent = 100;

    /// <summary>Rejected shares as a percentage of every share handed in, or null before the first share.</summary>
    public static double? Percent(long accepted, long rejected)
    {
        // Added as doubles: two long totals could overflow, and a rate needs no more precision than this.
        var submitted = (double)accepted + rejected;
        return submitted > 0 ? rejected / submitted * WholePercent : null;
    }
}
