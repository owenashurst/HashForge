using HashForge.Domain;
using HashForge.Mining.Network;
using HashForge.Mining.Payouts;
using HashForge.Mining.Pools;
using Microsoft.Extensions.Time.Testing;

namespace HashForge.Mining.UnitTests.Payouts;

public sealed class PayoutTests
{
    private readonly PoolRegistry _registry = new();
    private readonly NetworkStatsStore _networkStats = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly PoolDefinition _prop = TestData.Pool("prop", PayoutScheme.Prop, 1);
    private readonly PoolDefinition _solo = TestData.Pool("solo", PayoutScheme.Solo, 2);

    public PayoutTests()
    {
        _registry.TryAdd(_prop, out _);
        _registry.TryAdd(_solo, out _);
    }

    private PayoutRig Tracker() => new(_registry, new PayoutSchemeFactory(_time), _networkStats);

    [Fact]
    public void GIVEN_OnShare_WHEN_ShareIsAccepted_THEN_MinerGainsCreditedDifficulty()
    {
        var tracker = Tracker();

        tracker.OnShare(TestData.Share("prop", credited: 64));
        tracker.OnShare(TestData.Share("prop", credited: 64));
        Assert.False(tracker.IsMinerSpecific(_prop));

        Assert.Equal(128, tracker.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
    }

    [Fact]
    public void GIVEN_OnShare_WHEN_ShareIsRejectedOrPoolUnknown_THEN_NothingIsRecorded()
    {
        var tracker = Tracker();

        tracker.OnShare(TestData.Share("prop", outcome: ShareOutcome.Duplicate));
        tracker.OnShare(TestData.Share("missing"));

        Assert.True(tracker.IsMinerSpecific(_prop));
    }

    [Fact]
    public void GIVEN_GetWeights_WHEN_PoolIsSolo_THEN_OnlyRequestingMinerIsWeighted()
    {
        var tracker = Tracker();
        tracker.OnShare(TestData.Share("solo"));

        Assert.Equal(["me"], tracker.GetPlan(_solo, "me").Weights.Keys);
    }

    [Fact]
    public async Task GIVEN_OnBlockFoundAsync_WHEN_PoolIsProp_THEN_RoundIsCleared()
    {
        var tracker = Tracker();
        tracker.OnShare(TestData.Share("prop"));

        await tracker.OnBlockFoundAsync(TestData.Block("prop", PayoutScheme.Prop), CancellationToken.None);
        await tracker.OnBlockFoundAsync(TestData.Block("missing"), CancellationToken.None);

        Assert.True(tracker.IsMinerSpecific(_prop));
        Assert.Equal(["finder"], tracker.GetPlan(_prop, "finder").Weights.Keys);
    }

    [Fact]
    public void GIVEN_PoolRemoved_WHEN_PoolIsAddedAgain_THEN_MinersKeepTheWorkTheyHandedIn()
    {
        var tracker = Tracker();
        tracker.OnShare(TestData.Share("prop", credited: 64));

        _registry.Remove("prop");
        _registry.TryAdd(_prop, out _);

        Assert.Equal(64, tracker.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
    }

    [Fact]
    public void GIVEN_PoolRemoved_WHEN_StateIsSavedWhileItIsStopped_THEN_ItsWorkIsStillInTheSnapshot()
    {
        var original = Tracker();
        original.OnShare(TestData.Share("prop", credited: 64));
        _registry.Remove("prop");

        var restored = Tracker();
        restored.ImportState(original.ExportState());
        _registry.TryAdd(_prop, out _);

        Assert.Equal(64, restored.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GIVEN_Forget_WHEN_PoolIsAddedAgain_THEN_ItStartsWithEmptyHistory(bool stoppedFirst)
    {
        var tracker = Tracker();
        tracker.OnShare(TestData.Share("prop"));
        if (stoppedFirst)
        {
            _registry.Remove("prop");
        }

        tracker.Forget("prop");
        _registry.Remove("prop");
        _registry.TryAdd(_prop, out _);

        Assert.True(tracker.IsMinerSpecific(_prop));
    }

    [Fact]
    public void GIVEN_ImportState_WHEN_PoolStartsAfterStateIsLoaded_THEN_StateIsAppliedOnFirstUse()
    {
        var original = Tracker();
        original.OnShare(TestData.Share("prop", credited: 7));

        var restored = Tracker();
        restored.ImportState(original.ExportState());

        Assert.Equal(7, restored.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
    }

    [Fact]
    public void GIVEN_ImportState_WHEN_SchemeAlreadyExists_THEN_ItsStateIsReplaced()
    {
        var original = Tracker();
        original.OnShare(TestData.Share("prop", credited: 7));
        var restored = Tracker();
        restored.OnShare(TestData.Share("prop", credited: 1000));

        restored.ImportState(original.ExportState());

        Assert.Equal(7, restored.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
    }

    [Fact]
    public void GIVEN_ExportState_WHEN_RestoredPoolHasNotStartedYet_THEN_ItsStateIsStillExported()
    {
        var original = Tracker();
        original.OnShare(TestData.Share("prop", credited: 7));
        var restored = Tracker();
        restored.ImportState(original.ExportState());

        var reExported = Tracker();
        reExported.ImportState(restored.ExportState());

        Assert.Equal(7, reExported.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
    }

    [Fact]
    public void GIVEN_StateKey_WHEN_Read_THEN_IsStable()
    {
        Assert.Equal("payouts", Tracker().StateKey);
    }

    [Fact]
    public void GIVEN_GetPlan_WHEN_ManyMinersArriveAtOnceAfterARestart_THEN_RestoredStateIsNeverLost()
    {
        var original = Tracker();
        original.OnShare(TestData.Share("prop", credited: 7));
        var saved = original.ExportState();

        for (var run = 0; run < 200; run++)
        {
            var restored = Tracker();
            restored.ImportState(saved);

            Parallel.For(0, 8, _ => restored.GetPlan(_prop, "anyone"));

            Assert.Equal(7, restored.GetPlan(_prop, "anyone").Weights[TestData.MinerAddress]);
        }
    }

    [Fact]
    public void GIVEN_Dispose_WHEN_PoolIsRemovedAfterwards_THEN_TrackerNoLongerReacts()
    {
        var tracker = Tracker();
        tracker.OnShare(TestData.Share("prop", credited: 7));

        tracker.Dispose();
        _registry.Remove("prop");

        Assert.False(tracker.IsMinerSpecific(_prop));
    }
}