using System;
using System.Collections.Generic;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// A picked model element is stored in the graph file as its position in the model tree. Opened on another project, or after a model was
/// removed or appended, that position leads to an unrelated element, which used to be edited without a word. Each pick now carries the
/// element's identity and is checked against it; a graph file written before that (paths only) still loads.
/// </summary>
public class PickedEntryTests
{
    private static readonly Guid Wall = new Guid("3f81e10a-25b0-49ff-9520-63f2a763150a");

    [Fact]
    public void AnOldEntryWithOnlyAPathStillParsesAndIsNotChecked()
    {
        var entry = PickedEntry.Parse("0:1/2");

        Assert.Equal("0:1/2", entry.Path);
        Assert.Equal(Guid.Empty, entry.Guid);
        Assert.False(entry.HasIdentity);
        Assert.Equal(PickCheck.Unchecked, PickedEntry.Check(entry, Guid.NewGuid(), "anything"));
        Assert.Equal("0:1/2", entry.Format());
    }

    [Fact]
    public void AnEntryWithAGuidIsWrittenWithoutTheNameAndRoundTrips()
    {
        var text = PickedEntry.Encode("1:4/5", Wall, "Basic Wall");

        Assert.Equal("1:4/5|3f81e10a25b049ff952063f2a763150a", text);
        var back = PickedEntry.Parse(text);
        Assert.Equal("1:4/5", back.Path);
        Assert.Equal(Wall, back.Guid);
        Assert.Null(back.Name);
        Assert.Equal(1, back.ModelIndex);
    }

    [Fact]
    public void AnElementWithoutAGuidKeepsItsNameEscapedSoItCannotBreakTheList()
    {
        var text = PickedEntry.Encode("0:3", Guid.Empty, "A;B|C 100%");

        Assert.DoesNotContain(";", text);
        Assert.Equal(1, System.Linq.Enumerable.Count(text, c => c == '|') - 1);
        var back = PickedEntry.Parse(text);
        Assert.Equal("A;B|C 100%", back.Name);
        Assert.Equal("0:3", back.Path);
        Assert.Equal(text, back.Format());
    }

    [Fact]
    public void AnElementWithNeitherGuidNorNameIsStoredAsAPath()
    {
        Assert.Equal("0:3", PickedEntry.Encode("0:3", Guid.Empty, string.Empty));
        Assert.Equal("0:3", PickedEntry.Encode("0:3", Guid.Empty, null));
    }

    [Fact]
    public void TheItemAtThePathMustHaveTheGuidThatWasPicked()
    {
        var entry = PickedEntry.Parse(PickedEntry.Encode("0:1", Wall, "Wall"));

        Assert.Equal(PickCheck.Verified, PickedEntry.Check(entry, Wall, "renamed"));
        Assert.Equal(PickCheck.Mismatch, PickedEntry.Check(entry, Guid.NewGuid(), "Wall"));
        Assert.Equal(PickCheck.Mismatch, PickedEntry.Check(entry, Guid.Empty, "Wall"));
    }

    [Fact]
    public void WithoutAGuidTheNameDecides()
    {
        var entry = PickedEntry.Parse(PickedEntry.Encode("0:1", Guid.Empty, "Layer 1"));

        Assert.Equal(PickCheck.Verified, PickedEntry.Check(entry, Guid.Empty, "Layer 1"));
        Assert.Equal(PickCheck.Mismatch, PickedEntry.Check(entry, Guid.Empty, "Layer 2"));
        Assert.Equal(PickCheck.Mismatch, PickedEntry.Check(entry, Guid.Empty, null));
    }

    [Fact]
    public void ABadGuidTextIsTreatedAsNoGuid()
    {
        var entry = PickedEntry.Parse("0:1|not-a-guid");

        Assert.Equal(Guid.Empty, entry.Guid);
        Assert.Equal(PickCheck.Unchecked, PickedEntry.Check(entry, Guid.NewGuid(), null));
    }

    [Fact]
    public void ModelIndexIsTheNumberBeforeTheColon()
    {
        Assert.Equal(2, PickedEntry.Parse("2:0/1").ModelIndex);
        Assert.Equal(-1, PickedEntry.Parse("junk").ModelIndex);
    }

    [Fact]
    public void ACandidateIsChosenWhenItIsTheOnlyOneOrTheOnlyOneInThePickedModel()
    {
        Assert.True(PickedEntry.TryChoose(new[] { "x" }, _ => false, out var only));
        Assert.Equal("x", only);

        Assert.True(PickedEntry.TryChoose(new[] { "a1", "b1" }, c => c.StartsWith("b"), out var inModel));
        Assert.Equal("b1", inModel);
    }

    [Fact]
    public void NoCandidateIsGuessedWhenSeveralFitOrNoneDoes()
    {
        Assert.False(PickedEntry.TryChoose(new[] { "a1", "a2" }, c => c.StartsWith("a"), out _));
        Assert.False(PickedEntry.TryChoose(new[] { "a1", "a2" }, _ => false, out _));
        Assert.False(PickedEntry.TryChoose(new string[0], _ => true, out _));
        Assert.False(PickedEntry.TryChoose<string>(null, _ => true, out _));
    }

