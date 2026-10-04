using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The pure halves of the Transform nodes and Model.Remove: leaving out items that a listed container already moves, and the order in
/// which several models are taken out of a document.
/// </summary>
public class TransformListRulesTests
{
    private sealed class Tree
    {
        public Tree(string name, Tree? parent = null)
        {
            Name = name;
            Parent = parent;
        }

        public string Name { get; }

        public Tree? Parent { get; }

        public IEnumerable<Tree> Ancestors()
        {
            for (var current = Parent; current != null; current = current.Parent)
            {
                yield return current;
            }
        }
    }

    private sealed class ByName : IEqualityComparer<Tree>
    {
        public bool Equals(Tree? x, Tree? y) => x?.Name == y?.Name;

        public int GetHashCode(Tree item) => item.Name.GetHashCode();
    }

    private static List<Tree> Drop(IReadOnlyList<Tree> items, out int dropped) =>
        ListedAncestors.DropDescendantsAndRepeats(items, item => item.Ancestors(), new ByName(), out dropped);

    [Fact]
    public void AnItemBelowAListedContainerIsLeftOutBecauseTheContainerMovesItAlready()
    {
        var file = new Tree("file");
        var level = new Tree("level", file);
        var wall = new Tree("wall", level);
        var door = new Tree("door", wall);

        var kept = Drop(new[] { wall, door, level }, out var dropped);

        Assert.Equal(new[] { "level" }, kept.Select(k => k.Name));
        Assert.Equal(2, dropped);
    }

    [Fact]
    public void ItemsThatDoNotSitBelowEachOtherAllStayInTheirOrder()
    {
        var file = new Tree("file");
        var a = new Tree("a", file);
        var b = new Tree("b", file);
        var c = new Tree("c", file);

        var kept = Drop(new[] { c, a, b }, out var dropped);

        Assert.Equal(new[] { "c", "a", "b" }, kept.Select(k => k.Name));
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void TheSameItemListedTwiceIsKeptOnce()
    {
        var file = new Tree("file");
        var a = new Tree("a", file);

        var kept = Drop(new[] { a, new Tree("a", file) }, out var dropped);

        Assert.Single(kept);
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void ADistantAncestorCountsNotOnlyTheParent()
    {
        var file = new Tree("file");
        var level = new Tree("level", file);
        var wall = new Tree("wall", level);
        var door = new Tree("door", wall);

        var kept = Drop(new[] { file, door }, out var dropped);

        Assert.Equal(new[] { "file" }, kept.Select(k => k.Name));
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void AnEmptyListStaysEmpty()
    {
        var kept = Drop(new Tree[0], out var dropped);

        Assert.Empty(kept);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void SeveralModelsAreRemovedFromTheLastToTheFirstSoTheIndicesStayValid()
    {
        // Removing index 0 first would shift the model that was at 1 down to 0, and the second removal would take out the original 2.
        Assert.Equal(new[] { 1, 0 }, ModelRemoval.RemovalOrder(new[] { 0, 1 }));
        Assert.Equal(new[] { 4, 2, 0 }, ModelRemoval.RemovalOrder(new[] { 2, 0, 4 }));
    }

    [Fact]
    public void AModelNamedTwiceIsRemovedOnce()
    {
        Assert.Equal(new[] { 3, 1 }, ModelRemoval.RemovalOrder(new[] { 1, 3, 1, 3 }));
    }

    [Fact]
    public void NothingToRemoveGivesNoPositions()
    {
        Assert.Empty(ModelRemoval.RemovalOrder(new int[0]));
    }
}
