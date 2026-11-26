using System.Text.Json;
using HashForge.Mining.Jobs;
using HashForge.Mining.Shares;
using HashForge.Stratum.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace HashForge.Stratum.UnitTests.Sessions;

public sealed class JobDeliveryTests
{
    private const string TipA = "000000000000000000000000000000000000000000000000000000000000000a";
    private const string TipB = "000000000000000000000000000000000000000000000000000000000000000b";

    /// <summary>The block time of every job these tests build.</summary>
    private const uint TemplateTime = 1_700_000_000;

    private readonly FakeConnection _connection = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(TemplateTime + 20));
    private readonly JobDelivery _delivery;

    public JobDeliveryTests() => _delivery = new JobDelivery(new MinerChannel(_connection, TimeSpan.FromSeconds(30)), _time);

    private static MiningJob Job(string id, long sequence, string tip) =>
        HandlerTestData.Job(StratumTestKit.Pool(), id, sequence, tip);

    private IEnumerable<(string JobId, bool CleanJobs)> Sent() => _connection.Sent.Select(line =>
    {
        using var document = JsonDocument.Parse(line);
        var parameters = document.RootElement.GetProperty("params");
        return (parameters[0].GetString()!, parameters[8].GetBoolean());
    });

    private IEnumerable<uint> SentBlockTimes() => _connection.Sent.Select(line =>
    {
        using var document = JsonDocument.Parse(line);
        return Convert.ToUInt32(document.RootElement.GetProperty("params")[7].GetString()!, 16);
    });

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_FirstJobIsSent_THEN_MinerIsToldToStartClean()
    {
        await _delivery.SendAsync(Job("1", 1, TipA), CancellationToken.None);

        Assert.Equal([("1", true)], Sent());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_WorkIsRefreshedOnSameTip_THEN_MinerKeepsItsCurrentWork()
    {
        await _delivery.SendAsync(Job("1", 1, TipA), CancellationToken.None);
        await _delivery.SendAsync(Job("2", 2, TipA), CancellationToken.None);

        Assert.Equal([("1", true), ("2", false)], Sent());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_ChainTipChanges_THEN_MinerIsToldToDropOldWork()
    {
        await _delivery.SendAsync(Job("1", 1, TipA), CancellationToken.None);
        await _delivery.SendAsync(Job("2", 2, TipB), CancellationToken.None);

        Assert.Equal([("1", true), ("2", true)], Sent());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_OlderJobArrivesAfterNewerOne_THEN_OlderJobIsNeverSent()
    {
        await _delivery.SendAsync(Job("new", 5, TipB), CancellationToken.None);

        await _delivery.SendAsync(Job("old", 4, TipA), CancellationToken.None);

        Assert.Equal([("new", true)], Sent());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_SameJobIsSentAgain_THEN_ItIsResentWithoutCleaning()
    {
        var job = Job("1", 1, TipA);

        await _delivery.SendAsync(job, CancellationToken.None);
        await _delivery.SendAsync(job, CancellationToken.None);

        Assert.Equal([("1", true), ("1", false)], Sent());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_SameJobIsSentAgain_THEN_TheCopyCarriesTheTimeNowSoItIsNewWork()
    {
        var job = Job("1", 1, TipA);

        await _delivery.SendAsync(job, CancellationToken.None);
        await _delivery.SendAsync(job, CancellationToken.None);

        Assert.Equal([TemplateTime, TemplateTime + 20], SentBlockTimes());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_SameJobIsSentSeveralTimesInOneSecond_THEN_EveryCopyHasALaterTime()
    {
        var job = Job("1", 1, TipA);

        for (var i = 0; i < 4; i++)
        {
            await _delivery.SendAsync(job, CancellationToken.None);
        }

        Assert.Equal([TemplateTime, TemplateTime + 20, TemplateTime + 21, TemplateTime + 22], SentBlockTimes());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_ClockIsBehindTheTemplate_THEN_TheCopyStillMovesPastTheFirstOne()
    {
        var slowClock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TemplateTime - 5));
        var delivery = new JobDelivery(new MinerChannel(_connection, TimeSpan.FromSeconds(30)), slowClock);
        var job = Job("1", 1, TipA);

        await delivery.SendAsync(job, CancellationToken.None);
        await delivery.SendAsync(job, CancellationToken.None);

        Assert.Equal([TemplateTime, TemplateTime + 1], SentBlockTimes());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_ClockIsFarAheadOfTheTemplate_THEN_TheCopyStaysWithinWhatAShareMayCarry()
    {
        _time.SetUtcNow(DateTimeOffset.FromUnixTimeSeconds(TemplateTime + 100_000));
        var job = Job("1", 1, TipA);

        await _delivery.SendAsync(job, CancellationToken.None);
        await _delivery.SendAsync(job, CancellationToken.None);

        Assert.Equal([TemplateTime, TemplateTime + ShareTimeLimits.MaxFutureSeconds], SentBlockTimes());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_NewJobFollowsARepeatedOne_THEN_TheNewJobCarriesItsTemplatesOwnTime()
    {
        var job = Job("1", 1, TipA);
        await _delivery.SendAsync(job, CancellationToken.None);
        await _delivery.SendAsync(job, CancellationToken.None);

        await _delivery.SendAsync(Job("2", 2, TipA), CancellationToken.None);

        Assert.Equal([TemplateTime, TemplateTime + 20, TemplateTime], SentBlockTimes());
        Assert.Equal([("1", true), ("1", false), ("2", false)], Sent());
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_OneMinerIsSentAJobAgain_THEN_OtherMinersStillGetTheSharedLine()
    {
        var otherConnection = new FakeConnection();
        var otherDelivery = new JobDelivery(new MinerChannel(otherConnection, TimeSpan.FromSeconds(30)), _time);
        var job = Job("1", 1, TipA);
        await _delivery.SendAsync(job, CancellationToken.None);
        await _delivery.SendAsync(job, CancellationToken.None);

        await otherDelivery.SendAsync(job, CancellationToken.None);

        Assert.Equal(_connection.EncodedLines[0], Assert.Single(otherConnection.EncodedLines));
    }

    [Fact]
    public async Task GIVEN_HasSent_WHEN_JobsWereSentOnSameTip_THEN_AllOfThemAreKnownWhateverTheCase()
    {
        await _delivery.SendAsync(Job("a1", 1, TipA), CancellationToken.None);
        await _delivery.SendAsync(Job("a2", 2, TipA), CancellationToken.None);

        Assert.True(_delivery.HasSent("a1"));
        Assert.True(_delivery.HasSent("A2"));
        Assert.False(_delivery.HasSent("a3"));
    }

    [Fact]
    public async Task GIVEN_HasSent_WHEN_ChainTipChanged_THEN_JobsFromOldTipAreForgotten()
    {
        await _delivery.SendAsync(Job("1", 1, TipA), CancellationToken.None);
        await _delivery.SendAsync(Job("2", 2, TipB), CancellationToken.None);

        Assert.False(_delivery.HasSent("1"));
        Assert.True(_delivery.HasSent("2"));
    }

    [Fact]
    public async Task GIVEN_HasSent_WHEN_JobWasDroppedAsOutOfDate_THEN_ItIsNotKnown()
    {
        await _delivery.SendAsync(Job("new", 5, TipB), CancellationToken.None);
        await _delivery.SendAsync(Job("old", 4, TipB), CancellationToken.None);

        Assert.False(_delivery.HasSent("old"));
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_ManyJobsRaceInRandomOrder_THEN_MinerOnlyEverMovesForward()
    {
        var jobs = Enumerable.Range(1, 200).Select(i => Job(i.ToString("x"), i, i % 10 == 0 ? TipB : TipA)).ToList();

        await Task.WhenAll(jobs.OrderBy(_ => Random.Shared.Next()).Select(job => Task.Run(() => _delivery.SendAsync(job, CancellationToken.None))));

        var sequences = Sent().Select(s => Convert.ToInt64(s.JobId, 16)).ToList();
        Assert.Equal(sequences.Order(), sequences);
        Assert.Equal(200, sequences[^1]);
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_SameJobGoesToTwoMiners_THEN_BothAreSentTheVerySameEncodedLine()
    {
        var otherConnection = new FakeConnection();
        var otherDelivery = new JobDelivery(new MinerChannel(otherConnection, TimeSpan.FromSeconds(30)), _time);
        var job = Job("1", 1, TipA);

        await _delivery.SendAsync(job, CancellationToken.None);
        await otherDelivery.SendAsync(job, CancellationToken.None);

        // Equal memory means the same array, not a copy: the line was built once for both.
        Assert.Equal(Assert.Single(_connection.EncodedLines), Assert.Single(otherConnection.EncodedLines));
    }

    [Fact]
    public async Task GIVEN_SendAsync_WHEN_OneMinerHasSeenTheTipAndAnotherHasNot_THEN_EachGetsTheRightCleanFlag()
    {
        var newcomer = new FakeConnection();
        var newcomerDelivery = new JobDelivery(new MinerChannel(newcomer, TimeSpan.FromSeconds(30)), _time);
        await _delivery.SendAsync(Job("1", 1, TipA), CancellationToken.None);
        var refreshed = Job("2", 2, TipA);

        await _delivery.SendAsync(refreshed, CancellationToken.None);
        await newcomerDelivery.SendAsync(refreshed, CancellationToken.None);

        Assert.Equal([("1", true), ("2", false)], Sent());
        using var document = JsonDocument.Parse(Assert.Single(newcomer.Sent));
        Assert.True(document.RootElement.GetProperty("params")[8].GetBoolean());
    }
}