    [Fact]
    public void ThePartlyMissingMessageSaysHowManyAndWhatToDo()
    {
        var message = PickedEntry.MissingMessage(10, 3)!;

        Assert.StartsWith("3 of 10 picked elements were not found", message);
        Assert.Contains("Pick them again.", message);
        Assert.DoesNotContain("`1", message);
    }

    [Fact]
    public void TheMissingMessageHasWordsForNoneAndForOne()
    {
        Assert.StartsWith("None of the 4 picked elements was found", PickedEntry.MissingMessage(4, 4)!);
        Assert.StartsWith("The picked element was not found", PickedEntry.MissingMessage(1, 1)!);
        Assert.Null(PickedEntry.MissingMessage(5, 0));
    }

    [Fact]
    public void ThePickCountTheEditorShowsIsStillTheNumberOfEntries()
    {
        // The editor counts a pinned value by splitting at ';' (ModelPickerHost.CountOf); an identity must not add or hide one.
        var value = CamelGraph.Core.Editing.ModelPickerHost.Prefix +
                    string.Join(
                        CamelGraph.Core.Editing.ModelPickerHost.Separator.ToString(),
                        new[] { PickedEntry.Encode("0:1", Wall, "a"), PickedEntry.Encode("0:2", Guid.Empty, "x;y"), "0:3" });

        Assert.Equal(3, CamelGraph.Core.Editing.ModelPickerHost.CountOf(value));
    }
}

/// <summary>The wiring of the identity check into the Navisworks pick storage and the Captured Selection node.</summary>
public class PickedSelectionWiringTests
{
    [Fact]
    public void ThePickerStoresIdentitiesAndResolvesThroughTheCheckWithAWarning()
    {
        var source = NavisworksSourceText.Source("NavisworksModelPicker.cs");

        Assert.Contains("ModelItemPaths.ComputeEntries(", source);
        Assert.Contains("ModelItemPaths.ResolveEntries(", source);
        Assert.Contains("PickedEntry.MissingMessage(", source);
        Assert.Contains("NodeWarnings.Add(", source);
        // The node face stays cheap: it checks the first pick only and never walks the whole model.
        Assert.Contains("ResolveEntryQuick(doc, paths[0])", source);
    }

    [Fact]
    public void AnEntryThatFailsTheCheckIsLookedUpByGuidInOnePassNotGuessed()
    {
        var resolve = NavisworksMethodText.Body("Internal/ModelItemPaths.cs", "ResolveEntries");

        Assert.Contains("PickedEntry.Check(", resolve);
        Assert.Contains("ModelDataReader.AllItems(doc)", resolve);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(resolve, "ModelDataReader\\.AllItems\\(").Count);
        Assert.Contains("PickedEntry.TryChoose(", resolve);
    }

    [Fact]
    public void CapturingBuildsTheIndexOfEachParentOnce()
    {
        var path = NavisworksMethodText.Body("Internal/ModelItemPaths.cs", "ComputePath");
        var entries = NavisworksMethodText.Body("Internal/ModelItemPaths.cs", "ComputeEntries");

        Assert.Contains("cache.IndexOf(parent, current)", path);
        Assert.Contains("new PathIndexCache()", entries);
    }

    [Fact]
    public void CapturedSelectionRefusesAnEmptyCaptureAndWarnsWhenItHoldsNothing()
    {
        var source = NavisworksSourceText.Source("CapturedSelectionNode.cs");

        Assert.Contains("if (selected.Count == 0)", source);
        Assert.Contains("so the captured set was kept", source);
        Assert.Contains("No selection has been captured yet", source);
        Assert.Contains("could not be located)", source);
        Assert.Contains("ModelItemPaths.ResolveEntries(doc, _entries)", source);
        Assert.Contains("PickedEntry.MissingMessage(", source);
    }

    [Fact]
    public void CapturedSelectionStillWritesThePathsOldVersionsRead()
    {
        var source = NavisworksSourceText.Source("CapturedSelectionNode.cs");

        Assert.Contains("data[\"Paths\"]", source);
        Assert.Contains("data[\"Entries\"]", source);
        Assert.Contains("data[\"Entries\"] as JArray ?? data[\"Paths\"] as JArray", source);
    }

    [Fact]
    public void TheCapturedSelectionTextNoLongerClaimsSelectionCurrentIsStale()
    {
        var source = NavisworksSourceText.Source("CapturedSelectionNode.cs");

        // The text used to say Selection.Current re-reads on every run while it did not; it now does (LiveState), and the node says
        // where it belongs.
        Assert.Contains("reads the live selection again on every run", source);
        Assert.Contains("use the node outside", source);
        Assert.Contains("node groups (every instance of a group would share one set)", source);
    }
}
