using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Pins how an item is found in a tree of folders (the Clash Detective tests tree: tests at the top level and inside folders).
/// The regroup nodes used to look at the top level only, so a test in a folder was "no longer in the document" (NVC-01).
/// </summary>
public class SavedTreeLocatorTests
{
    private sealed class Entry
    {
        public Entry(string name, bool isFolder = false, params Entry[] children)
        {
            Name = name;
            IsFolder = isFolder;
            Children = children.ToList();
        }

        public string Name { get; }

        public bool IsFolder { get; }

        public List<Entry> Children { get; }
    }

    private static bool Find(IEnumerable<Entry> top, string name, out Entry? parent, out int index) =>
        SavedTreeLocator.TryFind(top, e => e.Name == name, e => e.IsFolder ? e.Children : null, out parent, out index);

    [Fact]
    public void ATopLevelItemHasNoParentAndItsPosition()
    {
        var top = new[] { new Entry("A"), new Entry("B"), new Entry("Folder", true, new Entry("C")) };

        Assert.True(Find(top, "B", out var parent, out var index));
        Assert.Null(parent);
        Assert.Equal(1, index);
    }

    [Fact]
    public void AnItemInsideAFolderIsFoundWithItsFolderAndItsPositionInThatFolder()
    {
        var folder = new Entry("Folder", true, new Entry("C"), new Entry("D"));
        var top = new[] { new Entry("A"), folder };

        Assert.True(Find(top, "D", out var parent, out var index));
        Assert.Same(folder, parent);
        Assert.Equal(1, index);
    }

    [Fact]
    public void AnItemInsideAFolderInsideAFolderIsFoundWithTheInnerFolder()
    {
        var inner = new Entry("Inner", true, new Entry("E"));
        var outer = new Entry("Outer", true, new Entry("X"), inner);

        Assert.True(Find(new[] { outer }, "E", out var parent, out var index));
        Assert.Same(inner, parent);
        Assert.Equal(0, index);
    }

    [Fact]
    public void AnItemThatIsNotThereIsNotFound()
    {
        Assert.False(Find(new[] { new Entry("A") }, "Z", out var parent, out var index));
        Assert.Null(parent);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void TheChildrenOfAnItemThatIsNotAFolderAreNotSearched()
    {
        // A clash test is not a folder to search: its children are results, not tests.
        var test = new Entry("Test 1", false, new Entry("Clash1"));

        Assert.False(Find(new[] { test }, "Clash1", out _, out var index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void TheFirstMatchInDocumentOrderWins()
    {
        var folder = new Entry("Folder", true, new Entry("Same"));
        var top = new[] { folder, new Entry("Same") };

        Assert.True(Find(top, "Same", out var parent, out _));
        Assert.Same(folder, parent);
    }
}
