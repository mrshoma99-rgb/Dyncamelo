using System.Collections.Generic;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// ClashResult.SetStatus / Assign / SetDescription take one result, one result group, or a (nested) list of them in one
/// object-typed port. This pins how that input is unpacked (NVC-05, NVC-21).
/// </summary>
public class ClashInputsTests
{
    private interface IResult
    {
    }

    private sealed class Result : IResult
    {
        public Result(string name) => Name = name;

        public string Name { get; }
    }

    private sealed class Group : IResult
    {
    }

    [Fact]
    public void ASingleResultIsOneItemAndNotAList()
    {
        var one = new Result("Clash1");

        var unpacked = ClashInputs.Flatten<IResult>(one);

        Assert.Same(one, Assert.Single(unpacked.Items));
        Assert.False(unpacked.WasSequence);
        Assert.Null(unpacked.FirstWrong);
    }

    [Fact]
    public void AGroupCountsAsAResultLikeAnyOtherIResult()
    {
        var group = new Group();

        var unpacked = ClashInputs.Flatten<IResult>(group);

        Assert.Same(group, Assert.Single(unpacked.Items));
        Assert.Null(unpacked.FirstWrong);
    }

    [Fact]
    public void AListIsUnpackedInOrderAndSaysItWasAList()
    {
        var a = new Result("a");
        var b = new Result("b");

        var unpacked = ClashInputs.Flatten<IResult>(new List<object> { a, b });

        Assert.Equal(new IResult[] { a, b }, unpacked.Items);
        Assert.True(unpacked.WasSequence);
    }

    [Fact]
    public void AnEmptyListIsStillAListWithNothingInIt()
    {
        var unpacked = ClashInputs.Flatten<IResult>(new List<object>());

        Assert.Empty(unpacked.Items);
        Assert.True(unpacked.WasSequence);
    }

    [Fact]
    public void ListsPerTestAreFlattenedIntoOneList()
    {
        var a = new Result("a");
        var b = new Result("b");
        var c = new Result("c");

        var unpacked = ClashInputs.Flatten<IResult>(new List<object> { new List<object> { a, b }, new List<object> { c } });

        Assert.Equal(new IResult[] { a, b, c }, unpacked.Items);
    }

    [Fact]
    public void EmptyEntriesAreLeftOutAndCounted()
    {
        var a = new Result("a");

        var unpacked = ClashInputs.Flatten<IResult>(new List<object?> { null, a, null });

        Assert.Same(a, Assert.Single(unpacked.Items));
        Assert.Equal(2, unpacked.SkippedNulls);
    }

    [Fact]
    public void ATextIsNeverReadAsAListOfCharacters()
    {
        var unpacked = ClashInputs.Flatten<IResult>("Clash1");

        Assert.Empty(unpacked.Items);
        Assert.False(unpacked.WasSequence);
        Assert.Equal("Clash1", unpacked.FirstWrong);
    }

    [Fact]
    public void TheFirstEntryOfTheWrongKindIsReportedSoTheNodeCanNameIt()
    {
        var a = new Result("a");

        var unpacked = ClashInputs.Flatten<IResult>(new List<object> { a, 42, "x" });

        Assert.Same(a, Assert.Single(unpacked.Items));
        Assert.Equal(42, unpacked.FirstWrong);
    }
}
