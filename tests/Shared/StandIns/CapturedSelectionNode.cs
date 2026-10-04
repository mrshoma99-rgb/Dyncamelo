using System;
using System.Collections.Generic;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;

namespace CamelGraph.TestSupport.StandIns;

/// <summary>
/// Stands in for <c>CamelGraph.Navisworks.CapturedSelectionNode</c> (the "Captured Selection" input), which cannot be loaded without
/// Navisworks. It has the real node's type tag, name, category, description and output, and — because the editor picks a node's body
/// by the node's class name — the same class name, so the editor draws the same body ("No selection captured", Capture, Clear).
/// </summary>
internal sealed class CapturedSelectionNode : NodeModel, ICapturedSelectionNode
{
    /// <summary>Serialised type tag, the same as the real node's.</summary>
    public const string TypeName = "CapturedSelection";

    public CapturedSelectionNode(CatalogueNode description, Type itemsType)
    {
        Name = description.Name;
        Category = description.Category;
        Description = description.Description;
        var output = description.Outputs.Count > 0 ? description.Outputs[0] : null;
        AddOutput(output?.Name ?? "items", itemsType, output?.Description ?? string.Empty);
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <inheritdoc />
    public int CapturedCount => 0;

    /// <summary>The text the node body shows (the real node's wording for an empty capture).</summary>
    public string CapturedSummary => "No selection captured — select items, then Capture";

    /// <inheritdoc />
    public void CaptureFromCurrentSelection()
    {
    }

    /// <inheritdoc />
    public void ClearCapturedSelection()
    {
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        return new object?[] { new List<object>() };
    }
}
