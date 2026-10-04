using System.Collections.Generic;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// An optional list input that narrows a node (the items of Model.Statistics): unwired means everything, wired and empty means
/// nothing. The Navisworks node itself cannot run here; this is the rule it calls.
/// </summary>
public class OptionalScopeTests
{
    [Fact]
    public void AnUnwiredInputMeansEverything()
    {
        var kind = OptionalScope.Classify<string>(null, out var given);

        Assert.Equal(ScopeKind.Everything, kind);
        Assert.Empty(given);
    }

    [Fact]
    public void AWiredEmptyListMeansNothingNotEverything()
    {
        // The reported defect: a search that found nothing counted the whole federated model.
        var kind = OptionalScope.Classify(new List<string>(), out var given);

        Assert.Equal(ScopeKind.Nothing, kind);
        Assert.Empty(given);
    }

    [Fact]
    public void AListOfOnlyNullsIsAlsoNothing()
    {
        var kind = OptionalScope.Classify(new List<string?> { null, null }, out var given);

        Assert.Equal(ScopeKind.Nothing, kind);
        Assert.Empty(given);
    }

    [Fact]
    public void ItemsAreGivenWithoutTheirNulls()
    {
        var kind = OptionalScope.Classify(new List<string?> { "a", null, "b" }, out var given);

        Assert.Equal(ScopeKind.Given, kind);
        Assert.Equal(new[] { "a", "b" }, given);
    }
}
