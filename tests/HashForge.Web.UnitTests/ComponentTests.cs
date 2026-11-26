using System.Net;
using Bunit;
using HashForge.Domain;
using HashForge.Domain.Formatting;
using HashForge.Mining.History;
using HashForge.Mining.Jobs;
using HashForge.Mining.Pools;
using HashForge.Mining.Shares;
using HashForge.Mining.Statistics;
using HashForge.Stratum.Sessions;
using HashForge.Web.Components.Layout;
using HashForge.Web.Components.Pages;
using HashForge.Web.Components.Shared;
using HashForge.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using AdminPage = HashForge.Web.Components.Pages.Admin;

namespace HashForge.Web.UnitTests;

public sealed class ComponentTests : WebTestContext
{
    private static PoolHistorySample Sample(int minutes, double hashrate) => new("pplns", Now.AddMinutes(minutes), hashrate, 3, 4);

    private IRenderedComponent<NavMenu> RenderNav(bool adminAllowed)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
        AdminAccess.IsAllowed(context.Connection.RemoteIpAddress).Returns(adminAllowed);
        return Render<NavMenu>(p => p.AddCascadingValue<HttpContext>(context));
    }

    [Fact]
    public void GIVEN_NavMenu_WHEN_VisitorIsOnAllowedNetwork_THEN_AdminLinkIsShown()
    {
        Assert.Contains(RenderNav(adminAllowed: true).FindAll("a"), a => a.TextContent == "Admin");
    }

    [Fact]
    public void GIVEN_NavMenu_WHEN_VisitorIsOutsideAllowedNetworks_THEN_AdminLinkIsHidden()
    {
        var links = RenderNav(adminAllowed: false).FindAll("a").Select(a => a.TextContent);

        Assert.Equal(["Pools", "Blocks", "Connect"], links);
    }

    [Fact]
    public void GIVEN_StatTile_WHEN_HintIsOmitted_THEN_OnlyLabelAndValueRender()
    {
        var tile = Render<StatTile>(p => p.Add(t => t.Label, "Hashrate").Add(t => t.Value, "2.5 TH/s"));

        Assert.Equal("Hashrate", tile.Find(".tile-label").TextContent);
        Assert.Equal("2.5 TH/s", tile.Find(".tile-value").TextContent);
        Assert.Empty(tile.FindAll(".tile-hint"));
    }

    [Fact]
    public void GIVEN_StatsTiles_WHEN_Rendered_THEN_ShowsEffortSharesAndBestShareWorker()
    {
        var tiles = Render<StatsTiles>(p => p.Add(t => t.Stats, Stats()).Add(t => t.Now, Now));

        var text = tiles.Markup;
        Assert.Contains("2.5 TH/s", text);
        Assert.Contains("42.5%", text);
        Assert.Contains("120", text);
        Assert.Contains("8.1 G", text);
        Assert.Contains("bc1qalice.rig1", text);
        Assert.Contains("30s ago", text);
    }

    [Fact]
    public void GIVEN_StatsTiles_WHEN_SharesWereRejected_THEN_ShowsTheRateWithTheCountBesideIt()
    {
        var tiles = Render<StatsTiles>(p => p.Add(t => t.Stats, Stats(accepted: 5000, rejected: 25)).Add(t => t.Now, Now));

        var tile = tiles.FindAll(".tile").Single(t => t.QuerySelector(".tile-label")!.TextContent == "Reject rate");
        Assert.Equal("0.5%", tile.QuerySelector(".tile-value")!.TextContent);
        Assert.Equal("25 rejected", tile.QuerySelector(".tile-hint")!.TextContent);
    }

    [Fact]
    public void GIVEN_StatsTiles_WHEN_NoSharesYet_THEN_RejectRateIsADashNotAPerfectScore()
    {
        var tiles = Render<StatsTiles>(p => p.Add(t => t.Stats, Stats(accepted: 0, rejected: 0)).Add(t => t.Now, Now));

        var tile = tiles.FindAll(".tile").Single(t => t.QuerySelector(".tile-label")!.TextContent == "Reject rate");
        Assert.Equal(Display.None, tile.QuerySelector(".tile-value")!.TextContent);
    }

    [Theory]
    [InlineData(0, 0, "0")]
    [InlineData(5000, 25, "25 (0.5%)")]
    [InlineData(0, 2, "2 (100%)")]
    public void GIVEN_RejectedWithRate_WHEN_SharesVary_THEN_CountComesFirstAndRateOnlyOnceThereAreShares(long accepted, long rejected, string expected)
    {
        Assert.Equal(expected, StatsText.RejectedWithRate(accepted, rejected));
    }

    [Fact]
    public void GIVEN_BlockTable_WHEN_NoBlocks_THEN_ShowsEmptyMessage()
    {
        var table = Render<BlockTable>(p => p.Add(t => t.Blocks, []).Add(t => t.Now, Now));

        Assert.Contains("No blocks found yet", table.Markup);
    }

    [Fact]
    public void GIVEN_BlockTable_WHEN_BlocksGiven_THEN_ShowsRewardEffortAndStatus()
    {
        var table = Render<BlockTable>(p => p.Add(t => t.Blocks, [Block(), Block(accepted: false)]).Add(t => t.Now, Now));

        var rows = table.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("850001", rows[0].TextContent);
        Assert.Contains("3.12500000 BTC", rows[0].TextContent);
        Assert.Contains("87.5%", rows[0].TextContent);
        Assert.Contains("Miners paid", table.Find("thead").TextContent);
        Assert.Equal("34", rows[0].QuerySelectorAll("td")[5].TextContent);
        Assert.Contains("Accepted", rows[0].TextContent);
        Assert.Contains("Rejected: high-hash", rows[1].TextContent);
    }

    [Fact]
    public void GIVEN_HashrateChart_WHEN_FewerThanTwoSamples_THEN_ShowsPlaceholderInsteadOfChart()
    {
        var chart = Render<HashrateChart>(p => p.Add(c => c.Samples, [Sample(0, 1e12)]));

        Assert.Empty(chart.FindAll("svg[role=img]"));
        Assert.Contains("appears once", chart.Markup);
    }

    [Fact]
    public void GIVEN_HashrateChart_WHEN_SamplesGiven_THEN_DrawsLineWithHoverPointsAxisAndTableView()
    {
        var chart = Render<HashrateChart>(p => p.Add(c => c.Samples, [Sample(0, 1e12), Sample(5, 2e12), Sample(10, 2.4e12)]));

        Assert.Equal(3, chart.Find("polyline.chart-line").GetAttribute("points")!.Split(' ').Length);
        Assert.Equal(3, chart.FindAll("circle.chart-hit").Count);
        Assert.Contains("2.4 TH/s, 3 miners", chart.Find("circle.chart-hit:last-of-type title").TextContent);
        Assert.Contains("5 TH/s", chart.Markup);
        Assert.Equal(3, chart.FindAll("details tbody tr").Count);
    }

    [Fact]
    public void GIVEN_Home_WHEN_PoolsAndNetworkAvailable_THEN_ShowsPoolsTotalsAndNetworkStats()
    {
        Overview.GetPools().Returns([PoolSnapshot("solo", PayoutScheme.Solo, 3333), PoolSnapshot()]);
        Overview.GetTotals().Returns(new ShareTotals(5000, 25));
        Overview.GetBestShare().Returns(new BestShare(7.7e12, "bc1qbob.s21", Now));
        NetworkStats.Current.Returns(Network());
        Blocks.GetRecentAsync(5, Arg.Any<CancellationToken>()).Returns([Block()]);
        Templates.Status.Returns(new TemplateStatus("00ab", Now) { RewardSatoshis = 312_600_000 });

        var home = Render<Home>();

        Assert.Equal(2, home.FindAll("table")[0].QuerySelectorAll("tbody tr").Length);
        Assert.Contains("SOLO", home.Markup);
        Assert.Contains("5,000", home.Markup);
        Assert.Contains("0.5%", home.Markup);
        Assert.Contains("25 rejected, all pools", home.Markup);
        Assert.Contains("7.7 T", home.Markup);
        Assert.Contains("bc1qbob.s21", home.Markup);
        Assert.Contains("850,000", home.Markup);
        Assert.Contains("650 EH/s", home.Markup);
        Assert.Contains("3.12600000 BTC", home.Markup);
        Assert.Contains("subsidy plus fees", home.Markup);
        Assert.Contains("Last network block", home.Markup);
        Assert.Contains("4m ago", home.Markup);
        Assert.Contains("850001", home.Markup);
        Assert.Empty(home.FindAll(".notice"));
    }

    [Fact]
    public void GIVEN_Home_WHEN_NodeIsUnreachableAndNoPools_THEN_ShowsWaitingMessages()
    {
        NetworkStats.Current.Returns((HashForge.Mining.Network.NetworkStats?)null);

        var home = Render<Home>();

        Assert.Contains("No pools are running", home.Markup);
        Assert.Contains("Waiting for the Bitcoin node", home.Markup);
    }

    [Fact]
    public void GIVEN_Home_WHEN_NodeIsSynchronising_THEN_ShowsSyncNotice()
    {
        NetworkStats.Current.Returns(Network(synced: false));

        Assert.Contains("still synchronising (50%)", Render<Home>().Find(".notice").TextContent);
    }

    [Fact]
    public void GIVEN_RefreshingComponent_WHEN_RefreshIntervalElapses_THEN_PageReloadsItsData()
    {
        var home = Render<Home>();
        Overview.GetPools().Returns([PoolSnapshot("late-pool")]);

        Time.Advance(TimeSpan.FromSeconds(WebOptions.RefreshSeconds));

        home.WaitForAssertion(() => Assert.Contains("late-pool", home.Markup));
    }

    [Fact]
    public void GIVEN_RefreshingComponent_WHEN_Disposed_THEN_NoFurtherReloadsHappen()
    {
        var home = Render<Home>();
        home.Instance.Dispose();
        Overview.ClearReceivedCalls();

        Time.Advance(TimeSpan.FromSeconds(60));

        Overview.DidNotReceive().GetPools();
    }

    [Fact]
    public void GIVEN_Pool_WHEN_PoolExists_THEN_ShowsStatsChartAndMiners()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        Statistics.GetMiners("pplns").Returns([new MinerSnapshot("bc1qalicealicealicealicealice", Stats(), [new WorkerSnapshot("rig1", Stats())])]);
        History.GetSinceAsync("pplns", Now.AddHours(-24), Arg.Any<CancellationToken>()).Returns([Sample(-10, 1e12), Sample(-5, 2e12)]);

        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));

        Assert.Contains("PPLNS", pool.Find("h1").TextContent);
        Assert.Contains("3334", pool.Markup);
        Assert.Contains("9 workers", pool.Markup);
        Assert.Single(pool.FindAll("svg[role=img]"));
        Assert.Contains("bc1qalicea…ealice", pool.Markup, StringComparison.Ordinal);
        var rejected = pool.Find("td[title='3 rejected']");
        Assert.Equal("2.44%", rejected.TextContent);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_PoolDoesNotExist_THEN_ShowsNotFoundMessage()
    {
        var pool = Render<Pool>(p => p.Add(x => x.Name, "ghost"));

        Assert.Contains("Pool not found", pool.Markup);
        Statistics.DidNotReceiveWithAnyArgs().GetMiners(default!);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_AddressIsSearched_THEN_NavigatesToThatMiner()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));

        pool.Find("#miner-address").Change(" bc1qsearchsearchsearch ");
        pool.Find("form").Submit();

        Assert.EndsWith("/pool/pplns/miner/bc1qsearchsearchsearch", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_SearchIsEmpty_THEN_StaysOnPage()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));
        var before = Services.GetRequiredService<NavigationManager>().Uri;

        pool.Find("form").Submit();

        Assert.Equal(before, Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void GIVEN_Miner_WHEN_MinerHasWorkers_THEN_ShowsStatsPerWorker()
    {
        Statistics.GetMiner("pplns", "bc1qalicealicealice").Returns(
            new MinerSnapshot("bc1qalicealicealice", Stats(), [new WorkerSnapshot("garage", Stats(accepted: 77)), new WorkerSnapshot("shed", Stats())]));

        var miner = Render<Miner>(p => p.Add(x => x.PoolName, "pplns").Add(x => x.Address, "bc1qalicealicealice"));

        var rows = miner.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("garage", rows[0].TextContent);
        Assert.Contains("77", rows[0].TextContent);
        Assert.Equal("3.75%", rows[0].QuerySelector("td[title='3 rejected']")!.TextContent);
        Assert.Equal("bc1qalicealicealice", miner.Find("h1").TextContent);
    }

    [Fact]
    public void GIVEN_Miner_WHEN_AddressHasNoShares_THEN_ShowsEmptyMessage()
    {
        var miner = Render<Miner>(p => p.Add(x => x.PoolName, "pplns").Add(x => x.Address, "bc1qnobodynobodynobody"));

        Assert.Contains("has not submitted any shares", miner.Markup);
    }

    [Fact]
    public void GIVEN_BlocksPage_WHEN_Rendered_THEN_RequestsConfiguredNumberOfBlocks()
    {
        WebOptions.RecentBlockCount = 40;
        Blocks.GetRecentAsync(40, Arg.Any<CancellationToken>()).Returns([Block()]);

        var page = Render<Blocks>();

        Assert.Single(page.FindAll("tbody tr"));
    }

    [Fact]
    public void GIVEN_Connect_WHEN_PoolsAreRunning_THEN_ShowsStratumAddressFeeAndPayoutRulePerPool()
    {
        Registry.Pools.Returns(
        [
            PoolDefinition("solo", PayoutScheme.Solo, 3333),
            PoolDefinition("prop", PayoutScheme.Prop, 3335),
            PoolDefinition(),
            PoolDefinition("bonus", PayoutScheme.FinderBonus, 3336),
        ]);

        var page = Render<Connect>();

        var rows = page.FindAll("tbody tr");
        Assert.Contains("stratum+tcp://pool.example:3333", rows[0].TextContent);
        Assert.Contains("keeps the whole reward", rows[0].TextContent);
        Assert.Contains("since the pool's last block", rows[1].TextContent);
        Assert.Contains("1.5%", rows[2].TextContent);
        Assert.Contains("2× network difficulty", rows[2].TextContent);
        Assert.Contains("BONUS", rows[3].TextContent);
        Assert.Contains("gets 1.00000000 BTC first", rows[3].TextContent);
    }

    [Fact]
    public void GIVEN_Connect_WHEN_NoPools_THEN_ShowsEmptyMessage()
    {
        Assert.Contains("No pools are running", Render<Connect>().Markup);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_NodeIsSynchronising_THEN_ShowsSyncStatusAndPruning()
    {
        NetworkStats.Current.Returns(Network(synced: false));

        var admin = Render<AdminPage>();

        Assert.Contains("Synchronising", admin.Markup);
        Assert.Contains("50%", admin.Markup);
        Assert.Contains("of 900,000 headers", admin.Markup);
        Assert.Contains("pruned node", admin.Markup);
        Assert.Contains("12 GB", admin.Markup);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_NodeHasNotAnswered_THEN_ShowsNotice()
    {
        NetworkStats.Current.Returns((HashForge.Mining.Network.NetworkStats?)null);

        Assert.Contains("has not answered yet", Render<AdminPage>().Find(".notice").TextContent);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_MinersAreConnected_THEN_ShowsTotalsSessionsAndIpList()
    {
        NetworkStats.Current.Returns(Network());
        Overview.GetPools().Returns([PoolSnapshot("solo", PayoutScheme.Solo, 3333)]);
        Overview.GetTotals().Returns(new ShareTotals(9000, 12));
        Miners.GetSessions().Returns(
        [
            new SessionInfo("01", "solo", IPAddress.Parse("192.168.1.40"), "bc1qalicealicealicealice", ["rig1", "rig2"], 4096, 500, 2, Now.AddMinutes(-7)),
            new SessionInfo("02", "solo", IPAddress.Parse("192.168.1.41"), null, [], 1024, 0, 0, Now),
        ]);
        Miners.GetAddresses().Returns([new ConnectedAddress(IPAddress.Parse("192.168.1.40"), 1), new ConnectedAddress(IPAddress.Parse("192.168.1.41"), 1)]);

        var admin = Render<AdminPage>();

        Assert.Contains("Synced", admin.Markup);
        Assert.Contains("9,000", admin.Markup);
        Assert.Contains("2 IP addresses", admin.Markup);
        var sessionRows = admin.FindAll("table")[1].QuerySelectorAll("tbody tr");
        Assert.Contains("192.168.1.40", sessionRows[0].TextContent);
        Assert.Contains("rig1, rig2", sessionRows[0].TextContent);
        Assert.Contains("4.1 K", sessionRows[0].TextContent);
        Assert.Contains("7m ago", sessionRows[0].TextContent);
        Assert.Contains("not authorised", sessionRows[1].TextContent);
        Assert.Equal(2, admin.FindAll(".ip-list li").Count);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_PoolFormSubmitted_THEN_PoolIsStartedWithoutRestartAndConfirmed()
    {
        PoolDefinition? requested = null;
        Provisioner.AddAsync(Arg.Do<PoolDefinition>(p => requested = p), Arg.Any<CancellationToken>()).Returns(ProvisionResult.Success);
        var admin = Render<AdminPage>();

        admin.Find("input[placeholder='pplns-2']").Change(" prop-2 ");
        admin.Find("select").Change("Prop");
        admin.FindAll("input[type=number]")[0].Change("3340");
        admin.FindAll("input[type=number]")[1].Change("2.5");
        admin.Find("input[placeholder='bc1q…']").Change("tb1qfee");
        admin.Find("form").Submit();

        Assert.Equal(("prop-2", PayoutScheme.Prop, 3340, 2.5m, "tb1qfee"), (requested!.Name, requested.Scheme, requested.Port, requested.FeePercent, requested.FeeAddress));
        Assert.Contains("now listening on port 3340", admin.Find(".success").TextContent);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_PoolCannotBeStarted_THEN_ReasonIsShown()
    {
        Provisioner.AddAsync(Arg.Any<PoolDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ProvisionResult.Failure("Port 3334 is already used by pool 'pplns'."));
        var admin = Render<AdminPage>();

        admin.Find("form").Submit();

        Assert.Contains("already used", admin.Find("p.notice[role=status]").TextContent);
    }

    [Theory]
    [InlineData(true, "stays stopped until you start it again")]
    [InlineData(false, "is not running")]
    public void GIVEN_Admin_WHEN_StopIsClicked_THEN_PoolIsStoppedAndOutcomeShown(bool stopped, string expectedMessage)
    {
        Overview.GetPools().Returns([PoolSnapshot("solo", PayoutScheme.Solo, 3333)]);
        Provisioner.StopAsync("solo", Arg.Any<CancellationToken>())
            .Returns(stopped ? ProvisionResult.Success : ProvisionResult.Failure("Pool 'solo' is not running."));
        var admin = Render<AdminPage>();

        admin.Find("button.danger").Click();

        Provisioner.Received(1).StopAsync("solo", Arg.Any<CancellationToken>());
        Assert.Contains(expectedMessage, admin.Find("[role=status]").TextContent);
    }

    private static ManagedPool Stopped(string name, PoolOrigin origin, int port = 3333) =>
        new(new PoolDefinition { Name = name, Scheme = PayoutScheme.Solo, Port = port }, origin, IsRunning: false);

    [Fact]
    public void GIVEN_Admin_WHEN_APoolIsStopped_THEN_ItIsStillListedAndCounted()
    {
        Overview.GetPools().Returns([PoolSnapshot("pplns", PayoutScheme.Pplns, 3334)]);
        Provisioner.GetPools().Returns(
        [
            Stopped("solo", PoolOrigin.Configuration),
            new ManagedPool(new PoolDefinition { Name = "pplns", Scheme = PayoutScheme.Pplns, Port = 3334 }, PoolOrigin.Configuration, IsRunning: true),
        ]);

        var admin = Render<AdminPage>();

        var row = Assert.Single(admin.FindAll("tr.stopped"));
        Assert.Contains("solo", row.TextContent);
        Assert.Contains("STOPPED", row.TextContent);
        Assert.Contains("3333", row.TextContent);
        Assert.Contains("1 running, 1 stopped", admin.Markup);
        Assert.Equal(2, admin.FindAll("table")[0].QuerySelectorAll("tbody tr").Length);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_StartIsClickedOnAStoppedPool_THEN_ItIsStartedAndConfirmed()
    {
        Provisioner.GetPools().Returns([Stopped("solo", PoolOrigin.Configuration)]);
        Provisioner.StartAsync("solo", Arg.Any<CancellationToken>()).Returns(ProvisionResult.Success);
        var admin = Render<AdminPage>();

        admin.Find("tr.stopped button.quiet").Click();

        Provisioner.Received(1).StartAsync("solo", Arg.Any<CancellationToken>());
        Assert.Contains("running again", admin.Find(".success").TextContent);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_AStoppedPoolCannotBeStarted_THEN_ReasonIsShown()
    {
        Provisioner.GetPools().Returns([Stopped("solo", PoolOrigin.Configuration)]);
        Provisioner.StartAsync("solo", Arg.Any<CancellationToken>()).Returns(ProvisionResult.Failure("Port 3333 is already used by pool 'other'."));
        var admin = Render<AdminPage>();

        admin.Find("tr.stopped button.quiet").Click();

        Assert.Contains("already used", admin.Find("p.notice[role=status]").TextContent);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_AStoppedPoolComesFromTheConfiguration_THEN_ItCannotBeDeletedHere()
    {
        Provisioner.GetPools().Returns([Stopped("solo", PoolOrigin.Configuration)]);

        var admin = Render<AdminPage>();

        Assert.Empty(admin.FindAll("tr.stopped button.danger"));
    }

    [Fact]
    public void GIVEN_Admin_WHEN_DeleteIsClickedOnAPoolCreatedHere_THEN_ItIsDeletedAndConfirmed()
    {
        Provisioner.GetPools().Returns([Stopped("extra", PoolOrigin.Administrator, 3337)]);
        Provisioner.DeleteAsync("extra", Arg.Any<CancellationToken>()).Returns(ProvisionResult.Success);
        var admin = Render<AdminPage>();

        admin.Find("tr.stopped button.danger").Click();

        Provisioner.Received(1).DeleteAsync("extra", Arg.Any<CancellationToken>());
        Assert.Contains("was deleted", admin.Find(".success").TextContent);
    }

    [Fact]
    public void GIVEN_StaticPages_WHEN_Rendered_THEN_ShowTheirHeadings()
    {
        Assert.Contains("Page not found", Render<NotFound>().Markup);
        Assert.Contains("Something went wrong", Render<Error>().Markup);
    }

    [Fact]
    public void GIVEN_MainLayout_WHEN_Rendered_THEN_ShowsBrandNavigationAndBody()
    {
        var layout = Render<MainLayout>(p => p.Add(l => l.Body, builder => builder.AddContent(0, "page body")));

        Assert.Equal("HashForge", layout.Find(".brand-name").TextContent);
        Assert.NotEmpty(layout.FindAll(".header-status .pill"));
        Assert.Equal(HashForge.Web.Branding.BrandingOptions.BuiltInLogo, layout.Find("img.brand-mark").GetAttribute("src"));
        Assert.Contains("Open-source Bitcoin mining pool", layout.Find("footer").TextContent);
        Assert.Contains("page body", layout.Find("main").TextContent);
        Assert.NotEmpty(layout.FindAll("nav a"));
    }

    [Theory]
    [InlineData(StatusKind.Good, "pill-good")]
    [InlineData(StatusKind.Bad, "pill-bad")]
    [InlineData(StatusKind.Busy, "pill-busy")]
    [InlineData(StatusKind.Neutral, "pill-neutral")]
    public void GIVEN_StatusPill_WHEN_KindGiven_THEN_ShowsTextWithMatchingStyle(StatusKind kind, string expectedClass)
    {
        var pill = Render<StatusPill>(p => p.Add(x => x.Kind, kind).Add(x => x.Text, "Accepted")).Find(".pill");

        Assert.Equal("Accepted", pill.TextContent);
        Assert.Contains(expectedClass, pill.ClassList);
    }

    [Theory]
    [InlineData(PayoutScheme.Solo, "SOLO")]
    [InlineData(PayoutScheme.Prop, "PROP")]
    [InlineData(PayoutScheme.Pplns, "PPLNS")]
    [InlineData(PayoutScheme.FinderBonus, "BONUS")]
    public void GIVEN_SchemeBadge_WHEN_SchemeGiven_THEN_ShowsUppercaseName(PayoutScheme scheme, string expected)
    {
        Assert.Equal(expected, Render<SchemeBadge>(p => p.Add(x => x.Scheme, scheme)).Find(".badge").TextContent);
    }

    [Theory]
    [InlineData(42.5, "42.5%", "width: 42.5%")]
    [InlineData(250, "250%", "width: 100%")]
    [InlineData(0, "0%", "width: 0%")]
    public void GIVEN_EffortMeter_WHEN_PercentGiven_THEN_ShowsValueAndBarCappedAtFull(double percent, string expectedText, string expectedStyle)
    {
        var meter = Render<EffortMeter>(p => p.Add(x => x.Percent, percent));

        Assert.Equal(expectedText, meter.Find(".effort > span").TextContent);
        Assert.Equal(expectedStyle, meter.Find(".meter > span").GetAttribute("style"));
    }

    [Fact]
    public void GIVEN_SectionCard_WHEN_AllPartsGiven_THEN_ShowsTitleAsideBodyAndFootnote()
    {
        var card = Render<SectionCard>(p => p
            .Add(x => x.Title, "Pools")
            .Add(x => x.Aside, "3 running")
            .Add(x => x.Footnote, "A note")
            .Add(x => x.Flush, true)
            .AddChildContent("body text"));

        Assert.Equal("Pools", card.Find("h2").TextContent);
        Assert.Equal("3 running", card.Find(".card-aside").TextContent);
        Assert.Equal("A note", card.Find(".card-foot").TextContent);
        Assert.Contains("body text", card.Find(".card-body.flush").TextContent);
    }

    [Fact]
    public void GIVEN_SectionCard_WHEN_OnlyTitleGiven_THEN_OptionalPartsAreOmitted()
    {
        var card = Render<SectionCard>(p => p.Add(x => x.Title, "Pools"));

        Assert.Empty(card.FindAll(".card-aside"));
        Assert.Empty(card.FindAll(".card-foot"));
        Assert.Empty(card.FindAll(".flush"));
    }

    [Fact]
    public void GIVEN_NodeStatus_WHEN_NodeIsSynced_THEN_ShowsChainAndHeight()
    {
        NetworkStats.Current.Returns(Network());

        var pill = Render<NodeStatus>().Find(".pill");

        Assert.Equal("main · block 850,000", pill.TextContent);
        Assert.Contains("pill-good", pill.ClassList);
    }

    [Fact]
    public void GIVEN_NodeStatus_WHEN_NodeIsSynchronising_THEN_ShowsProgress()
    {
        NetworkStats.Current.Returns(Network(synced: false));

        var pill = Render<NodeStatus>().Find(".pill");

        Assert.Equal("main · syncing 50%", pill.TextContent);
        Assert.Contains("pill-busy", pill.ClassList);
    }

    [Fact]
    public void GIVEN_NodeStatus_WHEN_NodeHasNotAnswered_THEN_ShowsConnecting()
    {
        NetworkStats.Current.Returns((HashForge.Mining.Network.NetworkStats?)null);

        Assert.Contains("Connecting to node", Render<NodeStatus>().Find(".pill-neutral").TextContent);
    }

    [Theory]
    [InlineData("Solo")]
    [InlineData("Prop")]
    [InlineData("Pplns")]
    public void GIVEN_Admin_WHEN_PayoutIsNotBonus_THEN_FinderBonusFieldIsHidden(string scheme)
    {
        var admin = Render<AdminPage>();

        admin.Find("select").Change(scheme);

        Assert.Empty(admin.FindAll("#finder-bonus"));
    }

    [Fact]
    public void GIVEN_Admin_WHEN_PayoutIsBonus_THEN_FinderBonusFieldAppearsAndItsAmountIsUsed()
    {
        PoolDefinition? requested = null;
        Provisioner.AddAsync(Arg.Do<PoolDefinition>(p => requested = p), Arg.Any<CancellationToken>()).Returns(ProvisionResult.Success);
        var admin = Render<AdminPage>();

        admin.Find("select").Change("FinderBonus");
        Assert.Equal("1", admin.Find("#finder-bonus").GetAttribute("value"));
        admin.Find("#finder-bonus").Change("0.25");
        admin.Find("form").Submit();

        Assert.Equal(PayoutScheme.FinderBonus, requested!.Scheme);
        Assert.Equal(25_000_000, requested.FinderBonusSatoshis);
    }

    [Fact]
    public void GIVEN_Admin_WHEN_BonusAmountEnteredThenPayoutChangedAway_THEN_AmountIsNotApplied()
    {
        PoolDefinition? requested = null;
        Provisioner.AddAsync(Arg.Do<PoolDefinition>(p => requested = p), Arg.Any<CancellationToken>()).Returns(ProvisionResult.Success);
        var admin = Render<AdminPage>();

        admin.Find("select").Change("FinderBonus");
        admin.Find("#finder-bonus").Change("5");
        admin.Find("select").Change("Prop");
        admin.Find("form").Submit();

        Assert.Equal(HashForge.Domain.PoolDefinition.DefaultFinderBonusSatoshis, requested!.FinderBonusSatoshis);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("bc1q'; DROP TABLE blocks;--")]
    [InlineData("../../admin")]
    [InlineData("javascript:alert(1)")]
    [InlineData("tooshort")]
    public void GIVEN_Pool_WHEN_SearchTextIsNotAnAddress_THEN_ShowsErrorAndDoesNotNavigate(string input)
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));
        var before = Services.GetRequiredService<NavigationManager>().Uri;

        pool.Find("#miner-address").Change(input);
        pool.Find("form").Submit();

        Assert.Equal(before, Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Contains("does not look like a Bitcoin address", pool.Find("[role=alert]").TextContent);
        Assert.Empty(pool.FindAll("script"));
        Assert.DoesNotContain(input, pool.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_ValidSearchFollowsInvalidOne_THEN_ErrorIsCleared()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));
        pool.Find("#miner-address").Change("not an address");
        pool.Find("form").Submit();

        pool.Find("#miner-address").Change("bc1qsearchsearchsearch");
        pool.Find("form").Submit();

        Assert.Empty(pool.FindAll("[role=alert]"));
    }

    [Fact]
    public void GIVEN_Pool_WHEN_Rendered_THEN_SearchBoxLimitsInputLength()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());

        var input = Render<Pool>(p => p.Add(x => x.Name, "pplns")).Find("#miner-address");

        Assert.Equal("90", input.GetAttribute("maxlength"));
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("bc1q\" onmouseover=\"alert(1)")]
    [InlineData("short")]
    public void GIVEN_Miner_WHEN_AddressInUrlIsNotAddressShaped_THEN_ItIsNeitherLookedUpNorDisplayed(string address)
    {
        var miner = Render<Miner>(p => p.Add(x => x.PoolName, "pplns").Add(x => x.Address, address));

        Assert.Contains("not valid", miner.Markup);
        Assert.Equal("Miner", miner.Find("h1").TextContent);
        Assert.DoesNotContain("onerror", miner.Markup);
        Assert.DoesNotContain("onmouseover", miner.Markup);
        Statistics.DidNotReceiveWithAnyArgs().GetMiner(default!, default!);
    }

    [Fact]
    public void GIVEN_Miner_WHEN_PoolNameInUrlContainsMarkup_THEN_ItIsEncodedInLinkAndText()
    {
        var miner = Render<Miner>(p => p.Add(x => x.PoolName, "a\"><script>x</script>").Add(x => x.Address, "bc1qalicealicealice"));

        Assert.Empty(miner.FindAll("script"));
        Assert.StartsWith("pool/a%22%3E%3Cscript", miner.Find("a.back-link").GetAttribute("href"));
    }

    [Fact]
    public void GIVEN_Icon_WHEN_KindIsNone_THEN_NothingIsRendered()
    {
        Assert.Empty(Render<Icon>(p => p.Add(i => i.Kind, IconKind.None)).FindAll("svg"));
    }

    [Fact]
    public void GIVEN_Icon_WHEN_KindGiven_THEN_RendersDecorativeSvgHiddenFromScreenReaders()
    {
        var svg = Render<Icon>(p => p.Add(i => i.Kind, IconKind.Hashrate)).Find("svg.icon");

        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.NotEmpty(svg.Children);
    }

    [Fact]
    public void GIVEN_IconShapes_WHEN_EveryKindIsRequested_THEN_EachHasADrawingExceptNone()
    {
        Assert.Equal(string.Empty, IconShapes.For(IconKind.None));
        Assert.All(Enum.GetValues<IconKind>().Where(k => k != IconKind.None), k => Assert.StartsWith("<", IconShapes.For(k)));
    }

    [Fact]
    public void GIVEN_StatTileAndSectionCard_WHEN_IconGiven_THEN_IconSitsBesideTheLabel()
    {
        var tile = Render<StatTile>(p => p.Add(t => t.Label, "Hashrate").Add(t => t.Value, "1 TH/s").Add(t => t.Icon, IconKind.Hashrate));
        var card = Render<SectionCard>(p => p.Add(c => c.Title, "Pools").Add(c => c.Icon, IconKind.Port));

        Assert.Single(tile.FindAll(".tile-label svg.icon"));
        Assert.Equal("Hashrate", tile.Find(".tile-label").TextContent);
        Assert.Single(card.FindAll("h2 svg.icon"));
        Assert.Equal("Pools", card.Find("h2").TextContent);
    }

    [Fact]
    public void GIVEN_Home_WHEN_NoWorkHasBeenFetchedYet_THEN_BlockRewardShowsADash()
    {
        NetworkStats.Current.Returns(Network());

        var home = Render<Home>();

        var reward = home.FindAll(".tile").Single(t => t.TextContent.Contains("Block reward"));
        Assert.Equal(Display.None, reward.QuerySelector(".tile-value")!.TextContent);
    }

    [Fact]
    public void GIVEN_Miner_WHEN_HistoryExists_THEN_ShowsHashrateChartWithoutMinerCounts()
    {
        Statistics.GetMiner("pplns", "bc1qalicealicealice").Returns(new MinerSnapshot("bc1qalicealicealice", Stats(), []));
        MinerHistory.Get("pplns", "bc1qalicealicealice").Returns(
        [
            new MinerHistorySample(Now.AddMinutes(-10).ToUnixTimeSeconds(), 4.1e12, 2),
            new MinerHistorySample(Now.AddMinutes(-5).ToUnixTimeSeconds(), 4.6e12, 2),
            new MinerHistorySample(Now.ToUnixTimeSeconds(), 4.8e12, 1),
        ]);

        var miner = Render<Miner>(p => p.Add(x => x.PoolName, "pplns").Add(x => x.Address, "bc1qalicealicealice"));

        Assert.Equal("Miner hashrate over time", miner.Find("svg[role=img]").GetAttribute("aria-label"));
        Assert.Equal(3, miner.FindAll("circle.chart-hit").Count);
        Assert.Contains("4.8 TH/s", miner.Find("text.chart-value").TextContent);
        Assert.Contains("last 24 hours", miner.Markup);
        Assert.DoesNotContain("Miners</th>", miner.Markup);
        Assert.DoesNotContain("miners</title>", miner.Markup);
    }

    [Fact]
    public void GIVEN_Miner_WHEN_NoHistoryYet_THEN_ChartShowsPlaceholder()
    {
        Statistics.GetMiner("pplns", "bc1qalicealicealice").Returns(new MinerSnapshot("bc1qalicealicealice", Stats(), []));

        var miner = Render<Miner>(p => p.Add(x => x.PoolName, "pplns").Add(x => x.Address, "bc1qalicealicealice"));

        Assert.Contains("The chart appears once a few readings have been collected", miner.Markup);
    }

    [Fact]
    public void GIVEN_Miner_WHEN_AddressIsInvalid_THEN_HistoryIsNeverLookedUp()
    {
        Render<Miner>(p => p.Add(x => x.PoolName, "pplns").Add(x => x.Address, "<script>"));

        MinerHistory.DidNotReceiveWithAnyArgs().Get(default!, default!);
    }

    private static readonly string BlockHash = new('a', 64);

    private static FoundBlock PaidBlock() => Block() with
    {
        RewardSatoshis = 5_000_000_000,
        Payouts =
        [
            new PayoutEntry("tb1qdlavq4cg7j0mnfsqtvv4mpkgu8mheclkpqc0n5", 4_950_000_000),
            new PayoutEntry("tb1qep7zndam7d6ucy4w9egy6wg2z5fm43qthy42p9", 50_000_000),
        ],
        MinersPaid = 1,
    };

    [Fact]
    public void GIVEN_BlockTable_WHEN_BlocksGiven_THEN_HeightLinksToTheBlockPage()
    {
        var table = Render<BlockTable>(p => p.Add(t => t.Blocks, [Block()]).Add(t => t.Now, Now));

        Assert.Equal($"block/{BlockHash}", table.Find("tbody tr td a").GetAttribute("href"));
    }

    [Fact]
    public void GIVEN_BlockDetails_WHEN_BlockExists_THEN_ShowsEachPayoutAmountAndMarksThePoolFee()
    {
        Blocks.FindAsync(BlockHash, Arg.Any<CancellationToken>()).Returns(PaidBlock());

        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash));

        var rows = page.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("49.50000000 BTC", rows[0].TextContent);
        Assert.Contains("Mining", rows[0].TextContent);
        Assert.Contains("99%", rows[0].TextContent);
        Assert.Contains("0.50000000 BTC", rows[1].TextContent);
        Assert.Contains("Pool fee", rows[1].TextContent);
        Assert.Contains("Block 850,001", page.Find("h1").TextContent);
    }

    [Fact]
    public void GIVEN_BlockDetails_WHEN_BlockExists_THEN_FullAddressesAppearNowhereInThePage()
    {
        Blocks.FindAsync(BlockHash, Arg.Any<CancellationToken>()).Returns(PaidBlock());

        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash));

        Assert.Contains("tb1qdlavq4…pqc0n5", page.Markup);
        Assert.DoesNotContain("tb1qdlavq4cg7j0mnfsqtvv4mpkgu8mheclkpqc0n5", page.Markup);
        Assert.DoesNotContain("tb1qep7zndam7d6ucy4w9egy6wg2z5fm43qthy42p9", page.Markup);
        Assert.DoesNotContain("bc1qalicealicealicealice", page.Markup);
    }

    [Theory]
    [InlineData("main", "View on mempool.space ↗", "https://mempool.space/block/")]
    [InlineData("testnet4", "View on mempool.space (testnet4) ↗", "https://mempool.space/testnet4/block/")]
    public void GIVEN_BlockDetails_WHEN_NodeIsOnAPublicNetwork_THEN_LinksToThatNetworksExplorer(string chain, string expectedText, string expectedPrefix)
    {
        Blocks.FindAsync(BlockHash, Arg.Any<CancellationToken>()).Returns(PaidBlock());
        NetworkStats.Current.Returns(Network() with { Chain = chain });

        var link = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash)).Find("a.explorer-link");

        Assert.Equal(expectedText, link.TextContent);
        Assert.Equal(expectedPrefix + BlockHash, link.GetAttribute("href"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Theory]
    [InlineData("regtest")]
    [InlineData(null)]
    public void GIVEN_BlockDetails_WHEN_NetworkHasNoPublicExplorer_THEN_NoExplorerLinkIsShown(string? chain)
    {
        Blocks.FindAsync(BlockHash, Arg.Any<CancellationToken>()).Returns(PaidBlock());
        NetworkStats.Current.Returns(chain is null ? null : Network() with { Chain = chain });

        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash));

        Assert.Empty(page.FindAll("a.explorer-link"));
    }

    [Fact]
    public void GIVEN_BlockDetails_WHEN_NoBlockHasThatHash_THEN_ShowsNotFound()
    {
        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash));

        Assert.Contains("Block not found", page.Find("h1").TextContent);
        Assert.Contains("No pool here has found a block with that hash", page.Markup);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("00ab")]
    public void GIVEN_BlockDetails_WHEN_HashInUrlIsNotABlockHash_THEN_ItIsNeitherLookedUpNorDisplayed(string hash)
    {
        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, hash));

        Assert.Contains("not a block hash", page.Markup);
        Assert.DoesNotContain(hash, page.Find("h1").TextContent);
        Blocks.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_PoolHasFoundBlocks_THEN_ListsThemWithoutAPoolColumn()
    {
        WebOptions.RecentBlockCount = 40;
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        Blocks.GetRecentForPoolAsync("pplns", 40, Arg.Any<CancellationToken>()).Returns([Block()]);

        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));

        var blocks = pool.FindAll("section.card").Single(card => card.QuerySelector("h2")!.TextContent == "Blocks found");
        Assert.Equal($"block/{BlockHash}", blocks.QuerySelector("tbody a")!.GetAttribute("href"));
        Assert.DoesNotContain("Pool", blocks.QuerySelectorAll("th").Select(th => th.TextContent));
        Assert.Contains("3.12500000 BTC", blocks.TextContent);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_PoolHasFoundNoBlocks_THEN_SaysSo()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());

        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));

        Assert.Contains("No blocks found yet", pool.Markup);
    }

    [Fact]
    public void GIVEN_BlockTable_WHEN_ShowPoolIsNotSet_THEN_PoolColumnIsShown()
    {
        var table = Render<BlockTable>(p => p.Add(t => t.Blocks, [Block()]).Add(t => t.Now, Now));

        Assert.Contains("Pool", table.FindAll("th").Select(th => th.TextContent));
    }

    [Theory]
    [InlineData(true, null, false, 0, "Accepted", "pill-busy")]
    [InlineData(true, null, false, 3, "Accepted", "pill-good")]
    [InlineData(true, null, false, 100, "Confirmed", "pill-good")]
    [InlineData(true, null, true, 0, "Orphaned", "pill-bad")]
    [InlineData(false, "inconclusive", false, 0, "Stale", "pill-bad")]
    [InlineData(false, "high-hash", false, 0, "Rejected: high-hash", "pill-bad")]
    public void GIVEN_BlockStatusPill_WHEN_BlockFateVaries_THEN_NamesItWithMatchingTone(
        bool accepted, string? rejectReason, bool orphaned, int confirmations, string expectedText, string expectedClass)
    {
        var block = Block() with { Accepted = accepted, RejectReason = rejectReason, Orphaned = orphaned, Confirmations = confirmations };

        var pill = Render<BlockStatusPill>(p => p.Add(x => x.Block, block)).Find(".pill");

        Assert.Equal(expectedText, pill.TextContent);
        Assert.Contains(expectedClass, pill.ClassList);
    }

    [Fact]
    public void GIVEN_BlockTable_WHEN_BlockWasOrphaned_THEN_StatusSaysOrphanedNotAccepted()
    {
        var table = Render<BlockTable>(p => p.Add(t => t.Blocks, [Block() with { Orphaned = true }]).Add(t => t.Now, Now));

        var status = table.Find("tbody tr").QuerySelectorAll("td")[^1].TextContent;
        Assert.Contains("Orphaned", status);
        Assert.DoesNotContain("Accepted", status);
    }

    [Fact]
    public void GIVEN_BlockDetails_WHEN_BlockWasOrphaned_THEN_SaysNothingWasPaidAndOffersNoExplorerLink()
    {
        Blocks.FindAsync(BlockHash, Arg.Any<CancellationToken>()).Returns(PaidBlock() with { Orphaned = true });
        NetworkStats.Current.Returns(Network());

        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash));

        Assert.Contains("Orphaned", page.Markup);
        Assert.Contains("these amounts were never paid", page.Markup);
        Assert.Empty(page.FindAll("a.explorer-link"));
        Assert.DoesNotContain("Confirmations", page.Markup);
    }

    [Fact]
    public void GIVEN_BlockDetails_WHEN_BlockIsOnTheChain_THEN_ShowsItsConfirmations()
    {
        Blocks.FindAsync(BlockHash, Arg.Any<CancellationToken>()).Returns(PaidBlock() with { Confirmations = 12 });

        var page = Render<BlockDetails>(p => p.Add(x => x.Hash, BlockHash));

        Assert.Contains("Confirmations", page.Markup);
        Assert.Contains("after 100 confirmations", page.Markup);
    }

    [Fact]
    public void GIVEN_Pool_WHEN_MinersAreListed_THEN_NoFullAddressIsInThePage()
    {
        Overview.FindPool("pplns").Returns(PoolSnapshot());
        Statistics.GetMiners("pplns").Returns([new MinerSnapshot("bc1qalicealicealicealicealice", Stats(), [new WorkerSnapshot("rig1", Stats())])]);

        var pool = Render<Pool>(p => p.Add(x => x.Name, "pplns"));

        Assert.DoesNotContain("bc1qalicealicealicealicealice", pool.Markup, StringComparison.Ordinal);
        Assert.Contains("Enter your own above", pool.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GIVEN_BlockTable_WHEN_Rendered_THEN_FindersFullAddressIsNotInThePage()
    {
        var table = Render<BlockTable>(p => p.Add(t => t.Blocks, [Block()]).Add(t => t.Now, Now));

        Assert.DoesNotContain("bc1qalicealicealicealice", table.Markup, StringComparison.Ordinal);
        Assert.Contains("bc1qalicea…", table.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GIVEN_Home_WHEN_PoolHasABestShare_THEN_ItsWorkersFullAddressIsNotInThePage()
    {
        const string address = "bc1qbobbobbobbobbobbobbobbobbob";
        var stats = Stats(best: new BestShare(7.7e12, $"{address}.s21", Now));
        Overview.GetPools().Returns([PoolSnapshot() with { Stats = stats }]);
        Overview.GetBestShare().Returns(stats.BestShare);

        var home = Render<Home>();

        Assert.DoesNotContain(address, home.Markup, StringComparison.Ordinal);
    }
}
