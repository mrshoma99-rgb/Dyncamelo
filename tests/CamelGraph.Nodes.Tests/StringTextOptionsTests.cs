using System;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Serialization;
using Xunit;
using static CamelGraph.Nodes.Tests.Val2Harness;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Parity of the text nodes (audit VAL-14, ENG-15): Substring clamps like Left and Right, Replace can ignore case, Split can drop
/// empty parts and trim, Trim takes the characters to remove. The three nodes that gained a parameter keep loading from the id
/// older graphs saved.
/// </summary>
public class StringTextOptionsTests
{
    private const string OldReplaceId = "CamelGraph.Nodes.StringNodes.Replace@string,string,string";
    private const string OldSplitId = "CamelGraph.Nodes.StringNodes.Split@string,string";
    private const string OldTrimId = "CamelGraph.Nodes.StringNodes.Trim@string";

    // ── Substring ───────────────────────────────────────────────────────────

    [Fact]
    public void Substring_OverAColumn_ClampsEveryShortTextInsteadOfFailingIt()
    {
        var node = Run(Create(Registry(), "String.Substring"), L("abcdef", "ab"), 1, 4);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(L("bcde", "b"), node.OutPorts[0].Value);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void Substring_HugeLength_IsClampedWithoutAnIntegerOverflow()
    {
        // ENG-15: length = 2147483647 used to say "Index and length must refer to a location within the string. (Parameter 'length')".
        Assert.Equal("bc", StringNodes.Substring("abc", 1, int.MaxValue));
        Assert.Equal("c", StringNodes.Substring("abc", 2, int.MaxValue));
    }

    [Fact]
    public void Substring_StartBeyondTheText_StaysAnErrorThatNamesTheNode()
    {
        var node = Run(Create(Registry(), "String.Substring"), "ab", 3, 1);

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("String.Substring: start index 3", node.Messages.First().Text);
    }

    // ── Replace ─────────────────────────────────────────────────────────────

    [Fact]
    public void Replace_IgnoreCase_ReplacesEveryCaseVariant()
    {
        Assert.Equal("x-x-x", StringNodes.Replace("Door-DOOR-door", "door", "x", true));
        Assert.Equal("Door-DOOR-x", StringNodes.Replace("Door-DOOR-door", "door", "x", false));
        Assert.Equal("Door-DOOR-x", StringNodes.Replace("Door-DOOR-door", "door", "x"));
    }

    [Fact]
    public void Replace_IgnoreCase_TreatsTheReplacementAsPlainTextAndKeepsTheRest()
    {
        Assert.Equal("a$1b", StringNodes.Replace("aXb", "x", "$1", true));
        Assert.Equal("aXXb", StringNodes.Replace("aXXb", "yy", "z", true));
        Assert.Equal("ab", StringNodes.Replace("aXb", "x", null!, true));
        Assert.Equal("", StringNodes.Replace("", "x", "y", true));
        Assert.Equal("x-x", StringNodes.Replace("é-É", "É", "x", true));
    }

    [Fact]
    public void Replace_OldIdStillLoadsAndKeepsTheCaseSensitiveBehaviour()
    {
        var registry = Registry();
        Assert.True(registry.TryGetDefinition(OldReplaceId, out var definition));
        Assert.Equal("String.Replace", definition!.Name);

        var node = Run(registry.CreateZeroTouchNode(OldReplaceId)!, "Door-door", "door", "x");

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal("Door-x", node.OutPorts[0].Value);
    }

    // ── Split ───────────────────────────────────────────────────────────────

    [Fact]
    public void Split_RemoveEmpty_DropsTheEmptyParts()
    {
        Assert.Equal(new[] { "a", "b", "c" }, StringNodes.Split("a,b,,c", ",", removeEmpty: true));
        Assert.Equal(new[] { "a", "b", "", "c" }, StringNodes.Split("a,b,,c", ","));
        Assert.Empty(StringNodes.Split("", ",", removeEmpty: true));
        Assert.Empty(StringNodes.Split("", "", removeEmpty: true));
    }

    [Fact]
    public void Split_Trim_RemovesTheSpacesAroundEveryPart_BeforeEmptyPartsAreDropped()
    {
        Assert.Equal(new[] { "a", "b", "c" }, StringNodes.Split("a, b ,c", ",", trim: true));
        Assert.Equal(new[] { "a", "", "c" }, StringNodes.Split("a,  ,c", ",", trim: true));
        Assert.Equal(new[] { "a", "c" }, StringNodes.Split("a,  ,c", ",", removeEmpty: true, trim: true));
        Assert.Equal(new[] { "a", "  ", "c" }, StringNodes.Split("a,  ,c", ",", removeEmpty: true));
    }

    [Fact]
    public void Split_OldIdStillLoadsAndKeepsEveryPart()
    {
        var registry = Registry();
        Assert.True(registry.TryGetDefinition(OldSplitId, out var definition));
        Assert.Equal("String.Split", definition!.Name);

        var node = Run(registry.CreateZeroTouchNode(OldSplitId)!, "a, b,,c", ",");

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(L("a", " b", "", "c"), node.OutPorts[0].Value);
    }

    // ── Trim ────────────────────────────────────────────────────────────────

    [Fact]
    public void Trim_Chars_RemovesThoseCharactersFromBothEnds()
    {
        Assert.Equal("Wall", StringNodes.Trim("--Wall-_", "-_"));
        Assert.Equal("Wall", StringNodes.Trim("/Wall/", "/"));
        Assert.Equal(" Wall ", StringNodes.Trim("/ Wall /", "/"));
        Assert.Equal("a b", StringNodes.Trim("  a b  ", ""));
        Assert.Equal("a b", StringNodes.Trim("  a b  "));
    }

    [Fact]
    public void Trim_OldIdStillLoadsAndTrimsWhitespace()
    {
        var registry = Registry();
        Assert.True(registry.TryGetDefinition(OldTrimId, out var definition));
        Assert.Equal("String.Trim", definition!.Name);

        var node = Run(registry.CreateZeroTouchNode(OldTrimId)!, L("  a ", "b\t"));

        Assert.Equal(L("a", "b"), node.OutPorts[0].Value);
    }

    // ── Saved graphs ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("String.Replace", OldReplaceId)]
    [InlineData("String.Split", OldSplitId)]
    [InlineData("String.Trim", OldTrimId)]
    public void AGraphFileSavedWithTheOldIdOpensAndFindsTheNewNode(string name, string oldId)
    {
        var registry = Registry();
        var graph = new GraphModel();
        var node = Create(registry, name);
        graph.AddNode(node);
        var serializer = new GraphSerializer(registry);

        var json = serializer.Serialize(graph);
        Assert.Contains("\"DefinitionId\": \"" + node.Definition.Id + "\"", json);
        var old = json.Replace(node.Definition.Id, oldId);
        var loaded = serializer.Deserialize(old);

        var loadedNode = Assert.IsType<CamelGraph.Core.Loader.ZeroTouchNodeModel>(Assert.Single(loaded.Nodes));
        Assert.Equal(name, loadedNode.Name);
        Assert.Equal(node.Definition.Id, loadedNode.Definition.Id);
        Assert.Empty(serializer.LoadWarnings);
    }

    [Fact]
    public void TheNewOptionsAreOptionalInputsWithTheirOldMeaningAsDefault()
    {
        var registry = Registry();

        Assert.Equal(false, Create(registry, "String.Replace").InPorts.Single(p => p.Name == "ignoreCase").DefaultValue);
        Assert.Equal(false, Create(registry, "String.Split").InPorts.Single(p => p.Name == "removeEmpty").DefaultValue);
        Assert.Equal(false, Create(registry, "String.Split").InPorts.Single(p => p.Name == "trim").DefaultValue);
        Assert.Equal(string.Empty, Create(registry, "String.Trim").InPorts.Single(p => p.Name == "chars").DefaultValue);
    }
}
