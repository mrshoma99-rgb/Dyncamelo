using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The pure half of Search.ByProperty. A list wired into <c>value</c> used to be turned into the text of its .NET type
/// ("System.Collections.Generic.List`1[System.Object]") and matched nothing; it is now "any of these", every entry an alternative of
/// ONE search.
/// </summary>
public class SearchPlanTests
{
    [Theory]
    [InlineData("equals", SearchMode.Equal)]
    [InlineData("", SearchMode.Equal)]
    [InlineData(null, SearchMode.Equal)]
    [InlineData("Contains", SearchMode.Contains)]
    [InlineData("wildcard", SearchMode.Wildcard)]
    [InlineData(">", SearchMode.GreaterThan)]
    [InlineData(">=", SearchMode.GreaterOrEqual)]
    [InlineData("<", SearchMode.LessThan)]
    [InlineData("<=", SearchMode.LessOrEqual)]
    [InlineData("GreaterThanOrEqual", SearchMode.GreaterOrEqual)]
    [InlineData("lessthan", SearchMode.LessThan)]
    [InlineData("exists", SearchMode.Exists)]
    public void TheModeTextIsRead(string? text, SearchMode expected)
    {
        Assert.Equal(expected, SearchPlan.ParseMode(text));
    }

    [Fact]
    public void AnUnknownModeListsTheValidOnes()
    {
        var error = Assert.Throws<ArgumentException>(() => SearchPlan.ParseMode("startswith"));

        Assert.Contains("'startswith'", error.Message);
        Assert.Contains("exists", error.Message);
    }

    [Fact]
    public void TheDropdownNamesAreAllModes()
    {
        Assert.All(SearchPlan.ModeNames, name => Assert.Equal(name, SearchPlan.ModeToText(SearchPlan.ParseMode(name))));
        Assert.Contains("exists", SearchPlan.ModeNames);
    }

    [Fact]
    public void AListOfValuesIsAnyOfThem()
    {
        var plan = SearchPlan.Create("equals", new List<object?> { "Wall", "Door", "Window" });

        Assert.True(plan.WasList);
        Assert.Equal(new object?[] { "Wall", "Door", "Window" }, plan.Values);
        Assert.False(plan.MatchesNothing);
    }

    [Fact]
    public void ASingleValueIsOneAlternative()
    {
        var plan = SearchPlan.Create("equals", "Wall");

        Assert.False(plan.WasList);
        Assert.Equal(new object?[] { "Wall" }, plan.Values);
    }

    [Fact]
    public void ANestedListIsFlattenedAndRepeatsAreDropped()
    {
        var plan = SearchPlan.Create("equals", new List<object?> { "A", new object?[] { "B", "A" }, 3, 3.0, 3 });

        Assert.Equal(new object?[] { "A", "B", 3, 3.0 }, plan.Values);
    }

    [Fact]
    public void ANumberAndTheSameNumberAsTextAreDifferentAlternatives()
    {
        // Stored as text or as a number they are different variants, so both must be tried.
        var plan = SearchPlan.Create("equals", new List<object?> { 3, "3" });

        Assert.Equal(2, plan.Values.Count);
    }

    [Fact]
    public void AnEmptyListMatchesNothingExceptForExists()
    {
        Assert.True(SearchPlan.Create("equals", new List<object?>()).MatchesNothing);
        Assert.True(SearchPlan.Create("contains", new object?[0]).MatchesNothing);
        Assert.True(SearchPlan.Create(">", new List<object?>()).MatchesNothing);
        Assert.False(SearchPlan.Create("exists", new List<object?>()).MatchesNothing);
    }

    [Fact]
    public void ExistsIgnoresTheValue()
    {
        var plan = SearchPlan.Create("exists", "ignored");

        Assert.Equal(SearchMode.Exists, plan.Mode);
        Assert.Empty(plan.Values);
    }

