using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// List@Level (Dynamo's @L): an input port can consume the incoming nested
/// list at a chosen level, counted from the INNERMOST (L1 = items, L2 = lists
/// of items, …). The engine replicates over the remaining outer levels; with
/// "keep list structure" off (the Dynamo default) those replicated levels
/// flatten into one list, with it on the nesting is preserved. Levels off =
/// exactly the pre-levels engine behavior.
/// </summary>
public class ListLevelsTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    private static List<object?> L(params object?[] items) => new List<object?>(items);

    private object? Run(string method, object? input, bool useLevels, int level, bool keepStructure)
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, input);
        var node = ZT.Node(method);
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);
        node.InPorts[0].SetLevels(useLevels, level, keepStructure);

        var result = _engine.Run(graph);
        Assert.True(result.Success);
        return node.OutPorts[0].Value;
    }

    // ------------------------------------------------- object-typed list ports

    [Fact]
    public void ObjectListPort_ConsumesWhole_ByDefault_ButLevelsMakeItMap()
    {
        var groups = L(L(1.0, 2.0), L(3.0, 4.0, 5.0));

        // CountItems(IList<object>) never replicates by default: one call, 2 groups.
        Assert.Equal(2, Run("CountItems", groups, useLevels: false, level: -1, keepStructure: false));

        // @L2 = "feed me the lists of items" — one call per group.
        Assert.Equal(new List<object?> { 2, 3 }, Run("CountItems", groups, true, 2, true));
    }

    // ----------------------------------------------------- flatten vs keep

    [Fact]
    public void L2_OnRank3Input_KeepOff_FlattensTheOuterLevels()
    {
        var nested = L(L(L(1.0, 2.0), L(3.0, 4.0)), L(L(5.0, 6.0)));
        Assert.Equal(new List<object?> { 3.0, 7.0, 11.0 }, Run("Sum", nested, true, 2, keepStructure: false));
    }

    [Fact]
    public void L2_OnRank3Input_KeepOn_PreservesTheNesting()
    {
        var nested = L(L(L(1.0, 2.0), L(3.0, 4.0)), L(L(5.0, 6.0)));
        var result = Assert.IsType<List<object?>>(Run("Sum", nested, true, 2, keepStructure: true));
        Assert.Equal(new List<object?> { 3.0, 7.0 }, result[0]);
        Assert.Equal(new List<object?> { 11.0 }, result[1]);
    }

    [Fact]
    public void LevelsOff_Rank3Input_KeepsTodaysStructurePreservingBehavior()
    {
        var nested = L(L(L(1.0, 2.0), L(3.0, 4.0)), L(L(5.0, 6.0)));
        var result = Assert.IsType<List<object?>>(Run("Sum", nested, false, -1, false));
        Assert.Equal(new List<object?> { 3.0, 7.0 }, result[0]);
        Assert.Equal(new List<object?> { 11.0 }, result[1]);
    }

    // ------------------------------------------------------------- L1 semantics

    [Fact]
    public void L1_OnListPort_FeedsEachItemPromotedToASingletonList()
    {
        var groups = L(L(1.0, 2.0), L(3.0, 4.0));

        // Sum normally eats each rank-1 group; @L1 targets the ITEMS, which
        // rank-promotion wraps into [x] — so each item sums to itself.
        Assert.Equal(new List<object?> { 1.0, 2.0, 3.0, 4.0 }, Run("Sum", groups, true, 1, keepStructure: false));

        var kept = Assert.IsType<List<object?>>(Run("Sum", groups, true, 1, keepStructure: true));
        Assert.Equal(new List<object?> { 1.0, 2.0 }, kept[0]);
        Assert.Equal(new List<object?> { 3.0, 4.0 }, kept[1]);
    }

    [Fact]
    public void ScalarPort_L1_MatchesDefaultOnFlatLists_AndFlattensDeepOnes()
    {
        Assert.Equal(new List<object?> { 1.0, 2.0, 3.0 }, Run("Sqrt", L(1.0, 4.0, 9.0), true, 1, false));

        var deep = L(L(1.0, 4.0), L(9.0));
        Assert.Equal(new List<object?> { 1.0, 2.0, 3.0 }, Run("Sqrt", deep, true, 1, keepStructure: false));

        var kept = Assert.IsType<List<object?>>(Run("Sqrt", deep, true, 1, keepStructure: true));
        Assert.Equal(new List<object?> { 1.0, 2.0 }, kept[0]);
        Assert.Equal(new List<object?> { 3.0 }, kept[1]);
    }

    // ----------------------------------------------------------- ragged lists

    [Fact]
    public void RaggedList_Default_PreservesShape_WithLevelsFlattens()
    {
        var ragged = L(1.0, L(4.0, 9.0));

        var plain = Assert.IsType<List<object?>>(Run("Sqrt", ragged, false, -1, false));
        Assert.Equal(1.0, plain[0]);
        Assert.Equal(new List<object?> { 2.0, 3.0 }, plain[1]);

        Assert.Equal(new List<object?> { 1.0, 2.0, 3.0 }, Run("Sqrt", ragged, true, 1, keepStructure: false));
    }

    // ------------------------------------------------- multiple leveled inputs

    [Fact]
    public void TwoLeveledInputs_PairUnderLacing()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, L(L(1.0, 2.0), L(10.0)));
        var b = ZT.Value(graph, L(L(100.0, 200.0), L(300.0)));
        var add = ZT.Node("Add");
        graph.AddNode(add);
        ZT.Wire(graph, a, 0, add, 0);
        ZT.Wire(graph, b, 0, add, 1);
        add.InPorts[0].SetLevels(true, 1, keepListStructure: true);
        add.InPorts[1].SetLevels(true, 1, keepListStructure: true);

        Assert.True(_engine.Run(graph).Success);
        var result = Assert.IsType<List<object?>>(add.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 101.0, 202.0 }, result[0]);
        Assert.Equal(new List<object?> { 310.0 }, result[1]);
    }

    // ------------------------------------------------------------ change flow

    [Fact]
    public void SetLevels_DirtiesTheNode_SoTheNextRunApplies()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, L(L(1.0, 2.0), L(3.0, 4.0, 5.0)));
        var count = ZT.Node("CountItems");
        graph.AddNode(count);
        ZT.Wire(graph, source, 0, count, 0);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(2, count.OutPorts[0].Value);
        Assert.False(count.IsDirty);

        count.InPorts[0].SetLevels(true, 2, false);
        Assert.True(count.IsDirty);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new List<object?> { 2, 3 }, count.OutPorts[0].Value);

        // No-op set does not dirty.
        count.InPorts[0].SetLevels(true, 2, false);
        Assert.False(count.IsDirty);
    }
}
