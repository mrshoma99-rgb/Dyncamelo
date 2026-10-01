using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class DateTimeExtraNodesTests
{
    private static readonly DateTime Friday = new DateTime(2026, 7, 10, 14, 30, 45);

    // ── DateTime.Components ─────────────────────────────────────────────────

    [Fact]
    public void Components_SplitsADateTime()
    {
        var parts = DateTimeExtraNodes.Components(Friday);

        Assert.Equal(
            new[] { "year", "month", "day", "hour", "minute", "second", "weekday", "dayOfYear", "isoWeek", "isoYear", "quarter" },
            parts.Keys);
        Assert.Equal(2026, parts["year"]);
        Assert.Equal(7, parts["month"]);
        Assert.Equal(10, parts["day"]);
        Assert.Equal(14, parts["hour"]);
        Assert.Equal(30, parts["minute"]);
        Assert.Equal(45, parts["second"]);
        Assert.Equal("Friday", parts["weekday"]);
        Assert.Equal(191, parts["dayOfYear"]);
        Assert.Equal(28, parts["isoWeek"]);
        Assert.Equal(2026, parts["isoYear"]);
        Assert.Equal(3, parts["quarter"]);
    }

    [Fact]
    public void Components_WeekdayIsTheEnglishNameWhateverTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            }
            catch (CultureNotFoundException)
            {
            }

            var names = Enumerable.Range(0, 7).Select(i => (string)DateTimeExtraNodes.Components(new DateTime(2026, 7, 6).AddDays(i))["weekday"]);
            Assert.Equal(new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }, names);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(2026, 1, 1, 2026, 1)]    // Thursday: week 1 of its own year
    [InlineData(2021, 1, 1, 2020, 53)]   // Friday: still the previous year's week 53
    [InlineData(2016, 1, 3, 2015, 53)]   // Sunday of that week
    [InlineData(2023, 1, 1, 2022, 52)]   // Sunday
    [InlineData(2024, 12, 30, 2025, 1)]  // Monday: already next year's week 1
    [InlineData(2018, 12, 31, 2019, 1)]
    [InlineData(2020, 12, 31, 2020, 53)] // Thursday
    [InlineData(2026, 7, 12, 2026, 28)]  // Sunday ends week 28
    [InlineData(2026, 7, 13, 2026, 29)]  // Monday starts week 29
    public void Components_IsoWeekFollowsTheThursdayRule(int year, int month, int day, int isoYear, int isoWeek)
    {
        var parts = DateTimeExtraNodes.Components(new DateTime(year, month, day));
        Assert.Equal(isoYear, parts["isoYear"]);
        Assert.Equal(isoWeek, parts["isoWeek"]);
    }

    [Fact]
    public void Components_IsoWeekAgreesWithTheFrameworkForEveryDayOfSeventyYears()
    {
        for (var day = new DateTime(1990, 1, 1); day < new DateTime(2060, 1, 1); day = day.AddDays(1))
        {
            var parts = DateTimeExtraNodes.Components(day);
            Assert.True(
                ISOWeek.GetWeekOfYear(day) == (int)parts["isoWeek"] && ISOWeek.GetYear(day) == (int)parts["isoYear"],
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " expected ISO " + ISOWeek.GetYear(day) + "-W" + ISOWeek.GetWeekOfYear(day) +
                " but got " + parts["isoYear"] + "-W" + parts["isoWeek"]);
        }
    }

    [Fact]
    public void Components_IsoWeekWorksAtTheEdgesOfTheCalendar()
    {
        var first = DateTimeExtraNodes.Components(DateTime.MinValue);
        Assert.Equal(1, first["isoYear"]);
        Assert.Equal(1, first["isoWeek"]);
        var last = DateTimeExtraNodes.Components(DateTime.MaxValue);
        Assert.Equal(9999, last["isoYear"]);
        Assert.Equal(52, last["isoWeek"]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(12, 4)]
    public void Components_Quarter(int month, int quarter)
    {
        Assert.Equal(quarter, DateTimeExtraNodes.Components(new DateTime(2026, month, 15))["quarter"]);
    }

    [Fact]
    public void Components_Midnight()
    {
        var parts = DateTimeExtraNodes.Components(new DateTime(2026, 3, 1));
        Assert.Equal(0, parts["hour"]);
        Assert.Equal(0, parts["minute"]);
        Assert.Equal(0, parts["second"]);
        Assert.Equal(60, parts["dayOfYear"]);
    }

    // ── Add* ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddMonths_ClampsTheDayToTheEndOfAShorterMonth()
    {
        Assert.Equal(new DateTime(2026, 2, 28), DateTimeExtraNodes.AddMonths(new DateTime(2026, 1, 31), 1));
        Assert.Equal(new DateTime(2028, 2, 29), DateTimeExtraNodes.AddMonths(new DateTime(2028, 1, 31), 1));
        Assert.Equal(new DateTime(2026, 7, 10, 14, 30, 45), DateTimeExtraNodes.AddMonths(new DateTime(2026, 1, 10, 14, 30, 45), 6));
    }

    [Fact]
    public void AddMonths_GoesBackAndAcrossYears()
    {
        Assert.Equal(new DateTime(2025, 11, 15), DateTimeExtraNodes.AddMonths(new DateTime(2026, 2, 15), -3));
        Assert.Equal(new DateTime(2028, 2, 15), DateTimeExtraNodes.AddMonths(new DateTime(2026, 2, 15), 24));
        Assert.Equal(Friday, DateTimeExtraNodes.AddMonths(Friday, 0));
    }

    [Fact]
    public void AddYears_HandlesLeapDays()
    {
        Assert.Equal(new DateTime(2029, 2, 28), DateTimeExtraNodes.AddYears(new DateTime(2028, 2, 29), 1));
        Assert.Equal(new DateTime(2032, 2, 29), DateTimeExtraNodes.AddYears(new DateTime(2028, 2, 29), 4));
        Assert.Equal(new DateTime(2016, 7, 10, 14, 30, 45), DateTimeExtraNodes.AddYears(Friday, -10));
    }

    [Fact]
    public void AddHoursAndMinutes_AcceptFractionsAndNegatives()
    {
        Assert.Equal(new DateTime(2026, 7, 10, 16, 0, 45), DateTimeExtraNodes.AddHours(Friday, 1.5));
        Assert.Equal(new DateTime(2026, 7, 9, 22, 30, 45), DateTimeExtraNodes.AddHours(Friday, -16));
        Assert.Equal(new DateTime(2026, 7, 10, 16, 0, 45), DateTimeExtraNodes.AddMinutes(Friday, 90));
        Assert.Equal(new DateTime(2026, 7, 10, 14, 30, 15), DateTimeExtraNodes.AddMinutes(Friday, -0.5));
    }

    [Fact]
    public void Add_OutOfRange_GivesAClearMessage()
    {
        var months = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddMonths(DateTime.MaxValue, 1));
        Assert.Contains("DateTime.AddMonths", months.Message);
        Assert.Contains("outside the supported dates", months.Message);
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddYears(new DateTime(9999, 1, 1), 1));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddMonths(Friday, int.MaxValue));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddHours(DateTime.MinValue, -1));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddMinutes(DateTime.MaxValue, 1));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddHours(Friday, 1e300));
    }

    [Fact]
    public void AddHoursAndMinutes_NotANumber_Throws()
    {
        var hours = Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.AddHours(Friday, double.NaN));
        Assert.Contains("DateTime.AddHours: hours must be a finite number", hours.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.AddMinutes(Friday, double.PositiveInfinity));
    }

    // ── Today / Compare ─────────────────────────────────────────────────────

    [Fact]
    public void Today_IsLocalMidnight()
    {
        var before = DateTime.Today;
        var today = DateTimeExtraNodes.Today();
        var after = DateTime.Today;

        Assert.Equal(TimeSpan.Zero, today.TimeOfDay);
        Assert.True(today == before || today == after);
    }

    [Fact]
    public void Compare_ReturnsMinusOneZeroOrOne()
    {
        Assert.Equal(-1, DateTimeExtraNodes.Compare(new DateTime(2026, 1, 1), new DateTime(2026, 1, 2)));
        Assert.Equal(0, DateTimeExtraNodes.Compare(Friday, Friday));
        Assert.Equal(1, DateTimeExtraNodes.Compare(new DateTime(2027, 1, 1), new DateTime(2026, 12, 31, 23, 59, 59)));
    }

    [Fact]
    public void Compare_UsesTicksOnly()
    {
        var utc = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(0, DateTimeExtraNodes.Compare(utc, unspecified));
        Assert.Equal(-1, DateTimeExtraNodes.Compare(utc, utc.AddTicks(1)));
    }

    // ── StartOf ─────────────────────────────────────────────────────────────

    [Fact]
    public void StartOf_Day()
    {
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeExtraNodes.StartOf(Friday, "day"));
    }

    [Fact]
    public void StartOf_WeekStartsOnMondayByDefault()
    {
        Assert.Equal(new DateTime(2026, 7, 6), DateTimeExtraNodes.StartOf(Friday));
        Assert.Equal(new DateTime(2026, 7, 6), DateTimeExtraNodes.StartOf(Friday, "week", "Monday"));
        Assert.Equal(new DateTime(2026, 7, 6), DateTimeExtraNodes.StartOf(new DateTime(2026, 7, 6, 23, 59, 59)));
        Assert.Equal(new DateTime(2026, 7, 6), DateTimeExtraNodes.StartOf(new DateTime(2026, 7, 12, 8, 0, 0)));
    }

    [Fact]
    public void StartOf_WeekCanStartOnSunday()
    {
        Assert.Equal(new DateTime(2026, 7, 5), DateTimeExtraNodes.StartOf(Friday, "week", "Sunday"));
        Assert.Equal(new DateTime(2026, 7, 12), DateTimeExtraNodes.StartOf(new DateTime(2026, 7, 12, 8, 0, 0), "week", "sunday"));
        Assert.Equal(new DateTime(2026, 7, 5), DateTimeExtraNodes.StartOf(new DateTime(2026, 7, 6), "week", "Sunday"));
    }

    [Theory]
    [InlineData(2026, 1, 1, 2026, 1, 1)]
    [InlineData(2026, 2, 28, 2026, 1, 1)]
    [InlineData(2026, 4, 1, 2026, 4, 1)]
    [InlineData(2026, 5, 15, 2026, 4, 1)]
    [InlineData(2026, 7, 10, 2026, 7, 1)]
    [InlineData(2026, 12, 31, 2026, 10, 1)]
    public void StartOf_Quarter(int y, int m, int d, int ey, int em, int ed)
    {
        Assert.Equal(new DateTime(ey, em, ed), DateTimeExtraNodes.StartOf(new DateTime(y, m, d, 13, 0, 0), "quarter"));
    }

    [Fact]
    public void StartOf_MonthAndYear()
    {
        Assert.Equal(new DateTime(2026, 7, 1), DateTimeExtraNodes.StartOf(Friday, "month"));
        Assert.Equal(new DateTime(2026, 1, 1), DateTimeExtraNodes.StartOf(Friday, "year"));
        Assert.Equal(new DateTime(2028, 1, 1), DateTimeExtraNodes.StartOf(new DateTime(2028, 12, 31, 23, 0, 0), "year"));
    }

    [Fact]
    public void StartOf_UnitIsCaseInsensitive_AndBlankMeansWeek()
    {
        Assert.Equal(new DateTime(2026, 7, 1), DateTimeExtraNodes.StartOf(Friday, "MONTH"));
        Assert.Equal(new DateTime(2026, 7, 6), DateTimeExtraNodes.StartOf(Friday, null!, null!));
        Assert.Equal(new DateTime(2026, 7, 6), DateTimeExtraNodes.StartOf(Friday, " ", " "));
    }

    [Fact]
    public void StartOf_KeepsTheDateTimeKind()
    {
        Assert.Equal(DateTimeKind.Utc, DateTimeExtraNodes.StartOf(new DateTime(2026, 7, 10, 5, 0, 0, DateTimeKind.Utc), "quarter").Kind);
        Assert.Equal(DateTimeKind.Local, DateTimeExtraNodes.StartOf(new DateTime(2026, 7, 10, 5, 0, 0, DateTimeKind.Local), "week").Kind);
    }

    [Fact]
    public void StartOf_BadOptions_ListTheChoices()
    {
        var unit = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.StartOf(Friday, "fortnight"));
        Assert.Contains("unit must be", unit.Message);
        Assert.Contains("fortnight", unit.Message);
        var day = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.StartOf(Friday, "week", "Saturday"));
        Assert.Contains("firstDayOfWeek must be \"Monday\" or \"Sunday\"", day.Message);
    }

    [Fact]
    public void StartOf_BeforeTheFirstDate_GivesAClearMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.StartOf(DateTime.MinValue, "week", "Sunday"));
        Assert.Contains("DateTime.StartOf", ex.Message);
    }

    // ── EndOf ───────────────────────────────────────────────────────────────

    [Fact]
    public void EndOf_IsTheNextPeriodStartMinusOneMillisecond()
    {
        var lastMs = TimeSpan.FromMilliseconds(999);
        Assert.Equal(new DateTime(2026, 7, 10, 23, 59, 59) + lastMs, DateTimeExtraNodes.EndOf(Friday, "day"));
        Assert.Equal(new DateTime(2026, 7, 12, 23, 59, 59) + lastMs, DateTimeExtraNodes.EndOf(Friday));
        Assert.Equal(new DateTime(2026, 7, 11, 23, 59, 59) + lastMs, DateTimeExtraNodes.EndOf(Friday, "week", "Sunday"));
        Assert.Equal(new DateTime(2026, 7, 31, 23, 59, 59) + lastMs, DateTimeExtraNodes.EndOf(Friday, "month"));
        Assert.Equal(new DateTime(2026, 9, 30, 23, 59, 59) + lastMs, DateTimeExtraNodes.EndOf(Friday, "quarter"));
        Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 59) + lastMs, DateTimeExtraNodes.EndOf(Friday, "year"));
    }

    [Fact]
    public void EndOf_MonthKnowsLeapYears()
    {
        Assert.Equal(new DateTime(2028, 2, 29, 23, 59, 59, 999), DateTimeExtraNodes.EndOf(new DateTime(2028, 2, 3), "month"));
        Assert.Equal(new DateTime(2026, 2, 28, 23, 59, 59, 999), DateTimeExtraNodes.EndOf(new DateTime(2026, 2, 3), "month"));
    }

    [Fact]
    public void EndOf_IsInsideItsPeriodAndTheNextMomentIsNot()
    {
        var end = DateTimeExtraNodes.EndOf(Friday, "week");
        Assert.Equal(DateTimeExtraNodes.StartOf(Friday, "week"), DateTimeExtraNodes.StartOf(end, "week"));
        Assert.NotEqual(DateTimeExtraNodes.StartOf(Friday, "week"), DateTimeExtraNodes.StartOf(end.AddMilliseconds(1), "week"));
    }

    [Fact]
    public void EndOf_BadOptionsAndTheLastYear()
    {
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.EndOf(Friday, "decade"));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.EndOf(Friday, "week", "Friday"));
        var ex = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.EndOf(new DateTime(9999, 12, 31), "year"));
        Assert.Contains("DateTime.EndOf", ex.Message);
    }

    // ── Range ───────────────────────────────────────────────────────────────

    [Fact]
    public void Range_IsInclusive()
    {
        var days = DateTimeExtraNodes.Range(new DateTime(2026, 7, 1), new DateTime(2026, 7, 5));
        Assert.Equal(Enumerable.Range(1, 5).Select(d => new DateTime(2026, 7, d)), days);
    }

    [Fact]
    public void Range_StepsByDays_AndStopsBeforeAnEndTheStepSkips()
    {
        Assert.Equal(
            new[] { new DateTime(2026, 7, 1), new DateTime(2026, 7, 3), new DateTime(2026, 7, 5) },
            DateTimeExtraNodes.Range(new DateTime(2026, 7, 1), new DateTime(2026, 7, 5), 2));
        Assert.Equal(
            new[] { new DateTime(2026, 7, 1), new DateTime(2026, 7, 3), new DateTime(2026, 7, 5) },
            DateTimeExtraNodes.Range(new DateTime(2026, 7, 1), new DateTime(2026, 7, 6), 2));
    }

    [Fact]
    public void Range_FractionalStep()
    {
        var items = DateTimeExtraNodes.Range(new DateTime(2026, 7, 1), new DateTime(2026, 7, 2), 0.5);
        Assert.Equal(
            new[] { new DateTime(2026, 7, 1), new DateTime(2026, 7, 1, 12, 0, 0), new DateTime(2026, 7, 2) },
            items);
    }

    [Fact]
    public void Range_WeeklyStepKeepsTheTimeOfDay()
    {
        var items = DateTimeExtraNodes.Range(new DateTime(2026, 7, 1, 9, 30, 0), new DateTime(2026, 7, 22, 9, 30, 0), 7);
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.Equal(new TimeSpan(9, 30, 0), i.TimeOfDay));
        Assert.Equal(new DateTime(2026, 7, 22, 9, 30, 0), items.Last());
    }

    [Fact]
    public void Range_SingleItemAndEmpty()
    {
        var day = new DateTime(2026, 7, 1);
        Assert.Equal(new[] { day }, DateTimeExtraNodes.Range(day, day));
        Assert.Equal(new[] { day }, DateTimeExtraNodes.Range(day, day.AddDays(1), 5));
        Assert.Empty(DateTimeExtraNodes.Range(day, day.AddDays(-1)));
        Assert.Equal(new[] { day }, DateTimeExtraNodes.Range(day, day.AddDays(3), 1e300));
    }

    [Fact]
    public void Range_KeepsTheStartKind()
    {
        var items = DateTimeExtraNodes.Range(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.All(items, i => Assert.Equal(DateTimeKind.Utc, i.Kind));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Range_StepMustBePositive(double step)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.Range(new DateTime(2026, 1, 1), new DateTime(2026, 1, 5), step));
        Assert.Contains("stepDays must be a number greater than 0", ex.Message);
    }

    [Fact]
    public void Range_RefusesMoreThan100000Items()
    {
        var start = new DateTime(2000, 1, 1);
        Assert.Equal(100000, DateTimeExtraNodes.Range(start, start.AddDays(99999)).Count);

        var ex = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Range(start, start.AddDays(100000)));
        Assert.Contains("100001", ex.Message);
        Assert.Contains("100000", ex.Message);

        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Range(start, start.AddDays(365), 0.001));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Range(start, start.AddDays(1), 1e-15));
    }

    // ── AgeInDays ───────────────────────────────────────────────────────────

    [Fact]
    public void AgeInDays_IsReferenceMinusDate()
    {
        Assert.Equal(10.5, DateTimeExtraNodes.AgeInDays(new DateTime(2026, 7, 1), new DateTime(2026, 7, 11, 12, 0, 0)));
        Assert.Equal(0d, DateTimeExtraNodes.AgeInDays(Friday, Friday));
        Assert.Equal(-2d, DateTimeExtraNodes.AgeInDays(new DateTime(2026, 7, 3), new DateTime(2026, 7, 1)));
        Assert.Equal(-DateTimeNodes.DaysBetween(Friday, Friday.AddDays(3)), -DateTimeExtraNodes.AgeInDays(Friday, Friday.AddDays(3)));
    }

    // ── Unix time ───────────────────────────────────────────────────────────

    [Fact]
    public void ToUnixSeconds_UsesTheEpochInUtc()
    {
        Assert.Equal(0d, DateTimeExtraNodes.ToUnixSeconds(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(-1d, DateTimeExtraNodes.ToUnixSeconds(new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
        Assert.Equal(
            (double)new DateTimeOffset(2026, 7, 10, 14, 30, 45, TimeSpan.Zero).ToUnixTimeSeconds(),
            DateTimeExtraNodes.ToUnixSeconds(new DateTime(2026, 7, 10, 14, 30, 45, DateTimeKind.Utc)));
    }

    [Fact]
    public void ToUnixSeconds_ADateWithoutAZoneIsTakenAsUtc()
    {
        Assert.Equal(
            DateTimeExtraNodes.ToUnixSeconds(new DateTime(2026, 7, 10, 14, 30, 45, DateTimeKind.Utc)),
            DateTimeExtraNodes.ToUnixSeconds(Friday));
        Assert.Equal(1783693845d, DateTimeExtraNodes.ToUnixSeconds(Friday));
    }

    [Fact]
    public void ToUnixSeconds_ALocalDateIsConvertedToUtc()
    {
        var local = new DateTime(2026, 7, 10, 14, 30, 45, DateTimeKind.Local);
        Assert.Equal((double)new DateTimeOffset(local).ToUnixTimeSeconds(), DateTimeExtraNodes.ToUnixSeconds(local));
    }

    [Fact]
    public void ToUnixSeconds_KeepsFractionsOfASecond()
    {
        Assert.Equal(0.5, DateTimeExtraNodes.ToUnixSeconds(new DateTime(1970, 1, 1, 0, 0, 0, 500, DateTimeKind.Utc)));
    }

    [Fact]
    public void FromUnixSeconds_ReturnsUtcDateTimes()
    {
        var epoch = DateTimeExtraNodes.FromUnixSeconds(0);
        Assert.Equal(new DateTime(1970, 1, 1), epoch);
        Assert.Equal(DateTimeKind.Utc, epoch.Kind);
        Assert.Equal(new DateTime(2026, 7, 10, 14, 30, 45), DateTimeExtraNodes.FromUnixSeconds(1783693845));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, DateTimeExtraNodes.FromUnixSeconds(1700000000));
        Assert.Equal(new DateTime(1969, 12, 31, 23, 59, 59), DateTimeExtraNodes.FromUnixSeconds(-1));
    }

    [Fact]
    public void FromUnixSeconds_KeepsFractions()
    {
        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, 500), DateTimeExtraNodes.FromUnixSeconds(0.5));
    }

    [Fact]
    public void FromUnixSeconds_RoundTripsWithToUnixSeconds()
    {
        var value = new DateTime(2026, 7, 10, 14, 30, 45, 250, DateTimeKind.Utc);
        Assert.Equal(value, DateTimeExtraNodes.FromUnixSeconds(DateTimeExtraNodes.ToUnixSeconds(value)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FromUnixSeconds_NotFinite_Throws(double seconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.FromUnixSeconds(seconds));
        Assert.Contains("finite number", ex.Message);
    }

    [Theory]
    [InlineData(1e12)]
    [InlineData(-1e12)]
    [InlineData(1e300)]
    public void FromUnixSeconds_OutOfRange_Throws(double seconds)
    {
        var ex = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.FromUnixSeconds(seconds));
        Assert.Contains("DateTime.FromUnixSeconds", ex.Message);
        Assert.Contains("outside the supported dates", ex.Message);
    }

    // ── Roles and the engine ────────────────────────────────────────────────

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static ZeroTouchNodeModel Create(NodeRegistry registry, string nodeName)
    {
        return new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == nodeName));
    }

    private static void Wire(GraphModel graph, NodeModel from, NodeModel to, string inPort)
    {
        var target = to.InPorts.First(p => p.Name == inPort);
        Assert.True(graph.Connect(from.OutPorts[0], target).Success, "could not wire into " + to.Name + "." + inPort);
    }

    [Fact]
    public void RegisterAll_ImportsEveryDateTimeExtraNode()
    {
        var names = CreateRegistry().Definitions.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "DateTime.Components", "DateTime.AddMonths", "DateTime.AddYears", "DateTime.AddHours", "DateTime.AddMinutes",
            "DateTime.Today", "DateTime.Compare", "DateTime.StartOf", "DateTime.EndOf", "DateTime.Range",
            "DateTime.AgeInDays", "DateTime.ToUnixSeconds", "DateTime.FromUnixSeconds",
        })
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void Roles_MeasuringNodesAreInfo_AndTheRestCreate()
    {
        var definitions = CreateRegistry().Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);

        foreach (var name in new[] { "DateTime.Components", "DateTime.Compare", "DateTime.AgeInDays" })
        {
            Assert.Equal(NodeFunction.Info, definitions[name].Function);
        }

        foreach (var name in new[] { "DateTime.AddMonths", "DateTime.AddYears", "DateTime.AddHours", "DateTime.AddMinutes", "DateTime.Today", "DateTime.StartOf", "DateTime.EndOf", "DateTime.Range", "DateTime.ToUnixSeconds", "DateTime.FromUnixSeconds" })
        {
            Assert.Equal(NodeFunction.Create, definitions[name].Function);
        }
    }

    [Fact]
    public void Definitions_ExposeChoicesAndKinds()
    {
        var definitions = CreateRegistry().Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);

        Assert.Equal(new[] { "day", "week", "month", "quarter", "year" }, definitions["DateTime.StartOf"].Inputs.Single(i => i.Name == "unit").Choices);
        Assert.Equal(new[] { "Monday", "Sunday" }, definitions["DateTime.EndOf"].Inputs.Single(i => i.Name == "firstDayOfWeek").Choices);
        Assert.Equal(
            new[] { "year", "month", "day", "hour", "minute", "second", "weekday", "dayOfYear", "isoWeek", "isoYear", "quarter" },
            definitions["DateTime.Components"].Outputs.Select(o => o.Name));
    }

    [Fact]
    public void Engine_ByDateThroughComponentsAndStartOf()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var year = new NumberInputNode { Value = 2026 };
        var month = new NumberInputNode { Value = 7 };
        var day = new NumberInputNode { Value = 10 };
        var byDate = Create(registry, "DateTime.ByDate");
        var components = Create(registry, "DateTime.Components");
        var startOfWeek = Create(registry, "DateTime.StartOf");
        foreach (var node in new NodeModel[] { year, month, day, byDate, components, startOfWeek })
        {
            graph.AddNode(node);
        }

        Wire(graph, year, byDate, "year");
        Wire(graph, month, byDate, "month");
        Wire(graph, day, byDate, "day");
        Wire(graph, byDate, components, "dateTime");
        Wire(graph, byDate, startOfWeek, "dateTime");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal("Friday", components.OutPorts.First(p => p.Name == "weekday").Value);
        Assert.Equal(28, components.OutPorts.First(p => p.Name == "isoWeek").Value);
        Assert.Equal(new DateTime(2026, 7, 6), startOfWeek.OutPorts[0].Value);
    }

    [Fact]
    public void Engine_AddMonthsReplicatesOverAList()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var date = new StringInputNode { Value = "2026-01-31" };
        var parse = Create(registry, "DateTime.Parse");
        var months = new ListCreateNode();
        var one = new NumberInputNode { Value = 1 };
        var two = new NumberInputNode { Value = 2 };
        var add = Create(registry, "DateTime.AddMonths");
        foreach (var node in new NodeModel[] { date, parse, months, one, two, add })
        {
            graph.AddNode(node);
        }

        months.AddItemPort();
        Wire(graph, date, parse, "text");
        Assert.True(graph.Connect(one.OutPorts[0], months.InPorts[0]).Success);
        Assert.True(graph.Connect(two.OutPorts[0], months.InPorts[1]).Success);
        Wire(graph, parse, add, "dateTime");
        Wire(graph, months, add, "months");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(
            new object?[] { new DateTime(2026, 2, 28), new DateTime(2026, 3, 31) },
            Assert.IsAssignableFrom<IEnumerable<object?>>(add.OutPorts[0].Value).ToArray());
    }

    [Fact]
    public void Engine_RangeBeyondTheLimitBecomesARedNode()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var start = new StringInputNode { Value = "2000-01-01" };
        var end = new StringInputNode { Value = "2999-01-01" };
        var parseStart = Create(registry, "DateTime.Parse");
        var parseEnd = Create(registry, "DateTime.Parse");
        var range = Create(registry, "DateTime.Range");
        foreach (var node in new NodeModel[] { start, end, parseStart, parseEnd, range })
        {
            graph.AddNode(node);
        }

        Wire(graph, start, parseStart, "text");
        Wire(graph, end, parseEnd, "text");
        Wire(graph, parseStart, range, "start");
        Wire(graph, parseEnd, range, "end");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success); // the run completes; the failure is on the node
        Assert.Equal(NodeState.Error, range.State);
        Assert.Contains("100000", range.StateMessage);
    }
}
