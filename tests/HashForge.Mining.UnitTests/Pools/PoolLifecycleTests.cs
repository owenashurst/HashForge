using HashForge.Domain;
using HashForge.Mining.Payouts;
using HashForge.Mining.Pools;
using Microsoft.Extensions.Logging.Abstractions;

namespace HashForge.Mining.UnitTests.Pools;

public sealed class PoolLifecycleTests
{
    private readonly PoolRegistry _registry = new();
    private readonly PoolCatalogue _catalogue = new();
    private readonly IPoolDefinitionStore _store = Substitute.For<IPoolDefinitionStore>();
    private readonly IStoppedPoolStore _stopped = Substitute.For<IStoppedPoolStore>();
    private readonly IPayoutSchemeRegistry _schemes = Substitute.For<IPayoutSchemeRegistry>();
    private readonly IAddressScriptResolver _resolver = Substitute.For<IAddressScriptResolver>();
    private readonly PoolProvisioner _provisioner;

    public PoolLifecycleTests()
    {
        _resolver.ResolveAsync(TestData.FeeAddress, Arg.Any<CancellationToken>())
            .Returns(new AddressResolution(true, [0x51], null));
        _store.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _stopped.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _provisioner = new PoolProvisioner(_registry, _catalogue, _store, _stopped, _resolver, _schemes, NullLogger<PoolProvisioner>.Instance);
    }

    private PoolBootstrapService Bootstrap(params PoolDefinition[] configured) =>
        new(_registry, _catalogue, _store, _stopped, _resolver, TestData.Options(o => { foreach (var p in configured) { o.Pools.Add(p); } }), NullLogger<PoolBootstrapService>.Instance);

