using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// The closing boundary of a loop region. Wire the <see cref="LoopItemNode"/>'s
/// <c>loop</c> handle into <c>loop</c> and the per-item value into <c>value</c>;
/// after the loop finishes, <c>results</c> holds one collected value per item.
/// The engine populates <c>results</c> directly, reading <c>value</c> once per
/// iteration — nodes wired downstream of <c>results</c> run once, after the loop.
/// </summary>
public sealed class LoopCollectNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "LoopCollect";

    /// <summary>Creates the node with its ports.</summary>
    public LoopCollectNode()
    {
        Name = "Loop.Collect";
        Category = "Workflow";
        Description = "Closes a loop and collects one value per iteration. Wire Loop.Item's 'loop' output into 'loop' and the per-item value into 'value'; 'results' is the list of collected values, available after the loop. Downstream of 'results' runs once.";
        AddInput("loop", typeof(object), "Loop handle from Loop.Item.");
        AddInput("value", typeof(object), "The value to collect this iteration.");
        AddOutput("results", typeof(IList<object>), "One collected value per item.");
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        // The engine's loop executor sets 'results' directly; this fallback (used
        // only if the node is somehow evaluated outside a loop) yields an empty list.
        return new object?[] { new List<object?>() };
    }
}
