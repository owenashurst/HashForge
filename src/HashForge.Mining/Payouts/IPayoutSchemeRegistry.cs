using HashForge.Domain;

namespace HashForge.Mining.Payouts;

/// <summary>Holds the one payout scheme instance each pool uses.</summary>
public interface IPayoutSchemeRegistry
{
    /// <summary>The pool's scheme, created on first use and given any share history restored for it.</summary>
    IPayoutScheme SchemeFor(PoolDefinition pool);

    /// <summary>The share history of every pool, including history restored for pools that have not started yet.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<ShareBucket>> ExportHistory();

    void ImportHistory(IReadOnlyDictionary<string, IReadOnlyList<ShareBucket>> history);

    /// <summary>Discards a pool's share history, for a pool that has been deleted and will not return.</summary>
    void Forget(string poolName);
}