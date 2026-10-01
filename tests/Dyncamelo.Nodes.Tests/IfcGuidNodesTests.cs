using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Xunit;
using GraphFunction = Dyncamelo.Core.Graph.NodeFunction;

namespace Dyncamelo.Nodes.Tests;

/// <summary>IFC.GuidEncode / IFC.GuidDecode and the codec the BCF nodes share with them.</summary>
public class IfcGuidNodesTests
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>
    /// GlobalId / GUID pairs worked out with the buildingSMART compression (the 128 bits as 22 base-64 digits,
    /// 0-9 A-Z a-z _ $, most significant first) by an independent reference implementation, plus the two extremes.
    /// </summary>
    public static IEnumerable<object[]> KnownPairs()
    {
        yield return new object[] { "0$WU4A9R19$vKWO$AdOnKA", "3f81e10a-25b0-49ff-9520-63f2a763150a" };
        yield return new object[] { "0YvctVUKr0kugbFTf53O9L", "22e66ddf-794d-40bb-8aa5-3dda450d8255" };
        yield return new object[] { "3cUkl32yn9qRSPvBJVyWYp", "e67aebc3-0bcc-49d1-b719-e4b4dff208b3" };
        yield return new object[] { "2O4thhPKX2yPXaQVe0n6Zi", "98137aeb-6548-42f1-9864-69fa00c468ec" };
        yield return new object[] { "1kTvXnbbzCWw8lcMd1dR4o", "6e779871-965f-4c83-a22f-9969c19db132" };
        yield return new object[] { "0000000000000000000000", "00000000-0000-0000-0000-000000000000" };
        yield return new object[] { "3$$$$$$$$$$$$$$$$$$$$$", "ffffffff-ffff-ffff-ffff-ffffffffffff" };
    }

    /// <summary>Independent encoder: the GUID as one 128-bit number written in 22 base-64 digits.</summary>
    private static string ReferenceEncode(Guid guid)
    {
        var value = BigInteger.Parse("0" + guid.ToString("N"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var chars = new char[22];
        for (int i = 21; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(value % 64)];
            value /= 64;
        }

        return new string(chars);
    }

    // ------------------------------------------------------------- encode

    [Theory]
    [MemberData(nameof(KnownPairs))]
    public void GuidEncode_GivesTheKnownGlobalId(string globalId, string guid)
    {
        Assert.Equal(globalId, IfcGuidNodes.GuidEncode(guid));
    }

    [Fact]
    public void GuidEncode_AcceptsCommonGuidLayouts()
    {
        const string expected = "0$WU4A9R19$vKWO$AdOnKA";
        Assert.Equal(expected, IfcGuidNodes.GuidEncode("3F81E10A-25B0-49FF-9520-63F2A763150A"));
        Assert.Equal(expected, IfcGuidNodes.GuidEncode("3f81e10a25b049ff952063f2a763150a"));
        Assert.Equal(expected, IfcGuidNodes.GuidEncode("{3f81e10a-25b0-49ff-9520-63f2a763150a}"));
        Assert.Equal(expected, IfcGuidNodes.GuidEncode("  3f81e10a-25b0-49ff-9520-63f2a763150a \t"));
    }

    [Fact]
    public void GuidEncode_ThrowsWhenNothingIsWired()
    {
        var error = Assert.Throws<ArgumentNullException>(() => IfcGuidNodes.GuidEncode(null!));
        Assert.Contains("IFC.GuidEncode requires a GUID", error.Message);
        Assert.Contains("'guid' input", error.Message);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    public void GuidEncode_SaysWhenTheTextIsEmpty(string text, string expected)
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidEncode(text));
        Assert.Contains("IFC.GuidEncode", error.Message);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void GuidEncode_SaysHowManyDigitsAreWrong()
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidEncode("3f81e10a-25b0-49ff"));
        Assert.Contains("16 hexadecimal digit(s)", error.Message);
        Assert.Contains("32", error.Message);
    }

    [Fact]
    public void GuidEncode_NamesTheCharacterThatIsNotHexadecimal()
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidEncode("3f81e10a-25b0-49ff-9520-63f2a763150g"));
        Assert.Contains("'g'", error.Message);
        Assert.Contains("hexadecimal", error.Message);
    }

    [Fact]
    public void GuidEncode_PointsAnIfcGlobalIdToGuidDecode()
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidEncode("0$WU4A9R19$vKWO$AdOnKA"));
        Assert.Contains("already an IFC GlobalId", error.Message);
        Assert.Contains("IFC.GuidDecode", error.Message);
    }

    // ------------------------------------------------------------- decode

    [Theory]
    [MemberData(nameof(KnownPairs))]
    public void GuidDecode_GivesTheKnownGuid(string globalId, string guid)
    {
        Assert.Equal(guid, IfcGuidNodes.GuidDecode(globalId));
    }

    [Fact]
    public void GuidDecode_ReturnsLowerCaseHyphenatedAndIgnoresSurroundingSpaces()
    {
        var guid = IfcGuidNodes.GuidDecode("  3cUkl32yn9qRSPvBJVyWYp ");
        Assert.Equal("e67aebc3-0bcc-49d1-b719-e4b4dff208b3", guid);
        Assert.Equal(guid.ToLowerInvariant(), guid);
        Assert.Equal(36, guid.Length);
    }

    [Fact]
    public void GuidDecode_ThrowsWhenNothingIsWired()
    {
        var error = Assert.Throws<ArgumentNullException>(() => IfcGuidNodes.GuidDecode(null!));
        Assert.Contains("IFC.GuidDecode requires an IFC GlobalId", error.Message);
        Assert.Contains("'globalId' input", error.Message);
    }

    [Fact]
    public void GuidDecode_SaysWhenTheTextIsEmpty()
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidDecode(" "));
        Assert.Contains("empty", error.Message);
    }

    [Theory]
    [InlineData("0$WU4A9R19$vKWO$AdOnK", 21)]
    [InlineData("0$WU4A9R19$vKWO$AdOnKAA", 23)]
    [InlineData("abc", 3)]
    public void GuidDecode_SaysHowLongTheTextIs(string text, int length)
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidDecode(text));
        Assert.Contains(length + " character(s)", error.Message);
        Assert.Contains("exactly 22", error.Message);
    }

    [Fact]
    public void GuidDecode_NamesTheCharacterOutsideTheAlphabet()
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidDecode("0$WU4A9R19$vKWO$AdOn!A"));
        Assert.Contains("'!'", error.Message);
        Assert.Contains("position 21", error.Message);
        Assert.Contains("alphabet", error.Message);
    }

    [Theory]
    [InlineData("4$WU4A9R19$vKWO$AdOnKA")]
    [InlineData("z$WU4A9R19$vKWO$AdOnKA")]
    public void GuidDecode_RejectsAFirstCharacterAboveThree(string text)
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidDecode(text));
        Assert.Contains("0, 1, 2 or 3", error.Message);
    }

    [Fact]
    public void GuidDecode_PointsAStandardGuidToGuidEncode()
    {
        var error = Assert.Throws<ArgumentException>(() => IfcGuidNodes.GuidDecode("3f81e10a-25b0-49ff-9520-63f2a763150a"));
        Assert.Contains("standard GUID", error.Message);
        Assert.Contains("IFC.GuidEncode", error.Message);
    }

    // ------------------------------------------------------------- round trips

    [Fact]
    public void ThousandRandomGuids_RoundTripAndMatchTheReferenceEncoder()
    {
        var random = new Random(20240601);
        var bytes = new byte[16];
        for (int i = 0; i < 1000; i++)
        {
            random.NextBytes(bytes);
            var guid = new Guid(bytes);

            var globalId = IfcGuidCodec.Encode(guid);
            Assert.Equal(22, globalId.Length);
            Assert.All(globalId, c => Assert.Contains(c, Alphabet));
            Assert.Contains(globalId[0], "0123");
            Assert.Equal(ReferenceEncode(guid), globalId);

            Assert.True(IfcGuidCodec.TryDecode(globalId, out var decoded));
            Assert.Equal(guid, decoded);

            var viaNodes = IfcGuidNodes.GuidDecode(IfcGuidNodes.GuidEncode(guid.ToString("D")));
            Assert.Equal(guid.ToString("D"), viaNodes);
        }
    }

    [Fact]
    public void EmptyGuid_RoundTripsToAllZeros()
    {
        Assert.Equal("0000000000000000000000", IfcGuidCodec.Encode(Guid.Empty));
        Assert.True(IfcGuidCodec.TryDecode("0000000000000000000000", out var guid));
        Assert.Equal(Guid.Empty, guid);
    }

    // ------------------------------------------------------------- the shared codec (BCF behaviour)

    [Fact]
    public void TryDecode_StillAcceptsAPlainGuid_AsTheBcfNodesRelyOn()
    {
        Assert.True(IfcGuidCodec.TryDecode("3f81e10a-25b0-49ff-9520-63f2a763150a", out var guid));
        Assert.Equal(new Guid("3f81e10a-25b0-49ff-9520-63f2a763150a"), guid);
        Assert.True(IfcGuidCodec.TryDecode(" 0$WU4A9R19$vKWO$AdOnKA ", out guid));
        Assert.Equal(new Guid("3f81e10a-25b0-49ff-9520-63f2a763150a"), guid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a guid")]
    [InlineData("0$WU4A9R19$vKWO$AdOnK")]
    [InlineData("0$WU4A9R19$vKWO$AdOn!A")]
    [InlineData("4$WU4A9R19$vKWO$AdOnKA")]
    public void TryDecode_RejectsEverythingElse(string? text)
    {
        Assert.False(IfcGuidCodec.TryDecode(text, out var guid));
        Assert.Equal(Guid.Empty, guid);
    }

    [Theory]
    [InlineData("0$WU4A9R19$vKWO$AdOnKA", true)]
    [InlineData("  0$WU4A9R19$vKWO$AdOnKA  ", true)]
    [InlineData("3f81e10a-25b0-49ff-9520-63f2a763150a", false)]
    [InlineData("4$WU4A9R19$vKWO$AdOnKA", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGlobalId_OnlyAcceptsTwentyTwoCharacterIfcIds(string? text, bool expected)
    {
        Assert.Equal(expected, IfcGuidCodec.IsGlobalId(text));
    }

    // ------------------------------------------------------------- registry and engine

    [Fact]
    public void Registered_InTheIfcCategory_AndHelpersAreNotImportedAsNodes()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        foreach (var name in new[] { "IFC.GuidEncode", "IFC.GuidDecode" })
        {
            var definition = registry.Definitions.Single(d => d.Name == name);
            Assert.Equal("IFC", definition.Category);
            Assert.Equal(GraphFunction.Create, definition.Function);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        }

        foreach (var helper in new[] { "IfcGuidCodec", "PropertySpec", "PropertyCatalog", "GroupStatistics", "SnapshotBuilder", "GuidLookup", "IfcGuidLookup", "GuidRequest" })
        {
            Assert.DoesNotContain(registry.Definitions, d => d.Id.Contains(helper + "."));
        }
    }

    [Fact]
    public void Engine_EncodesAndDecodesAGuidAcrossTwoNodes()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var guid = new StringInputNode { Value = "3f81e10a-25b0-49ff-9520-63f2a763150a" };
        var encode = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "IFC.GuidEncode"));
        var decode = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "IFC.GuidDecode"));
        foreach (var node in new NodeModel[] { guid, encode, decode })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(guid.OutPorts[0], encode.InPorts[0]).Success);
        Assert.True(graph.Connect(encode.OutPorts[0], decode.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal("0$WU4A9R19$vKWO$AdOnKA", encode.OutPorts[0].Value);
        Assert.Equal("3f81e10a-25b0-49ff-9520-63f2a763150a", decode.OutPorts[0].Value);
    }

    [Fact]
    public void Engine_ReplicatesGuidEncodeOverAList()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var list = new ListCreateNode();
        var first = new StringInputNode { Value = "3f81e10a-25b0-49ff-9520-63f2a763150a" };
        var second = new StringInputNode { Value = "00000000-0000-0000-0000-000000000000" };
        var encode = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "IFC.GuidEncode"));
        foreach (var node in new NodeModel[] { list, first, second, encode })
        {
            graph.AddNode(node);
        }

        list.AddItemPort();
        Assert.True(graph.Connect(first.OutPorts[0], list.InPorts[0]).Success);
        Assert.True(graph.Connect(second.OutPorts[0], list.InPorts[1]).Success);
        Assert.True(graph.Connect(list.OutPorts[0], encode.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        var ids = Assert.IsAssignableFrom<IEnumerable<object?>>(encode.OutPorts[0].Value).ToArray();
        Assert.Equal(new object?[] { "0$WU4A9R19$vKWO$AdOnKA", "0000000000000000000000" }, ids);
    }

    [Fact]
    public void Engine_ShowsTheProblemOnTheRedNode()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var text = new StringInputNode { Value = "0$WU4A9R19$vKWO$AdOnK" };
        var decode = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "IFC.GuidDecode"));
        graph.AddNode(text);
        graph.AddNode(decode);
        Assert.True(graph.Connect(text.OutPorts[0], decode.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, decode.State);
        Assert.Contains("IFC.GuidDecode", decode.StateMessage);
        Assert.Contains("21 character(s)", decode.StateMessage);
    }
}
