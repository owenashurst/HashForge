using HashForge.Domain.Formatting;

namespace HashForge.Domain.UnitTests;

public sealed class DisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0 H/s")]
    [InlineData(-5, "0 H/s")]
    [InlineData(double.NaN, "0 H/s")]
    [InlineData(0.5, "0.5 H/s")]
    [InlineData(999, "999 H/s")]
    [InlineData(1500, "1.5 kH/s")]
    [InlineData(2.5e12, "2.5 TH/s")]
    [InlineData(6.5e20, "650 EH/s")]
    [InlineData(1e30, "1000000000 ZH/s")]
    public void GIVEN_Hashrate_WHEN_ValueGiven_THEN_UsesLargestFittingUnit(double value, string expected)
    {
        Assert.Equal(expected, Display.Hashrate(value));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(512, "512")]
    [InlineData(65536, "65.54 K")]
    [InlineData(9.2e13, "92 T")]
    public void GIVEN_Difficulty_WHEN_ValueGiven_THEN_UsesMetricSuffix(double value, string expected)
    {
        Assert.Equal(expected, Display.Difficulty(value));
    }

    [Fact]
    public void GIVEN_SimpleFormatters_WHEN_ValuesGiven_THEN_ReturnReadableText()
    {
        Assert.Equal("12 GB", Display.Bytes(12_000_000_000));
        Assert.Equal("87.5%", Display.Percent(87.5));
        Assert.Equal("100%", Display.Percent(100));
        Assert.Equal("1,234,567", Display.Count(1_234_567));
        Assert.Equal("3.12500000 BTC", Display.Bitcoin(312_500_000));
        Assert.Equal("0.00000546 BTC", Display.Bitcoin(546));
    }

    [Theory]
    [InlineData(0, 0, "–")]
    [InlineData(500, 0, "0%")]
    [InlineData(5000, 25, "0.5%")]
    [InlineData(108_852, 37, "0.03%")]
    [InlineData(40_000_000, 1, "<0.01%")]
    [InlineData(0, 4, "100%")]
    public void GIVEN_RejectRate_WHEN_SharesVary_THEN_ShowsARateThatNeverHidesARejection(long accepted, long rejected, string expected)
    {
        Assert.Equal(expected, Display.RejectRate(accepted, rejected));
    }

    [Theory]
    [InlineData(0, "0s ago")]
    [InlineData(59, "59s ago")]
    [InlineData(60, "1m ago")]
    [InlineData(3599, "59m ago")]
    [InlineData(3600, "1h ago")]
    [InlineData(86400, "1d ago")]
    [InlineData(-30, "0s ago")]
    public void GIVEN_Age_WHEN_TimeHasPassed_THEN_UsesLargestWholeUnit(int secondsAgo, string expected)
    {
        Assert.Equal(expected, Display.Age(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void GIVEN_Age_WHEN_TimestampIsNull_THEN_ReturnsNever()
    {
        Assert.Equal("never", Display.Age(null, Now));
    }

    [Fact]
    public void GIVEN_Shorten_WHEN_ValueIsLong_THEN_KeepsStartAndEnd()
    {
        Assert.Equal("bc1qabcdef…uvwxyz", Display.Shorten("bc1qabcdefghijklmnopqrstuvwxyz"));
        Assert.Equal("short.rig1", Display.Shorten("short.rig1"));
    }

    [Fact]
    public void GIVEN_Shorten_WHEN_ValueEndsInLettersWithSeparateAccents_THEN_NoLetterLosesItsAccent()
    {
        var shortened = Display.Shorten("bc1qabcdefghijklmnop.riga\u030Aa\u030Aa\u030Aa\u030Aa\u030Aa\u030Aa\u030A");

        Assert.Equal("bc1qabcdef…a\u030Aa\u030Aa\u030Aa\u030Aa\u030Aa\u030A", shortened);
    }
}
