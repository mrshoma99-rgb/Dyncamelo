using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// The opening boundary of a loop region. Wire a list into <c>items</c>; the
/// <c>item</c> output yields the current element and drives the nodes wired
/// downstream of it, which the engine re-executes once per element, in order.
/// The <c>loop</c> output is a handle that must be wired into a matching
/// <see cref="LoopCollectNode"/> to close the region.
/// <para>
/// Unlike the reified <c>Action.*</c> nodes, the loop body is built from the
/// ordinary nodes — any node placed between <c>Loop.Item.item</c> and
/// <c>Loop.Collect</c> runs per item, so the loop is universal.
/// </para>
/// </summary>
public sealed class LoopItemNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "LoopItem";

    private object? _currentItem;
    private int _index;
    private int _count;

    /// <summary>Creates the node with its ports.</summary>
    public LoopItemNode()
    {
        Name = "Loop.Item";
        Category = "Workflow";
        Description = "Yields the current item of a loop. Wire a list into 'items', wire 'item' into the nodes that should run per item, and wire 'loop' into a Loop.Collect to close the loop. The engine runs everything between here and Loop.Collect once per item, in order.";
        AddInput("items", typeof(IEnumerable<object>), "The list to iterate.");
        AddOutput("item", typeof(object), "The current item (per iteration).");
        AddOutput("index", typeof(int), "The current 0-based index.");
        AddOutput("count", typeof(int), "The total number of items.");
        AddOutput("loop", typeof(object), "Loop handle — wire into Loop.Collect to close the loop.");
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <summary>
    /// Binds the node to a specific iteration. Called by the engine's loop
    /// executor before each pass over the body; the outputs then report this item.
    /// </summary>
    /// <param name="item">The current element.</param>
    /// <param name="index">Its zero-based index.</param>
    /// <param name="count">The total element count.</param>
    public void BindIteration(object? item, int index, int count)
    {
        _currentItem = item;
        _index = index;
        _count = count;
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        // The engine binds the iteration and reads the items list directly; this
        // simply reports the currently-bound state (the handle is the node itself).
        return new object?[] { _currentItem, _index, _count, this };
    }
}
