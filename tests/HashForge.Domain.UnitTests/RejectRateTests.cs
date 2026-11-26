namespace HashForge.Domain.UnitTests;

public sealed class RejectRateTests
{
    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(75, 25, 25)]
    [InlineData(0, 10, 100)]
    [InlineData(9_999, 1, 0.01)]
    public void GIVEN_Percent_WHEN_SharesWereHandedIn_THEN_IsRejectedOverEverythingHandedIn(long accepted, long rejected, double expected)
    {
        Assert.Equal(expected, RejectRate.Percent(accepted, rejected)!.Value, precision: 10);
    }

    [Fact]
    public void GIVEN_Percent_WHEN_NoSharesYet_THEN_ThereIsNoRate()
    {
        Assert.Null(RejectRate.Percent(0, 0));
    }

    [Fact]
    public void GIVEN_Percent_WHEN_TotalsAreNearTheLargestNumber_THEN_DoesNotOverflow()
    {
        Assert.Equal(50, RejectRate.Percent(long.MaxValue, long.MaxValue)!.Value, precision: 10);
    }
}
