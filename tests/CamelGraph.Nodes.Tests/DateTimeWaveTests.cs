using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// DateTime.Add, DateTime.Difference, DateTime.AddWorkdays, DateTime.IsWeekend, DateTime.FromExcelSerial, the unit on
/// DateTime.Range, day-first DateTime.Parse and the retirement of the five Add nodes and AgeInDays (VAL-17, VAL-20, VAL-30,
/// VAL-33, VAL-34, ENG-15, COL-36, SYS-34). Saved graphs that name a retired node or an old id must keep running.
/// </summary>
public class DateTimeWaveTests
{
    private static readonly DateTime Friday = new DateTime(2026, 7, 10, 14, 30, 45);

    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static string Warnings(NodeModel node) =>
        string.Join(" | ", node.Messages.Where(m => m.Severity == MessageSeverity.Warning).Select(m => m.Text));

    // ------------------------------------------------------------ DateTime.Add (VAL-20)

    [Theory]
    [InlineData(30, "seconds", "2026-07-10 14:31:15")]
    [InlineData(90, "minutes", "2026-07-10 16:00:45")]
    [InlineData(1.5, "hours", "2026-07-10 16:00:45")]
    [InlineData(-1, "days", "2026-07-09 14:30:45")]
    [InlineData(0.5, "days", "2026-07-11 02:30:45")]
    [InlineData(2, "weeks", "2026-07-24 14:30:45")]
    [InlineData(6, "months", "2027-01-10 14:30:45")]
    [InlineData(-10, "years", "2016-07-10 14:30:45")]
    [InlineData(1, "WEEK", "2026-07-17 14:30:45")]
    public void AddMovesByTheChosenUnit(double amount, string unit, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DateTimeExtraNodes.Add(Friday, amount, unit));
    }

    [Fact]
    public void AddDefaultsToDays_AndClampsMonthEnds()
    {
        Assert.Equal(Friday.AddDays(3), DateTimeExtraNodes.Add(Friday, 3));
        Assert.Equal(new DateTime(2026, 2, 28), DateTimeExtraNodes.Add(new DateTime(2026, 1, 31), 1, "months"));
        Assert.Equal(new DateTime(2029, 2, 28), DateTimeExtraNodes.Add(new DateTime(2028, 2, 29), 1, "years"));
    }

    [Fact]
    public void AddSaysPlainThingsAboutNanInfinityRangeAndUnits()
    {
        var nan = Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.Add(Friday, double.NaN, "days"));
        Assert.Contains("DateTime.Add: amount must be a finite number", nan.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.Add(Friday, double.PositiveInfinity, "hours"));

        var range = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Add(Friday, 1e9, "days"));
        Assert.Contains("outside the supported dates", range.Message);
        Assert.DoesNotContain("Parameter", range.Message);
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Add(Friday, 1e10, "months"));

        var unit = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Add(Friday, 1, "fortnights"));
        Assert.Contains("fortnights", unit.Message);
        Assert.Contains("months or years", unit.Message);
    }

    [Fact]
    public void AFractionOfAMonthIsRoundedAndSaidSo()
    {
        var node = EngineRun.Run("DateTime.Add", -1, new DateTime(2026, 1, 15), 1.5, "months");

        Assert.Equal(new DateTime(2026, 3, 15), node.OutPorts[0].Value);
        Assert.Contains("Whole months only: 1.5 was rounded to 2", Warnings(node));

        Assert.Empty(EngineRun.Run("DateTime.Add", -1, new DateTime(2026, 1, 15), 2.0, "months").Messages);
        Assert.Empty(EngineRun.Run("DateTime.Add", -1, new DateTime(2026, 1, 15), 1.5, "days").Messages);
    }

    [Fact]
    public void AddMapsOverAColumnOfDates()
    {
        var node = EngineRun.Run("DateTime.Add", -1, L(new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)), 1, "months");

        Assert.Equal(L(new DateTime(2026, 2, 1), new DateTime(2026, 3, 1)), node.OutPorts[0].Value);
    }

    [Fact]
    public void AddCarriesTheSearchWordsOfTheFiveRetiredNodes()
    {
        var definition = EngineRun.Registry().Definitions.Single(d => d.Name == "DateTime.Add");
        foreach (var word in new[] { "AddDays", "AddHours", "AddMinutes", "AddMonths", "AddYears", "offset", "shift", "schedule", "later", "earlier" })
        {
            Assert.Contains(word, definition.SearchTags);
        }

        Assert.Equal(new[] { "seconds", "minutes", "hours", "days", "weeks", "months", "years" }, definition.Inputs.Single(i => i.Name == "unit").Choices);
        Assert.Equal("days", definition.Inputs.Single(i => i.Name == "unit").DefaultValue);
    }

    // ------------------------------------------------------------ the retired nodes still load and run

    [Theory]
    [InlineData("DateTime.AddDays", "DateTime.Add")]
    [InlineData("DateTime.AddHours", "DateTime.Add")]
    [InlineData("DateTime.AddMinutes", "DateTime.Add")]
    [InlineData("DateTime.AddMonths", "DateTime.Add")]
    [InlineData("DateTime.AddYears", "DateTime.Add")]
    [InlineData("DateTime.AgeInDays", "DateTime.DaysBetween")]
    public void ARetiredNodeIsLeftOutOfTheLibraryButStillNamesWhatToUse(string name, string replacement)
    {
        var registry = EngineRun.Registry();
        var old = registry.Definitions.Single(d => d.Name == name);

        Assert.True(old.IsDeprecated);
        Assert.Equal(replacement, old.Replacement);
        Assert.False(registry.Definitions.Single(d => d.Name == replacement).IsDeprecated);
    }

    [Fact]
    public void AGraphFileThatUsesARetiredNodeStillLoadsAndGivesTheSameDates()
    {
        var registry = EngineRun.Registry();
        var graph = new GraphModel();
        var cases = new (string Name, object Amount, DateTime Expected)[]
        {
            ("DateTime.AddDays", 2.5, new DateTime(2026, 7, 13, 2, 30, 45)),
            ("DateTime.AddHours", 1.5, new DateTime(2026, 7, 10, 16, 0, 45)),
            ("DateTime.AddMinutes", 90d, new DateTime(2026, 7, 10, 16, 0, 45)),
            ("DateTime.AddMonths", 7d, new DateTime(2027, 2, 10, 14, 30, 45)),
            ("DateTime.AddYears", -1d, new DateTime(2025, 7, 10, 14, 30, 45)),
        };
        foreach (var (name, amount, _) in cases)
        {
            var node = EngineRun.Create(registry, name);
            node.InPorts[0].SetUserValue("2026-07-10 14:30:45");
            node.InPorts[1].SetUserValue(amount);
            graph.AddNode(node);
        }

        var serializer = new GraphSerializer(registry);
        var loaded = serializer.Deserialize(serializer.Serialize(graph));
        new GraphEngine().Run(loaded);

        Assert.Empty(serializer.LoadWarnings);
        foreach (var (name, _, expected) in cases)
        {
            var node = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == name);
            Assert.True(node.State == NodeState.Executed, name + ": " + node.State + " " + node.StateMessage);
            Assert.Equal(expected, node.OutPorts[0].Value);
        }
    }

    [Fact]
    public void RetiredAddDaysNowSaysPlainThingsToo()
    {
        var nan = EngineRun.Run("DateTime.AddDays", -1, Friday, double.NaN);
        Assert.Equal(NodeState.Error, nan.State);
        Assert.Contains("DateTime.AddDays: days must be a finite number", nan.StateMessage);

        var huge = EngineRun.Run("DateTime.AddDays", -1, Friday, 1e9);
        Assert.Equal(NodeState.Error, huge.State);
        Assert.Contains("outside the supported dates", huge.StateMessage);
        Assert.DoesNotContain("Value to add was out of range", huge.StateMessage);
    }

    [Fact]
    public void AgeInDaysForwardsToDaysBetweenAndTheTagsMovedThere()
    {
        var reference = new DateTime(2026, 7, 11, 12, 0, 0);
        Assert.Equal(DateTimeNodes.DaysBetween(Friday, reference), DateTimeExtraNodes.AgeInDays(Friday, reference));

        var registry = EngineRun.Registry();
        var between = registry.Definitions.Single(d => d.Name == "DateTime.DaysBetween");
        foreach (var word in new[] { "age", "old", "overdue", "since", "elapsed", "AgeInDays" })
        {
            Assert.Contains(word, between.SearchTags);
        }

        // The old node still runs through the engine: age of Friday at the reference.
        var old = EngineRun.Run("DateTime.AgeInDays", -1, Friday, reference);
        Assert.Equal(DateTimeNodes.DaysBetween(Friday, reference), (double)old.OutPorts[0].Value!, 9);
    }

    // ------------------------------------------------------------ DateTime.Difference (VAL-33)

    [Theory]
    [InlineData("seconds", 90061.0)]
    [InlineData("minutes", 1501.0166666666667)]
    [InlineData("hours", 25.016944444444444)]
    [InlineData("days", 1.0423726851851852)]
    [InlineData("weeks", 0.14891038359788358)]
    public void DifferenceInTheElapsedTimeUnits(string unit, double expected)
    {
        var start = new DateTime(2026, 7, 10, 0, 0, 0);
        var end = start.AddSeconds(90061);

        Assert.Equal(expected, DateTimeExtraNodes.Difference(start, end, unit), 9);
        Assert.Equal(-expected, DateTimeExtraNodes.Difference(end, start, unit), 9);
    }

    [Fact]
    public void DifferenceInMonthsAndYearsCountsCalendarMonths()
    {
        Assert.Equal(2.0, DateTimeExtraNodes.Difference(new DateTime(2026, 1, 10), new DateTime(2026, 3, 10), "months"));
        Assert.Equal(14.0, DateTimeExtraNodes.Difference(new DateTime(2026, 1, 10), new DateTime(2027, 3, 10), "months"));
        Assert.Equal(1.0, DateTimeExtraNodes.Difference(new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), "months"));
        Assert.Equal(-2.0, DateTimeExtraNodes.Difference(new DateTime(2026, 3, 10), new DateTime(2026, 1, 10), "months"));
        Assert.Equal(0.0, DateTimeExtraNodes.Difference(Friday, Friday, "months"));

        // 1 May to 16 May is 15 of the 31 days of May.
        Assert.Equal(1d + 15d / 31d, DateTimeExtraNodes.Difference(new DateTime(2026, 4, 1), new DateTime(2026, 5, 16), "months"), 9);
        Assert.Equal(2.0, DateTimeExtraNodes.Difference(new DateTime(2024, 2, 29), new DateTime(2026, 2, 28), "years"), 2);
        Assert.Equal(1.0, DateTimeExtraNodes.Difference(new DateTime(2026, 1, 10), new DateTime(2027, 1, 10), "years"));
    }

    [Fact]
    public void DifferenceAtTheEdgeOfTheCalendarDoesNotThrow()
    {
        Assert.True(DateTimeExtraNodes.Difference(DateTime.MinValue, DateTime.MaxValue, "months") > 119000);
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Difference(Friday, Friday, "decades"));
    }

    // ------------------------------------------------------------ DateTime.Range with a unit (VAL-34)

    [Fact]
    public void RangeByMonthsCountsFromTheStartSoAMonthEndDoesNotDrift()
    {
        var items = DateTimeExtraNodes.Range(new DateTime(2026, 1, 31), new DateTime(2026, 5, 31), 1, "months");

        Assert.Equal(
            new[] { new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), new DateTime(2026, 3, 31), new DateTime(2026, 4, 30), new DateTime(2026, 5, 31) },
            items);
    }

    [Fact]
    public void RangeByQuartersYearsAndWeeks()
    {
        Assert.Equal(
            new[] { new DateTime(2026, 1, 15), new DateTime(2026, 4, 15), new DateTime(2026, 7, 15) },
            DateTimeExtraNodes.Range(new DateTime(2026, 1, 15), new DateTime(2026, 8, 1), 3, "months"));
        Assert.Equal(
            new[] { new DateTime(2024, 2, 29), new DateTime(2025, 2, 28), new DateTime(2026, 2, 28) },
            DateTimeExtraNodes.Range(new DateTime(2024, 2, 29), new DateTime(2026, 6, 1), 1, "years"));
        Assert.Equal(
            new[] { new DateTime(2026, 7, 1), new DateTime(2026, 7, 15), new DateTime(2026, 7, 29) },
            DateTimeExtraNodes.Range(new DateTime(2026, 7, 1), new DateTime(2026, 7, 30), 2, "weeks"));
        Assert.Empty(DateTimeExtraNodes.Range(new DateTime(2026, 7, 1), new DateTime(2026, 6, 1), 1, "months"));
    }

    [Fact]
    public void RangeByMonthsNeedsAWholeStepAndHasTheSameCap()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.Range(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), 1.5, "months"));
        Assert.Contains("whole number", ex.Message);

        var many = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.Range(new DateTime(1, 1, 1), new DateTime(9999, 12, 31), 1, "months").Count);
        Assert.Contains("100000", many.Message);
    }

    [Fact]
    public void ARangeSavedBeforeTheUnitExistedStillLoadsWithItsStepAndRuns()
    {
        var registry = EngineRun.Registry();
        var current = registry.Definitions.Single(d => d.Name == "DateTime.Range");
        var graph = new GraphModel();
        var node = new ZeroTouchNodeModel(current);
        node.InPorts[0].SetUserValue("2026-07-01");
        node.InPorts[1].SetUserValue("2026-07-05");
        node.InPorts[2].SetUserValue(2d);
        graph.AddNode(node);
        var serializer = new GraphSerializer(registry);

        // The file as version 0.50 wrote it: the old id (no unit) and the input still called stepDays.
        var json = serializer.Serialize(graph)
            .Replace(current.Id, "CamelGraph.Nodes.DateTimeExtraNodes.Range@System.DateTime,System.DateTime,double")
            .Replace("\"step\"", "\"stepDays\"");
        Assert.Contains("stepDays", json);
        var loaded = serializer.Deserialize(json);
        new GraphEngine().Run(loaded);

        var range = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single();
        Assert.Equal("DateTime.Range", range.Name);
        Assert.Equal(2d, range.InPorts[2].UserValue);
        Assert.Empty(serializer.LoadWarnings);
        Assert.Equal(
            new[] { new DateTime(2026, 7, 1), new DateTime(2026, 7, 3), new DateTime(2026, 7, 5) },
            ((System.Collections.IEnumerable)range.OutPorts[0].Value!).Cast<DateTime>());
    }

    // ------------------------------------------------------------ working days (VAL-34)

    [Theory]
    [InlineData("2026-07-10", 1, "2026-07-13")]   // Friday + 1 = Monday
    [InlineData("2026-07-13", -1, "2026-07-10")]  // Monday - 1 = Friday
    [InlineData("2026-07-11", 1, "2026-07-13")]   // Saturday + 1 = Monday
    [InlineData("2026-07-11", -1, "2026-07-10")]  // Saturday - 1 = Friday
    [InlineData("2026-07-12", 5, "2026-07-17")]   // Sunday + 5 = the next Friday
    [InlineData("2026-07-13", 5, "2026-07-20")]   // five working days = one week
    [InlineData("2026-07-10", 10, "2026-07-24")]
    [InlineData("2026-07-10", 11, "2026-07-27")]
    [InlineData("2026-07-15", 0, "2026-07-15")]
    [InlineData("2026-07-11", 0, "2026-07-11")]   // 0 leaves a weekend date alone
    [InlineData("2026-07-15", -7, "2026-07-06")]
    public void AddWorkdaysSkipsTheWeekend(string start, int days, string expected)
    {
        var from = DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture).AddHours(9.5);
        var result = DateTimeExtraNodes.AddWorkdays(from, days);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture).AddHours(9.5), result);
    }

    [Fact]
    public void AddWorkdaysWithAFridayAndSaturdayWeekend()
    {
        // Thursday 2026-07-09 + 1 = Sunday 2026-07-12; Sunday - 1 = Thursday.
        Assert.Equal(new DateTime(2026, 7, 12), DateTimeExtraNodes.AddWorkdays(new DateTime(2026, 7, 9), 1, "Friday and Saturday"));
        Assert.Equal(new DateTime(2026, 7, 9), DateTimeExtraNodes.AddWorkdays(new DateTime(2026, 7, 12), -1, "Friday and Saturday"));
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddWorkdays(Friday, 1, "Tuesday"));
    }

    [Fact]
    public void AddWorkdaysIsFastForBigNumbersAndSaysWhenItRunsOutOfDates()
    {
        Assert.Equal(new DateTime(2026, 7, 13).AddDays(7 * 100000), DateTimeExtraNodes.AddWorkdays(new DateTime(2026, 7, 13), 500000));
        var ex = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.AddWorkdays(new DateTime(9999, 12, 29), 10));
        Assert.Contains("outside the supported dates", ex.Message);
    }

    [Fact]
    public void IsWeekendMasksAScheduleDownToWorkingDays()
    {
        Assert.False(DateTimeExtraNodes.IsWeekend(new DateTime(2026, 7, 10)));
        Assert.True(DateTimeExtraNodes.IsWeekend(new DateTime(2026, 7, 11)));
        Assert.True(DateTimeExtraNodes.IsWeekend(new DateTime(2026, 7, 12, 23, 59, 0)));
        Assert.True(DateTimeExtraNodes.IsWeekend(new DateTime(2026, 7, 10), "Friday and Saturday"));
        Assert.False(DateTimeExtraNodes.IsWeekend(new DateTime(2026, 7, 12), "Friday and Saturday"));

        var node = EngineRun.Run("DateTime.IsWeekend", -1, L(new DateTime(2026, 7, 10), new DateTime(2026, 7, 11)));
        Assert.Equal(L(false, true), node.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ DateTime.FromExcelSerial (COL-36, SYS-34)

    [Fact]
    public void ExcelSerialsBecomeDatesWithTheTimeOfDay()
    {
        Assert.Equal(new DateTime(2023, 3, 15), DateTimeExtraNodes.FromExcelSerial(45000));
        Assert.Equal(new DateTime(2023, 3, 15, 12, 0, 0), DateTimeExtraNodes.FromExcelSerial(45000.5));
        Assert.Equal(new DateTime(1899, 12, 30), DateTimeExtraNodes.FromExcelSerial(0));
        Assert.Equal(new DateTime(2023, 3, 15), DateTimeExtraNodes.FromExcelSerial(43538, "1904"));
    }

    [Fact]
    public void ExcelSerialsAgreeWithWhatTheExcelWriterStores()
    {
        // The writer's date cells use the same formula (DateTime.ToOADate); a round trip returns the date.
        var date = new DateTime(2026, 10, 4, 8, 15, 0);
        Assert.Equal(date, DateTimeExtraNodes.FromExcelSerial(date.ToOADate()));
    }

    [Fact]
    public void ABadExcelSerialSaysSoInPlainWords()
    {
        var nan = Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeExtraNodes.FromExcelSerial(double.NaN));
        Assert.Contains("finite number", nan.Message);
        var huge = Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.FromExcelSerial(1e12));
        Assert.Contains("is not a date", huge.Message);
        Assert.DoesNotContain("OLE", huge.Message);
        Assert.Throws<ArgumentException>(() => DateTimeExtraNodes.FromExcelSerial(45000, "1950"));
    }

    [Fact]
    public void AColumnOfExcelSerialsConvertsAtOnce()
    {
        var node = EngineRun.Run("DateTime.FromExcelSerial", -1, L(45000.0, 45001.0, null));

        Assert.Equal(L(new DateTime(2023, 3, 15), new DateTime(2023, 3, 16), null), node.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ DateTime.Parse (VAL-30) and Format (ENG-15)

    [Fact]
    public void DayFirstReadsEuropeanDates()
    {
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("10/07/2026", "", true));
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("10.07.2026", "", true));
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("10-07-2026", "", true));
        Assert.Equal(new DateTime(2026, 7, 13), DateTimeNodes.Parse("13/07/2026", "", true));
        Assert.Equal(new DateTime(2026, 7, 3), DateTimeNodes.Parse(" 3/7/2026 ", "", true));
        Assert.Equal(new DateTime(2026, 7, 10, 14, 30, 0), DateTimeNodes.Parse("10/07/2026 14:30", "", true));
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("10/07/26", "", true));
    }

    [Fact]
    public void DayFirstStillReadsIsoAndWrittenOutDates()
    {
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("2026-07-10", "", true));
        Assert.Equal(new DateTime(2026, 7, 10, 14, 30, 0), DateTimeNodes.Parse("2026-07-10 14:30", "", true));
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("10 Jul 2026", "", true));
    }

    [Fact]
    public void WithoutDayFirstTheOldReadingStaysAndAnAmbiguousDateIsSaidSo()
    {
        Assert.Equal(new DateTime(2026, 10, 7), DateTimeNodes.Parse("10/07/2026"));

        var node = EngineRun.Run("DateTime.Parse", -1, "10/07/2026");
        Assert.Equal(new DateTime(2026, 10, 7), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("7 October 2026", Warnings(node));
        Assert.Contains("10 July 2026", Warnings(node));
        Assert.Contains("dayFirst", Warnings(node));
    }

    [Theory]
    [InlineData("2026-07-10")]
    [InlineData("07/07/2026")]
    [InlineData("10 Jul 2026")]
    public void ADateThatCannotBeReadTwoWaysGivesNoWarning(string text)
    {
        var node = EngineRun.Run("DateTime.Parse", -1, text);
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void ADayFirstDateWithoutTheSwitchFailsWithAHint()
    {
        var ex = Assert.Throws<FormatException>(() => DateTimeNodes.Parse("13/07/2026"));
        Assert.Contains("dayFirst", ex.Message);

        var other = Assert.Throws<FormatException>(() => DateTimeNodes.Parse("not a date"));
        Assert.DoesNotContain("dayFirst", other.Message);
    }

    [Fact]
    public void DayFirstWarnsNothingAndAFormatOverridesIt()
    {
        var node = EngineRun.Run("DateTime.Parse", -1, "10/07/2026", "", true);
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new DateTime(2026, 7, 10), node.OutPorts[0].Value);

        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("07/10/2026", "MM/dd/yyyy", true));
    }

    [Fact]
    public void AParseSavedWithoutDayFirstStillLoadsAndRuns()
    {
        var registry = EngineRun.Registry();
        var current = registry.Definitions.Single(d => d.Name == "DateTime.Parse");
        var graph = new GraphModel();
        var node = new ZeroTouchNodeModel(current);
        node.InPorts[0].SetUserValue("2026-07-10");
        graph.AddNode(node);
        var serializer = new GraphSerializer(registry);

        var json = serializer.Serialize(graph).Replace(current.Id, "CamelGraph.Nodes.DateTimeNodes.Parse@string,string");
        Assert.DoesNotContain("@string,string,bool", json);
        var loaded = serializer.Deserialize(json);
        new GraphEngine().Run(loaded);

        var parse = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single();
        Assert.Equal("DateTime.Parse", parse.Name);
        Assert.Equal(new DateTime(2026, 7, 10), parse.OutPorts[0].Value);
        Assert.Empty(serializer.LoadWarnings);
    }

    [Fact]
    public void ABadFormatStringIsNamedInsteadOfShowingDotNetText()
    {
        var format = Assert.Throws<FormatException>(() => DateTimeNodes.Format(Friday, "x"));
        Assert.Contains("DateTime.Format: 'x' is not a date format", format.Message);
        Assert.DoesNotContain("Input string was not in a correct format", format.Message);

        Assert.Equal("10/07/2026", DateTimeNodes.Format(Friday, "dd/MM/yyyy"));

        foreach (var bad in new[] { "{", "x" })
        {
            try
            {
                DateTimeNodes.Parse("2026-07-10", bad);
                Assert.Fail("a bad format should not parse");
            }
            catch (FormatException ex)
            {
                Assert.Contains("DateTime.Parse", ex.Message);
                Assert.DoesNotContain("Input string was not in a correct format", ex.Message);
            }
        }
    }

    // ------------------------------------------------------------ groups and clocks (VAL-15, documentation)

    [Fact]
    public void TheClockAndDiceNodesSayWhatHappensInsideAGroup()
    {
        var registry = EngineRun.Registry();
        foreach (var name in new[] { "DateTime.Now", "DateTime.Today", "Math.Random" })
        {
            var description = registry.Definitions.Single(d => d.Name == name).Description;
            Assert.Contains("node group", description);
            Assert.Contains("Group Input", description);
        }
    }
}