    private ManagedPool Managed(string name) => _provisioner.GetPools().Single(pool => pool.Definition.Name == name);

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_PoolIsValid_THEN_PoolIsRegisteredAndStored()
    {
        var pool = TestData.Pool();

        var result = await _provisioner.AddAsync(pool, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Same(pool, _registry.Find(pool.Name));
        await _store.Received(1).SaveAsync(pool, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_DefinitionIsInvalid_THEN_FailsWithoutAskingNode()
    {
        var result = await _provisioner.AddAsync(TestData.Pool() with { Port = 0 }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("port", result.Error);
        await _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_NodeRejectsFeeAddress_THEN_FailsWithNodeReason()
    {
        _resolver.ResolveAsync("bad", Arg.Any<CancellationToken>()).Returns(new AddressResolution(false, null, "Invalid checksum"));

        var result = await _provisioner.AddAsync(TestData.Pool() with { FeeAddress = "bad" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Invalid checksum", result.Error);
        Assert.Empty(_registry.Pools);
    }

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_PortConflicts_THEN_FailsWithoutStoring()
    {
        await _provisioner.AddAsync(TestData.Pool("a"), CancellationToken.None);
        _store.ClearReceivedCalls();

        var result = await _provisioner.AddAsync(TestData.Pool("b"), CancellationToken.None);

        Assert.False(result.Succeeded);
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_NameOrPortBelongsToAStoppedPool_THEN_ItIsRefused()
    {
        await _provisioner.AddAsync(TestData.Pool("a", port: 1), CancellationToken.None);
        await _provisioner.StopAsync("a", CancellationToken.None);

        var sameName = await _provisioner.AddAsync(TestData.Pool("a", port: 2), CancellationToken.None);
        var samePort = await _provisioner.AddAsync(TestData.Pool("b", port: 1), CancellationToken.None);

        Assert.Contains("already exists", sameName.Error);
        Assert.Contains("already used by pool 'a'", samePort.Error);
        Assert.Empty(_registry.Pools);
    }

    [Fact]
    public async Task GIVEN_StopAsync_WHEN_PoolIsRunning_THEN_ItStopsButIsKeptAndRemembered()
    {
        await _provisioner.AddAsync(TestData.Pool("a"), CancellationToken.None);

        var result = await _provisioner.StopAsync("a", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(_registry.Pools);
        Assert.False(Managed("a").IsRunning);
        await _stopped.Received(1).AddAsync("a", Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_StopAsync_WHEN_PoolIsUnknownOrAlreadyStopped_THEN_ItFailsWithoutRememberingAnything()
    {
        await _provisioner.AddAsync(TestData.Pool("a"), CancellationToken.None);
        await _provisioner.StopAsync("a", CancellationToken.None);
        _stopped.ClearReceivedCalls();

        var unknown = await _provisioner.StopAsync("missing", CancellationToken.None);
        var again = await _provisioner.StopAsync("a", CancellationToken.None);

        Assert.Contains("no pool called 'missing'", unknown.Error);
        Assert.Contains("not running", again.Error);
        await _stopped.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_PoolWasStopped_THEN_ItRunsAgainAndIsNoLongerRememberedAsStopped()
    {
        var pool = TestData.Pool("a");
        await _provisioner.AddAsync(pool, CancellationToken.None);
        await _provisioner.StopAsync("a", CancellationToken.None);

        var result = await _provisioner.StartAsync("a", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Same(pool, _registry.Find("a"));
        Assert.True(Managed("a").IsRunning);
        await _stopped.Received(1).RemoveAsync("a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_PoolIsUnknownOrRunning_THEN_ItFails()
    {
        await _provisioner.AddAsync(TestData.Pool("a"), CancellationToken.None);

        var unknown = await _provisioner.StartAsync("missing", CancellationToken.None);
        var running = await _provisioner.StartAsync("a", CancellationToken.None);

        Assert.Contains("no pool called 'missing'", unknown.Error);
        Assert.Contains("already running", running.Error);
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_NodeNowRejectsTheFeeAddress_THEN_ItStaysStoppedWithTheReason()
    {
        await _provisioner.AddAsync(TestData.Pool("a"), CancellationToken.None);
        await _provisioner.StopAsync("a", CancellationToken.None);
        _resolver.ResolveAsync(TestData.FeeAddress, Arg.Any<CancellationToken>()).Returns(new AddressResolution(false, null, "Invalid prefix"));

        var result = await _provisioner.StartAsync("a", CancellationToken.None);

        Assert.Contains("Invalid prefix", result.Error);
        Assert.Empty(_registry.Pools);
        await _stopped.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GIVEN_DeleteAsync_WHEN_PoolWasCreatedByAnAdministrator_THEN_ItIsGoneForGood(bool stoppedFirst)
    {
        await _provisioner.AddAsync(TestData.Pool("a"), CancellationToken.None);
        if (stoppedFirst)
        {
            await _provisioner.StopAsync("a", CancellationToken.None);
        }

        var result = await _provisioner.DeleteAsync("a", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(_registry.Pools);
        Assert.Empty(_provisioner.GetPools());
        await _store.Received(1).DeleteAsync("a", Arg.Any<CancellationToken>());
        await _stopped.Received(1).RemoveAsync("a", Arg.Any<CancellationToken>());
        _schemes.Received(1).Forget("a");
    }

    [Fact]
    public async Task GIVEN_DeleteAsync_WHEN_PoolComesFromTheConfiguration_THEN_ItIsRefusedAndKeptRunning()
    {
        await Bootstrap(TestData.Pool("configured")).StartAsync(CancellationToken.None);

        var result = await _provisioner.DeleteAsync("configured", CancellationToken.None);

        Assert.Contains("configuration", result.Error);
        Assert.True(Managed("configured").IsRunning);
        Assert.False(Managed("configured").CanBeDeleted);
        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_DeleteAsync_WHEN_PoolIsUnknown_THEN_ItFailsWithoutTouchingStore()
    {
        var result = await _provisioner.DeleteAsync("missing", CancellationToken.None);

        Assert.False(result.Succeeded);
        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_GetPools_WHEN_PoolsComeFromBothPlaces_THEN_EachSaysWhereItCameFromOrderedByPort()
    {
        _store.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TestData.Pool("stored", port: 1)]);
        await Bootstrap(TestData.Pool("configured", port: 2)).StartAsync(CancellationToken.None);

        var pools = _provisioner.GetPools();

        Assert.Equal(
            [("stored", PoolOrigin.Administrator, true), ("configured", PoolOrigin.Configuration, true)],
            pools.Select(pool => (pool.Definition.Name, pool.Origin, pool.IsRunning)));
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_AnAdministratorStoppedAPool_THEN_ItIsKnownButNotStarted()
    {
        _stopped.GetAllAsync(Arg.Any<CancellationToken>()).Returns(["SOLO"]);

        await Bootstrap(TestData.Pool("solo", port: 1), TestData.Pool("prop", port: 2)).StartAsync(CancellationToken.None);

        Assert.Equal("prop", Assert.Single(_registry.Pools).Name);
        Assert.False(Managed("solo").IsRunning);
        await _stopped.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_AStoppedPoolWasSwitchedOffInTheConfiguration_THEN_ItIsForgottenSoItReturnsRunning()
    {
        _stopped.GetAllAsync(Arg.Any<CancellationToken>()).Returns(["solo"]);

        await Bootstrap(TestData.Pool("solo") with { Enabled = false }).StartAsync(CancellationToken.None);

        Assert.Empty(_provisioner.GetPools());
        await _stopped.Received(1).RemoveAsync("solo", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_StoredPoolSharesANameWithAConfiguredPool_THEN_OnlyTheConfiguredPoolExists()
    {
        _store.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TestData.Pool("bonus", port: 2)]);

        await Bootstrap(TestData.Pool("bonus", port: 1)).StartAsync(CancellationToken.None);

        var pool = Assert.Single(_provisioner.GetPools());
        Assert.Equal((1, PoolOrigin.Configuration), (pool.Definition.Port, pool.Origin));
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_ConfiguredAndStoredPoolsExist_THEN_BothAreRegistered()
    {
        _store.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TestData.Pool("stored", port: 2)]);

        await Bootstrap(TestData.Pool("configured", port: 1)).StartAsync(CancellationToken.None);

        Assert.Equal(["configured", "stored"], _registry.Pools.Select(p => p.Name));
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_PoolIsDisabled_THEN_ItIsNotRegistered()
    {
        await Bootstrap(TestData.Pool("off") with { Enabled = false }).StartAsync(CancellationToken.None);

        Assert.Empty(_registry.Pools);
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_StoredPoolConflictsWithConfiguredPool_THEN_ConfiguredPoolWins()
    {
        _store.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TestData.Pool("stored", port: 1)]);

        await Bootstrap(TestData.Pool("configured", port: 1)).StartAsync(CancellationToken.None);

        Assert.Equal("configured", Assert.Single(_registry.Pools).Name);
    }

    [Fact]
    public async Task GIVEN_StopAsync_WHEN_Called_THEN_CompletesWithoutChangingPools()
    {
        var service = Bootstrap(TestData.Pool("a"));
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.Single(_registry.Pools);
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_NodeRejectsAPoolsFeeAddress_THEN_ThatPoolIsRefusedAndOthersStart()
    {
        _resolver.ResolveAsync("wrong-network", Arg.Any<CancellationToken>()).Returns(new AddressResolution(false, null, "Invalid prefix"));

        await Bootstrap(TestData.Pool("bad", port: 1) with { FeeAddress = "wrong-network" }, TestData.Pool("good", port: 2))
            .StartAsync(CancellationToken.None);

        Assert.Equal("good", Assert.Single(_registry.Pools).Name);
    }

    [Fact]
    public async Task GIVEN_StartAsync_WHEN_PoolHasZeroFeeAndNoFeeAddress_THEN_ItStartsWithoutAskingTheNode()
    {
        await Bootstrap(TestData.Pool("free") with { FeePercent = 0, FeeAddress = "" }).StartAsync(CancellationToken.None);

        Assert.Equal("free", Assert.Single(_registry.Pools).Name);
        await _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_PoolHasZeroFeeAndNoFeeAddress_THEN_ItIsStartedWithoutAskingTheNode()
    {
        var result = await _provisioner.AddAsync(TestData.Pool("free") with { FeePercent = 0, FeeAddress = "" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        await _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task GIVEN_AddAsync_WHEN_PoolChargesAFeeButHasNoAddress_THEN_ItIsRefused()
    {
        var result = await _provisioner.AddAsync(TestData.Pool("paid") with { FeePercent = 1, FeeAddress = "" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("fee address is required", result.Error);
    }

    [Fact]
    public async Task GIVEN_FindProblemAsync_WHEN_NodeRejectsAddressWithoutReason_THEN_ReturnsDefaultReason()
    {
        _resolver.ResolveAsync("odd", Arg.Any<CancellationToken>()).Returns(new AddressResolution(false, null, null));

        var problem = await _resolver.FindProblemAsync(TestData.Pool() with { FeeAddress = "odd" }, CancellationToken.None);

        Assert.Equal(AddressResolution.DefaultError, problem);
        Assert.Null(await _resolver.FindProblemAsync(TestData.Pool(), CancellationToken.None));
    }}
