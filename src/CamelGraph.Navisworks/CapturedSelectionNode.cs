using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Navisworks.Internal;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Navisworks;

/// <summary>
/// An interactive input node that snapshots the current Navisworks selection and
/// keeps it. Unlike <c>Selection.Current</c> (which re-reads the live selection
/// on every run), this stores the captured items in the node and outputs the
/// same set every run — until you press Capture again. The set is stored as
/// model-tree paths so it survives save/reload of the graph.
/// </summary>
public class CapturedSelectionNode : NodeModel, ICapturedSelectionNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "CapturedSelection";

    private List<string> _paths = new List<string>();

    /// <summary>Creates the node with a single model-item output.</summary>
    public CapturedSelectionNode()
    {
        Name = "Captured Selection";
        Category = "Navisworks.Selection";
        Description = "Snapshots the current Navisworks selection and keeps it, so the graph runs on that fixed set " +
                      "even after you select something else. Press Capture to (re)store the live selection, Clear to forget it.";
        AddOutput("items", typeof(IEnumerable<ModelItem>), "The captured model items.");
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "snapshot", "freeze", "fixed", "pin", "keep", "capture", "remember" };

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <inheritdoc />
    public int CapturedCount => _paths.Count;

    /// <summary>Human-readable state shown in the node body.</summary>
    public string CapturedSummary => _paths.Count == 0
        ? "No selection captured — select items, then Capture"
        : _paths.Count.ToString(CultureInfo.InvariantCulture) + " element(s) captured";

    /// <inheritdoc />
    public void CaptureFromCurrentSelection()
    {
        var doc = NavisworksContext.ResolveDocument(null);

        // SelectedItems is a live view — snapshot it before walking the tree.
        var selected = new ModelItemCollection(doc.CurrentSelection.SelectedItems);
        var paths = new List<string>(selected.Count);
        foreach (var item in selected)
        {
            var path = ModelItemPaths.ComputePath(doc, item);
            if (path != null)
            {
                paths.Add(path);
            }
        }

        _paths = paths;
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CapturedSummary));
        MarkDirty();
    }

    /// <inheritdoc />
    public void ClearCapturedSelection()
    {
        if (_paths.Count == 0)
        {
            return;
        }

        _paths = new List<string>();
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CapturedSummary));
        MarkDirty();
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var doc = NavisworksContext.ResolveDocument(null);
        var items = new List<ModelItem>(_paths.Count);
        foreach (var path in _paths)
        {
            var item = ModelItemPaths.ResolvePath(doc, path);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return new object?[] { items };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["Paths"] = new JArray(_paths);
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        _paths = data["Paths"] is JArray array
            ? array.Select(t => t.ToString()).ToList()
            : new List<string>();
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CapturedSummary));
    }
}
