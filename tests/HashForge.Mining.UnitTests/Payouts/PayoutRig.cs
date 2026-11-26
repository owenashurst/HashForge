using HashForge.Domain;
using HashForge.Mining.Network;
using HashForge.Mining.Payouts;
using HashForge.Mining.Pools;
using HashForge.Mining.Shares;

namespace HashForge.Mining.UnitTests.Payouts;

/// <summary>The payout classes wired together over one scheme registry, as they are in the running pool.</summary>
internal sealed class PayoutRig : IDisposable
{
    private readonly PayoutSchemeRegistry _schemes;
    private readonly PayoutRecorder _recorder;
    private readonly PayoutPlanSource _plans;
    private readonly PayoutState _state;

    public PayoutRig(IPoolRegistry pools, IPayoutSchemeFactory factory, INetworkStatsStore networkStats)
    {
        _schemes = new PayoutSchemeRegistry(pools, factory);
        _recorder = new PayoutRecorder(pools, _schemes, TestData.DifficultyFrom(networkStats));
        _plans = new PayoutPlanSource(_schemes);
        _state = new PayoutState(_schemes);
    }

    public string StateKey => _state.StateKey;

    public void Dispose() => _schemes.Dispose();

    public void OnShare(ShareResult share) => _recorder.OnShare(share);

    public Task OnBlockFoundAsync(FoundBlock block, CancellationToken cancellationToken) => _recorder.OnBlockFoundAsync(block, cancellationToken);

    public PayoutPlan GetPlan(PoolDefinition pool, string minerAddress) => _plans.GetPlan(pool, minerAddress);

    public bool IsMinerSpecific(PoolDefinition pool) => _plans.IsMinerSpecific(pool);

    public string ExportState() => _state.ExportState();

    public void ImportState(string state) => _state.ImportState(state);

    public void Forget(string poolName) => _schemes.Forget(poolName);
}