    [Fact]
    public void ContainsAndWildcardTakeEveryEntryAsText()
    {
        var contains = SearchPlan.Create("contains", new List<object?> { "DEMO", 12, "DEMO" });
        var wildcard = SearchPlan.Create("wildcard", new List<object?> { "*-L1-*", "*-L2-*" });

        Assert.Equal(new[] { "DEMO", "12" }, contains.Texts);
        Assert.Equal(new[] { "*-L1-*", "*-L2-*" }, wildcard.Texts);
    }

    [Fact]
    public void ANullEntryIsAnErrorForTextModesAndNamesItsPosition()
    {
        var error = Assert.Throws<ArgumentNullException>(() => SearchPlan.Create("contains", new List<object?> { "a", null, "c" }));

        Assert.Contains("value 2 of 3", error.Message);
        Assert.Throws<ArgumentNullException>(() => SearchPlan.Create("contains", null));
    }

    [Fact]
    public void ANullValueStillMeansAnEmptyPropertyForEquals()
    {
        var plan = SearchPlan.Create("equals", null);

        Assert.Single(plan.Values);
        Assert.Null(plan.Values[0]);
    }

    [Fact]
    public void AnEmptyWildcardPatternIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SearchPlan.Create("wildcard", string.Empty));
        Assert.Throws<ArgumentException>(() => SearchPlan.Create("wildcard", new List<object?> { "a*", string.Empty }));
    }

    [Fact]
    public void TheComparisonsTakeNumbersAndTextThatLooksLikeOne()
    {
        var plan = SearchPlan.Create(">=", new List<object?> { 100, "50.5", 100.0, 7L });

        Assert.Equal(new[] { 100.0, 50.5, 7.0 }, plan.Numbers);
    }

    [Fact]
    public void AWordWhereANumberIsNeededNamesTheWordAndItsPosition()
    {
        var single = Assert.Throws<ArgumentException>(() => SearchPlan.Create(">", "abc"));
        var inList = Assert.Throws<ArgumentException>(() => SearchPlan.Create("<", new List<object?> { 1, "abc" }));

        Assert.Equal("Mode '>' compares with a number; 'abc' is not one.", single.Message.Split('\n')[0].Replace(" (Parameter 'value')", string.Empty));
        Assert.Contains("value 2 of 2", inList.Message);
        Assert.Throws<ArgumentNullException>(() => SearchPlan.Create(">", null));
    }

    [Fact]
    public void ADictionaryOrTextIsNotAList()
    {
        Assert.False(SearchPlan.IsList("abc"));
        Assert.False(SearchPlan.IsList(new Dictionary<string, object?>()));
        Assert.False(SearchPlan.IsList(5));
        Assert.True(SearchPlan.IsList(new[] { 1, 2 }));
        Assert.True(SearchPlan.IsList(new List<string>()));
    }

    [Fact]
    public void TheMessagesNeverShowAGenericTypeName()
    {
        var error = Assert.Throws<ArgumentException>(() => SearchPlan.Create(">", new List<object?> { new List<object?> { "x" } }));

        Assert.DoesNotContain("`1", error.Message);
        Assert.DoesNotContain("System.Collections", error.Message);
    }
}

/// <summary>Distinct values grouped for SelectionSets.BulkByPropertyValues.</summary>
public class DistinctValueGroupsTests
{
    [Fact]
    public void TheSameTextStoredTwoWaysKeepsBothVariants()
    {
        // "3" is text in one file and an integer in another: one set, and its search has to find both.
        var groups = new DistinctValueGroups<string>();
        groups.Add("3", "text:3", "DisplayString:3");
        groups.Add("3", "int:3", "Int32:3");
        groups.Add("3", "text again", "DisplayString:3");

        Assert.Equal(1, groups.Count);
        Assert.Equal(new[] { "text:3", "int:3" }, groups.VariantsOf("3"));
    }

