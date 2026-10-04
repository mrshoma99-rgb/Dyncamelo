using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Nodes with a tab input, one of them for the tabs a person added.</summary>
public static class UserTabFixtures
{
    public static string Remove(object element, [NodeTabChoice("element", UserDefinedOnly = true)] string tab) => tab;

    public static string Read(object element, [NodeTabChoice("element")] string tab) => tab;
}

/// <summary>
/// The tab pickers of the nodes that write or remove a user-defined tab list only the tabs a person added, not the tabs of the model's
/// source files (which those nodes cannot change).
/// </summary>
public class UserDefinedTabChoiceTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(UserTabFixtures));

    private static ModelDataChoice Choice(string method) =>
        Definitions.Single(d => d.Method.Name == method).Inputs.Single(i => i.Name == "tab").DataChoice!;

    [Fact]
    public void TheFlagReachesThePortsSearch()
    {
        var choice = Choice("Remove");

        Assert.Equal(ModelDataKind.Tab, choice.Kind);
        Assert.True(choice.UserDefinedOnly);
    }

    [Fact]
    public void ByDefaultEveryTabIsListed()
    {
        Assert.False(Choice("Read").UserDefinedOnly);
    }
}
