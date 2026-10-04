using System;
using System.Globalization;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Pins how ClashTest.Edit reads its "leave unchanged" defaults (empty name, "unchanged" choices,
/// tolerance -1, unwired selections), which test-type spellings it accepts, and how a duplicated
/// test is named (ClashTest.Duplicate).
/// </summary>
public class ClashEditRulesTests
{
    // ------------------------------------------------------------ unchanged sentinel

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("unchanged", true)]
    [InlineData("UNCHANGED", true)]
    [InlineData("  Unchanged ", true)]
    [InlineData("Hard", false)]
    [InlineData("yes", false)]
    public void IsUnchanged_RecognisesTheKeepSentinels(string? input, bool expected)
    {
        Assert.Equal(expected, ClashEditRules.IsUnchanged(input));
    }

    // ------------------------------------------------------------ test type

    [Theory]
    [InlineData("Hard", "Hard")]
    [InlineData("hard", "Hard")]
    [InlineData("HARDCONSERVATIVE", "HardConservative")]
    [InlineData("hard conservative", "HardConservative")]
    [InlineData("Hard-Conservative", "HardConservative")]
    [InlineData("hard_conservative", "HardConservative")]
    [InlineData("Clearance", "Clearance")]
    [InlineData(" clearance ", "Clearance")]
    [InlineData("Duplicate", "Duplicate")]
    [InlineData("duplicates", "Duplicate")]
    [InlineData("Custom", "Custom")]
    public void NormalizeTestType_AcceptsAnyCaseSpacingAndPlural(string input, string expected)
    {
        Assert.Equal(expected, ClashEditRules.NormalizeTestType(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unchanged")]
    public void NormalizeTestType_Unchanged_MeansKeepTheCurrentType(string? input)
    {
        Assert.Null(ClashEditRules.NormalizeTestType(input));
    }

    [Fact]
    public void NormalizeTestType_UnknownType_ListsTheValidOnes()
    {
        var error = Assert.Throws<ArgumentException>(() => ClashEditRules.NormalizeTestType("soft"));
        Assert.Contains("'soft'", error.Message);
        Assert.Contains("Hard, HardConservative, Clearance, Duplicate, Custom", error.Message);
        Assert.Contains("unchanged", error.Message);
    }

    [Fact]
    public void TestTypes_MatchTheNavisworksEnumNames()
    {
        // ClashHelpers.ParseTestType feeds these straight to Enum.TryParse<ClashTestType>.
        Assert.Equal(new[] { "Hard", "HardConservative", "Clearance", "Duplicate", "Custom" }, ClashEditRules.TestTypes);
    }

    // ------------------------------------------------------------ tolerance

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.01, 0.01)]
    [InlineData(25.5, 25.5)]
    public void ResolveTolerance_NonNegative_IsSet(double input, double expected)
    {
        Assert.Equal(expected, ClashEditRules.ResolveTolerance(input));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-0.001)]
    [InlineData(-500.0)]
    public void ResolveTolerance_Negative_MeansUnchanged(double input)
    {
        Assert.Null(ClashEditRules.ResolveTolerance(input));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ResolveTolerance_NotFinite_IsAnError(double input)
    {
        var error = Assert.Throws<ArgumentException>(() => ClashEditRules.ResolveTolerance(input));
        Assert.Contains("-1", error.Message);
    }

    // ------------------------------------------------------------ flags

    [Theory]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData("False", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void ResolveFlag_ReadsYesAndNoSpellings(string input, bool expected)
    {
        Assert.Equal(expected, ClashEditRules.ResolveFlag(input, "mergeComposites"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unchanged")]
    public void ResolveFlag_Unchanged_IsNull(string? input)
    {
        Assert.Null(ClashEditRules.ResolveFlag(input, "mergeComposites"));
    }

    [Fact]
    public void ResolveFlag_Garbage_NamesTheInput()
    {
        var error = Assert.Throws<ArgumentException>(() => ClashEditRules.ResolveFlag("maybe", "mergeComposites"));
        Assert.Contains("'maybe'", error.Message);
        Assert.Contains("mergeComposites", error.Message);
        Assert.Contains("yes, no or unchanged", error.Message);
    }

    // ------------------------------------------------------------ plan

    [Fact]
    public void Plan_AllDefaults_ChangesNothing()
    {
        var plan = ClashEditRules.Plan("", "unchanged", -1, "unchanged", false, false);
        Assert.False(plan.HasChanges);
        Assert.False(plan.HasSettingsEdit);
        Assert.Null(plan.NewName);
        Assert.Null(plan.TestType);
        Assert.Null(plan.Tolerance);
        Assert.Null(plan.MergeComposites);
        Assert.False(plan.ReplaceSelectionA);
        Assert.False(plan.ReplaceSelectionB);
    }

    [Fact]
    public void Plan_WhitespaceName_IsTreatedAsNoRename()
    {
        Assert.False(ClashEditRules.Plan("   ", "", -1, "", false, false).HasChanges);
    }

    [Fact]
    public void Plan_OnlyARename_IsNotASettingsEdit()
    {
        var plan = ClashEditRules.Plan("New name", "unchanged", -1, "unchanged", false, false);
        Assert.True(plan.HasChanges);
        Assert.False(plan.HasSettingsEdit); // the rename uses its own API call
        Assert.Equal("New name", plan.NewName);
    }

    [Fact]
    public void Plan_CarriesEveryRequestedChange()
    {
        var plan = ClashEditRules.Plan("MEP vs Structure", "clearance", 0.05, "yes", true, true);
        Assert.True(plan.HasChanges);
        Assert.True(plan.HasSettingsEdit);
        Assert.Equal("MEP vs Structure", plan.NewName);
        Assert.Equal("Clearance", plan.TestType);
        Assert.Equal(0.05, plan.Tolerance);
        Assert.True(plan.MergeComposites);
        Assert.True(plan.ReplaceSelectionA);
        Assert.True(plan.ReplaceSelectionB);
    }

    [Fact]
    public void Plan_ToleranceOfZero_IsARealChange()
    {
        var plan = ClashEditRules.Plan("", "", 0.0, "", false, false);
        Assert.True(plan.HasSettingsEdit);
        Assert.Equal(0.0, plan.Tolerance);
    }

    [Fact]
    public void Plan_SelectionsAlone_AreSettingsEdits()
    {
        Assert.True(ClashEditRules.Plan("", "", -1, "", true, false).HasSettingsEdit);
        Assert.True(ClashEditRules.Plan("", "", -1, "", false, true).HasSettingsEdit);
    }

    [Fact]
    public void Plan_PropagatesInputErrors()
    {
        Assert.Throws<ArgumentException>(() => ClashEditRules.Plan("", "soft", -1, "", false, false));
        Assert.Throws<ArgumentException>(() => ClashEditRules.Plan("", "", double.NaN, "", false, false));
        Assert.Throws<ArgumentException>(() => ClashEditRules.Plan("", "", -1, "maybe", false, false));
    }

    // ------------------------------------------------------------ describe

    [Fact]
    public void Describe_ListsTheChanges()
    {
        var plan = ClashEditRules.Plan("X", "hard", 0.5, "no", true, false);
        Assert.Equal("name 'X', type Hard, tolerance 0.5, merge composites off, selection A", ClashEditRules.Describe(plan));
    }

    [Fact]
    public void Describe_NoChanges()
    {
        Assert.Equal("no changes", ClashEditRules.Describe(ClashEditRules.Plan("", "", -1, "", false, false)));
    }

    [Fact]
    public void Describe_UsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = comma;
            var text = ClashEditRules.Describe(ClashEditRules.Plan("", "", 0.25, "", false, false));
            Assert.Equal("tolerance 0.25", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Describe_NullPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ClashEditRules.Describe(null!));
    }

    // ------------------------------------------------------------ copy name

    [Theory]
    [InlineData("Pipes vs Beams", "", "Pipes vs Beams copy")]
    [InlineData("Pipes vs Beams", null, "Pipes vs Beams copy")]
    [InlineData("Pipes vs Beams", "   ", "Pipes vs Beams copy")]
    [InlineData("Pipes vs Beams", "Pipes vs Beams (0.05)", "Pipes vs Beams (0.05)")]
    [InlineData("", "", "Clash test copy")]
    [InlineData(null, null, "Clash test copy")]
    [InlineData("", "Mine", "Mine")]
    public void CopyName_DefaultsToNameCopy(string? source, string? requested, string expected)
    {
        Assert.Equal(expected, ClashEditRules.CopyName(source, requested));
    }
}
