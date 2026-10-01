using System;
using System.Globalization;
using Dyncamelo.Nodes.Coordination;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins the input rules behind TimelinerTask.SetProgress (clamping), TimelinerTask.SetActual
/// (date range) and Appearance.Focus (percent to a transparency fraction).
/// </summary>
public class ScheduleRulesTests
{
    // ------------------------------------------------------------ progress clamping

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(37.5, 37.5)]
    [InlineData(100.0, 100.0)]
    [InlineData(-5.0, 0.0)]
    [InlineData(-0.0001, 0.0)]
    [InlineData(120.0, 100.0)]
    [InlineData(100.0001, 100.0)]
    [InlineData(double.PositiveInfinity, 100.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void ClampPercent_KeepsProgressInsideZeroToHundred(double input, double expected)
    {
        Assert.Equal(expected, ScheduleRules.ClampPercent(input), 9);
    }

    [Fact]
    public void ClampPercent_NaN_IsAnErrorNotAZero()
    {
        var error = Assert.Throws<ArgumentException>(() => ScheduleRules.ClampPercent(double.NaN));
        Assert.Contains("0 to 100", error.Message);
    }

    [Fact]
    public void ClampPercent_AFractionIsNotRescaled()
    {
        // 0.5 means half a percent, not 50% — documented: the input is a percentage.
        Assert.Equal(0.5, ScheduleRules.ClampPercent(0.5), 9);
    }

    // ------------------------------------------------------------ percent -> fraction

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(85.0, 0.85)]
    [InlineData(50.0, 0.5)]
    [InlineData(100.0, 1.0)]
    [InlineData(12.5, 0.125)]
    public void PercentToFraction_ConvertsZeroToHundredToZeroToOne(double percent, double expected)
    {
        Assert.Equal(expected, ScheduleRules.PercentToFraction(percent, "otherTransparency"), 9);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(100.5)]
    [InlineData(250.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PercentToFraction_OutsideZeroToHundred_IsAnErrorThatNamesTheInput(double percent)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleRules.PercentToFraction(percent, "otherTransparency"));
        Assert.Equal("otherTransparency", error.ParamName);
        Assert.Contains("0 (opaque) to 100 (invisible)", error.Message);
    }

    [Fact]
    public void PercentToFraction_ErrorTextUsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = comma;
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleRules.PercentToFraction(100.5, "otherTransparency"));
            Assert.Contains("100.5", error.Message);
            Assert.DoesNotContain("100,5", error.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void PercentToFraction_TypingAFractionIsNotRescaled()
    {
        // 0.85 typed for 85 gives 0.0085 (almost opaque) — the node's description says percent.
        Assert.Equal(0.0085, ScheduleRules.PercentToFraction(0.85, "otherTransparency"), 9);
    }

    // ------------------------------------------------------------ date range

    [Fact]
    public void RequireDateRange_EndAfterStart_Passes()
    {
        ScheduleRules.RequireDateRange(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), "actual", "end");
    }

    [Fact]
    public void RequireDateRange_SameDay_Passes()
    {
        var day = new DateTime(2026, 3, 1);
        ScheduleRules.RequireDateRange(day, day, "actual", "end");
    }

    [Fact]
    public void RequireDateRange_EndBeforeStart_ExplainsWhichDatesAreWrong()
    {
        var error = Assert.Throws<ArgumentException>(
            () => ScheduleRules.RequireDateRange(new DateTime(2026, 3, 10), new DateTime(2026, 3, 1), "actual", "end"));
        Assert.Equal("end", error.ParamName);
        Assert.Contains("actual end date (2026-03-01)", error.Message);
        Assert.Contains("actual start date (2026-03-10)", error.Message);
    }

    [Fact]
    public void RequireDateRange_ErrorDatesAreCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var error = Assert.Throws<ArgumentException>(
                () => ScheduleRules.RequireDateRange(new DateTime(2026, 12, 31), new DateTime(2026, 1, 2), "actual", "end"));
            Assert.Contains("2026-01-02", error.Message);
            Assert.Contains("2026-12-31", error.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