    [Fact]
    public void NumbersAreOrderedBySizeNotAsText()
    {
        var order = DistinctValueGroups<string>.Order(new[] { "10", "2", "1.5", "100", "-3" });

        Assert.Equal(new[] { "-3", "1.5", "2", "10", "100" }, order);
    }

    [Fact]
    public void TextIsOrderedIgnoringCase()
    {
        var order = DistinctValueGroups<string>.Order(new[] { "b", "A", "c", "B" });

        // Same letters in another case keep a fixed order (capital first), so the sets are made in the same order on every run.
        Assert.Equal(new[] { "A", "B", "b", "c" }, order);
    }

    [Fact]
    public void AMixOfNumbersAndWordsIsOrderedAsText()
    {
        var order = DistinctValueGroups<string>.Order(new[] { "10", "2", "Roof" });

        Assert.Equal(new[] { "10", "2", "Roof" }, order);
    }

    [Fact]
    public void TheEmptyAndNoneKeysComeLastAndDoNotStopNumericOrder()
    {
        var order = DistinctValueGroups<string>.Order(new[] { "(empty)", "10", "(none)", "2" });

        Assert.Equal(new[] { "2", "10", "(none)", "(empty)" }, order);
    }

    [Fact]
    public void SortedKeysUsesTheSameOrder()
    {
        var groups = new DistinctValueGroups<int>();
        groups.Add("10", 1, "a");
        groups.Add("9", 2, "b");

        Assert.Equal(new[] { "9", "10" }, groups.SortedKeys());
    }
}

/// <summary>Folder paths and sort order of the saved-tree folder nodes.</summary>
public class SavedTreePathsTests
{
    [Theory]
    [InlineData("Walls/Level 1", new[] { "Walls", "Level 1" })]
    [InlineData(" Walls / Level 1 /", new[] { "Walls", "Level 1" })]
    [InlineData("A\\B", new[] { "A", "B" })]
    [InlineData("Walls", new[] { "Walls" })]
    [InlineData("", new string[0])]
    [InlineData("  ", new string[0])]
    [InlineData("//", new string[0])]
    public void APathIsSplitIntoFolderNames(string path, string[] expected)
    {
        Assert.Equal(expected, SavedTreePaths.Split(path));
    }

    [Fact]
    public void NullIsTheTopLevel()
    {
        Assert.Empty(SavedTreePaths.Split(null));
        Assert.False(SavedTreePaths.IsPath(null));
        Assert.False(SavedTreePaths.IsPath("One"));
        Assert.True(SavedTreePaths.IsPath("One/Two"));
    }

    [Fact]
    public void TheSortIsByNameIgnoringCaseAndKeepsTheOrderOfEqualNames()
    {
        var order = SavedTreePaths.SortedOrder(new string?[] { "b", "A", "a", null, "C" });

        // null counts as empty text and sorts first; "A" and "a" keep their current order.
        Assert.Equal(new[] { 3, 1, 2, 0, 4 }, order);
    }

    [Fact]
    public void ApplyingTheMovesGivesTheSortedOrder()
    {
        var names = new List<string> { "d", "b", "e", "a", "c" };
        var order = SavedTreePaths.SortedOrder(names);

        var current = names.ToList();
        foreach (var move in SavedTreePaths.MovesToFront(order))
        {
            var entry = current[move.Key];
            current.RemoveAt(move.Key);
            current.Insert(move.Value, entry);
        }

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, current);
    }

    [Fact]
    public void OnlyTheEntriesOutOfPlaceAreMoved()
    {
        // b c d are already in order at the end; only a has to come to the front.
        var names = new List<string> { "b", "c", "d", "a" };
        var moves = SavedTreePaths.MovesToFront(SavedTreePaths.SortedOrder(names));

        Assert.Single(moves);
    }

    [Fact]
    public void AFolderThatIsAlreadyInOrderNeedsNoMove()
    {
        var order = SavedTreePaths.SortedOrder(new string?[] { "a", "b", "c" });

        Assert.Empty(SavedTreePaths.MovesToFront(order));
    }
}
