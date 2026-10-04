using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The plain .NET logic behind the saved-viewpoint nodes that cannot be run without Navisworks: naming a batch of viewpoints or BCF
/// topics after clash results from several tests (Navisworks numbers results per test, so "Clash1" exists in every test), the
/// <c>nameFormat</c> tokens, the natural sort of a viewpoint folder, folder paths such as "Reviews/Week 12", and the {name}
/// placeholder of a picture file path.
/// </summary>
public class ViewpointNamingHelpersTests
{
    // ------------------------------------------------------------------ NameDisambiguator (NVC-04)

    [Fact]
    public void ResultsOfOneTestKeepTheirOwnNames()
    {
        var result = NameDisambiguator.Make(new[] { "Clash1", "Clash2" }, new string?[] { "Walls", "Walls" });

        Assert.Equal(new[] { "Clash1", "Clash2" }, result.Names);
        Assert.False(result.GroupsPrefixed);
        Assert.Equal(0, result.Renumbered);
    }

    [Fact]
    public void ResultsOfTwoTestsGetTheTestInFrontSoNoneReplacesAnother()
    {
        // The usual Clash.Tests -> ClashTest.Results -> flatten: Clash1 and Clash2 exist in both tests.
        var result = NameDisambiguator.Make(
            new[] { "Clash1", "Clash2", "Clash1", "Clash2" },
            new string?[] { "Ducts v Walls", "Ducts v Walls", "Pipes v Beams", "Pipes v Beams" });

        Assert.True(result.GroupsPrefixed);
        Assert.Equal(
            new[] { "Ducts v Walls - Clash1", "Ducts v Walls - Clash2", "Pipes v Beams - Clash1", "Pipes v Beams - Clash2" },
            result.Names);
        Assert.Equal(4, result.Names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void WithoutAnyTestTheNamesAreOnlyNumberedWhereTheyRepeat()
    {
        var result = NameDisambiguator.Make(new[] { "A", "B", "A", "A" }, new string?[] { null, null, "", null });

        Assert.False(result.GroupsPrefixed);
        Assert.Equal(new[] { "A", "B", "A (2)", "A (3)" }, result.Names);
        Assert.Equal(2, result.Renumbered);
    }

    [Fact]
    public void ANumberedNameNeverTakesANameThatIsAlreadyThere()
    {
        var result = NameDisambiguator.Make(new[] { "A", "A (2)", "A" }, new string?[3]);

        Assert.Equal(new[] { "A", "A (2)", "A (3)" }, result.Names);
    }

    [Fact]
    public void TheSameClashOfTheSameTestTwiceIsNumberedEvenAfterThePrefix()
    {
        var result = NameDisambiguator.Make(new[] { "Clash1", "Clash1", "Clash1" }, new string?[] { "T1", "T2", "T1" });

        Assert.Equal(new[] { "T1 - Clash1", "T2 - Clash1", "T1 - Clash1 (2)" }, result.Names);
        Assert.Equal(1, result.Renumbered);
    }

    [Fact]
    public void NamesAndGroupsMustHaveTheSameLength()
    {
        Assert.Throws<ArgumentException>(() => NameDisambiguator.Make(new[] { "A" }, new string?[2]));
    }

    // ------------------------------------------------------------------ NameTemplate

    [Fact]
    public void TokensAreReplacedIgnoringCase()
    {
        var text = NameTemplate.Apply(
            "{TEST} / {name} #{index}",
            new Dictionary<string, string> { ["test"] = "Ducts", ["name"] = "Clash7", ["index"] = "3" },
            "nameFormat");

        Assert.Equal("Ducts / Clash7 #3", text);
    }

    [Fact]
    public void AnUnknownTokenIsAnErrorThatListsTheKnownOnes()
    {
        var error = Assert.Throws<ArgumentException>(() => NameTemplate.Apply(
            "{tset}",
            new Dictionary<string, string> { ["test"] = "a", ["name"] = "b" },
            "nameFormat"));

        Assert.Contains("{tset}", error.Message);
        Assert.Contains("{test}", error.Message);
        Assert.Contains("{name}", error.Message);
        Assert.Contains("nameFormat", error.Message);
    }

    [Fact]
    public void TextWithoutTokensAndUnclosedBracesIsKeptAsItIs()
    {
        var values = new Dictionary<string, string> { ["name"] = "x" };

        Assert.Equal("plain", NameTemplate.Apply("plain", values, "nameFormat"));
        Assert.Equal("a { b", NameTemplate.Apply("a { b", values, "nameFormat"));
        Assert.False(NameTemplate.HasTokens(""));
        Assert.False(NameTemplate.HasTokens(null));
        Assert.False(NameTemplate.HasTokens("a { b"));
        Assert.True(NameTemplate.HasTokens("{name}"));
    }

    // ------------------------------------------------------------------ NaturalNameComparer (NVC-35)

    [Fact]
    public void Clash2ComesBeforeClash10ForANaturalSortButNotForAnAlphabeticOne()
    {
        var names = new List<string> { "Clash10", "Clash2", "Clash1", "clash11" };

        var alphabetic = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var natural = names.OrderBy(n => n, NaturalNameComparer.Instance).ToList();

        Assert.Equal(new[] { "Clash1", "Clash10", "clash11", "Clash2" }, alphabetic);
        Assert.Equal(new[] { "Clash1", "Clash2", "Clash10", "clash11" }, natural);
    }

    [Fact]
    public void TheNaturalSortIgnoresCaseComparesNumbersByValueAndIsStable()
    {
        var comparer = NaturalNameComparer.Instance;

        Assert.Equal(0, comparer.Compare("Level 2", "level 2"));
        Assert.True(comparer.Compare("Level 02", "Level 3") < 0);
        Assert.True(comparer.Compare("Level 2", "Level 02") != 0, "different spellings of one number still order");
        Assert.True(comparer.Compare("A", "A1") < 0);
        Assert.True(comparer.Compare("1", "A") < 0);
        Assert.True(comparer.Compare(null, "A") < 0);
        Assert.True(comparer.Compare("A", null) > 0);
        Assert.True(comparer.Compare("Room 9999999999999999999999", "Room 10000000000000000000000") < 0);
    }

    // ------------------------------------------------------------------ SavedItemPath (NVC-19)

    [Fact]
    public void AFolderPathIsSplitIntoTrimmedNames()
    {
        Assert.Equal(new[] { "Reviews", "Week 12", "Level 1" }, SavedItemPath.Split(" Reviews / Week 12//Level 1 "));
        Assert.Empty(SavedItemPath.Split(null));
        Assert.Empty(SavedItemPath.Split("  "));
        Assert.Empty(SavedItemPath.Split("/"));
        Assert.Equal("Reviews/Week 12", SavedItemPath.Join(new[] { "Reviews", "Week 12" }));
    }

    [Fact]
    public void OnlyTextWithAtLeastTwoNamesIsAPath()
    {
        Assert.True(SavedItemPath.IsPath("A/B"));
        Assert.False(SavedItemPath.IsPath("A"));
        Assert.False(SavedItemPath.IsPath("/A"));
        Assert.False(SavedItemPath.IsPath(null));
        Assert.False(SavedItemPath.IsPath(""));
    }

    // ------------------------------------------------------------------ FileNameTemplate (NVC-43)

    [Fact]
    public void ThePlaceholderBecomesTheViewpointNameSafeForAFileName()
    {
        Assert.True(FileNameTemplate.HasNameToken("C:\\out\\{name}.png"));
        Assert.True(FileNameTemplate.HasNameToken("C:\\out\\{NAME}.png"));
        Assert.False(FileNameTemplate.HasNameToken("C:\\out\\view.png"));

        Assert.Equal("C:\\out\\Level 1 - North.png", FileNameTemplate.Apply("C:\\out\\{name}.png", "Level 1 - North"));
        Assert.Equal("C:\\out\\A_B_C.png", FileNameTemplate.Apply("C:\\out\\{Name}.png", "A/B:C"));
        Assert.Equal("C:\\out\\viewpoint.png", FileNameTemplate.Apply("C:\\out\\{name}.png", "  "));
        Assert.Equal("{name}{name}", FileNameTemplate.Apply("{name}{name}", "{name}")); // a name that looks like the placeholder is not replaced again
    }

    [Fact]
    public void APathWithoutThePlaceholderIsLeftAlone()
    {
        Assert.Equal("C:\\out\\view.png", FileNameTemplate.Apply("C:\\out\\view.png", "Level 1"));
    }

    [Fact]
    public void TheSanitizedNameHasNoCharacterWindowsRefuses()
    {
        var name = FileNameTemplate.Sanitize("a\\b/c:d*e?f\"g<h>i|j\tk. ");

        Assert.Equal("a_b_c_d_e_f_g_h_i_j_k", name);
        Assert.Equal("viewpoint", FileNameTemplate.Sanitize(null));
        Assert.Equal("viewpoint", FileNameTemplate.Sanitize("..."));
    }
}
