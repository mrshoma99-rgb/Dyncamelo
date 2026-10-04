using System.Linq;
using CamelGraph.Core.Graph;
using Xunit;
using static CamelGraph.Nodes.Tests.Val2Harness;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// One house default for the text searches (audit VAL-19): String.Contains, IndexOf and LastIndexOf ignore upper/lower case like
/// StartsWith and EndsWith; the Regex nodes stay case-sensitive and say so.
/// </summary>
public class StringCaseDefaultsTests
{
    [Theory]
    [InlineData("String.Contains")]
    [InlineData("String.IndexOf")]
    [InlineData("String.LastIndexOf")]
    [InlineData("String.StartsWith")]
    [InlineData("String.EndsWith")]
    public void TheIgnoreCaseInputOfTheTextSearchesDefaultsToTrue(string name)
    {
        var port = Create(Registry(), name).InPorts.Single(p => p.Name == "ignoreCase");

        Assert.True(port.HasDefault);
        Assert.Equal(true, port.DefaultValue);
    }

    [Fact]
    public void Contains_InAGraph_MatchesRegardlessOfCaseUnlessSwitchedOff()
    {
        var byDefault = Run(Create(Registry(), "String.Contains"), L("Wall-A", "DOOR"), "door");
        Assert.Equal(NodeState.Executed, byDefault.State);
        Assert.Equal(L(false, true), byDefault.OutPorts[0].Value);

        var strict = Create(Registry(), "String.Contains");
        strict.InPorts.Single(p => p.Name == "ignoreCase").SetUserValue(false);
        Run(strict, L("Wall-A", "DOOR", "door"), "door");
        Assert.Equal(L(false, false, true), strict.OutPorts[0].Value);
    }

    [Fact]
    public void IndexOf_InAGraph_FindsTheTextRegardlessOfCase()
    {
        var node = Run(Create(Registry(), "String.IndexOf"), "Level 02 - FLOOR", "floor");

        Assert.Equal(11, node.OutPorts[0].Value);
    }

    [Fact]
    public void LastIndexOf_InAGraph_FindsTheTextRegardlessOfCase()
    {
        var node = Run(Create(Registry(), "String.LastIndexOf"), "model.v1.RVT", ".rvt");

        Assert.Equal(8, node.OutPorts[0].Value);
    }

    [Theory]
    [InlineData("String.RegexIsMatch")]
    [InlineData("String.RegexMatch")]
    [InlineData("String.RegexMatches")]
    [InlineData("String.RegexReplace")]
    public void TheRegexNodesStayCaseSensitiveAndSayIt(string name)
    {
        var node = Create(Registry(), name);
        var port = node.InPorts.Single(p => p.Name == "ignoreCase");

        Assert.Equal(false, port.DefaultValue);
        Assert.Contains("Case-sensitive", node.Description);
        Assert.Contains("case-sensitive", port.Description);
    }

    [Fact]
    public void RegexSplit_DescriptionExplainsHowToIgnoreCase()
    {
        Assert.Contains("(?i)", Create(Registry(), "String.RegexSplit").Description);
    }

    [Fact]
    public void RegexIsMatch_InAGraph_IsCaseSensitiveByDefault()
    {
        var node = Run(Create(Registry(), "String.RegexIsMatch"), "ABC", "abc");

        Assert.Equal(false, node.OutPorts[0].Value);
    }
}
