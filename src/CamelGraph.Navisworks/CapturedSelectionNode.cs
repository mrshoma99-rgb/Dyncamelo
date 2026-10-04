using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Navisworks;

/// <summary>
/// An interactive input node that snapshots the current Navisworks selection and
/// keeps it. Unlike <c>Selection.Current</c> (which reads the live selection
/// again on every run), this stores the captured items in the node and outputs the
/// same set every run — until you press Capture again. The set is stored as
/// model-tree paths, each with the element's identity (its GUID, or its name when it
/// has none), so it survives save/reload of the graph and is checked against the
/// model each run: an element that is no longer where it was is looked up by its GUID,
/// and what cannot be found is reported on the node instead of being replaced by
/// another element. A graph file saved before identities were stored holds paths
/// only and still loads.
/// </summary>
public class CapturedSelectionNode : NodeModel, ICapturedSelectionNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "CapturedSelection";

    // Each entry is "path", "path|guid" or "path||name" (see PickedEntry).
    private List<string> _entries = new List<string>();

    // What the last Capture saw, shown in the node body (not saved).
    private int _lastSelected = -1;
    private int _lastNotLocated;
    private string _note = string.Empty;

    /// <summary>Creates the node with a single model-item output.</summary>
    public CapturedSelectionNode()
    {
        Name = "Captured Selection";
        Category = "Navisworks.Selection";
        Description = "Snapshots the current Navisworks selection and keeps it, so the graph runs on that fixed set " +
                      "even after you select something else (Selection.Current, in contrast, reads the live selection again on every run). " +
                      "Press Capture to (re)store the live selection, Clear to forget it. Each element is stored with its GUID and checked " +
                      "when the graph runs: if the model changed or is another project, the elements are looked up by GUID and any that " +
                      "cannot be found are reported on the node and left out. The capture belongs to the graph file, so use the node outside " +
                      "node groups (every instance of a group would share one set) and capture again for each project.";
        AddOutput("items", typeof(IEnumerable<ModelItem>), "The captured model items.");
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "snapshot", "freeze", "fixed", "pin", "keep", "capture", "remember" };

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <inheritdoc />
    public int CapturedCount => _entries.Count;

    /// <summary>Human-readable state shown in the node body.</summary>
    public string CapturedSummary
    {
        get
        {
            var text = _entries.Count == 0
                ? "No selection captured — select items, then Capture"
                : _entries.Count.ToString(CultureInfo.InvariantCulture) + " element(s) captured";
            if (_entries.Count > 0 && _lastNotLocated > 0 && _lastSelected >= 0)
            {
                text = _entries.Count.ToString(CultureInfo.InvariantCulture) + " of " + _lastSelected.ToString(CultureInfo.InvariantCulture) +
                       " selected element(s) captured (" + _lastNotLocated.ToString(CultureInfo.InvariantCulture) + " could not be located)";
            }

            return _note.Length == 0 ? text : text + ". " + _note;
        }
    }

    /// <inheritdoc />
    public void CaptureFromCurrentSelection()
    {
        var doc = NavisworksContext.ResolveDocument(null);

        // SelectedItems is a live view — snapshot it before walking the tree.
        var selected = new ModelItemCollection(doc.CurrentSelection.SelectedItems);
        if (selected.Count == 0)
        {
            // One stray click on Capture must not wipe a stored set.
            _note = _entries.Count == 0
                ? "Nothing is selected in Navisworks."
                : "Nothing is selected in Navisworks, so the captured set was kept.";
            Refresh();
            return;
        }

        _entries = ModelItemPaths.ComputeEntries(doc, selected, out var notLocated);
        _lastSelected = selected.Count;
        _lastNotLocated = notLocated;
        _note = _entries.Count == 0 ? "None of the selected elements could be located in the model tree." : string.Empty;
        Refresh();
        MarkDirty();
    }

    /// <inheritdoc />
    public void ClearCapturedSelection()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _entries = new List<string>();
        _lastSelected = -1;
        _lastNotLocated = 0;
        _note = string.Empty;
        Refresh();
        MarkDirty();
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        if (_entries.Count == 0)
        {
            AddMessage(MessageSeverity.Warning, "No selection has been captured yet. Select items in Navisworks, then press Capture on this node.");
            return new object?[] { new List<ModelItem>() };
        }

        var doc = NavisworksContext.ResolveDocument(null);
        var resolution = ModelItemPaths.ResolveEntries(doc, _entries);
        var message = PickedEntry.MissingMessage(resolution.Total, resolution.Missing);
        if (message != null)
        {
            AddMessage(MessageSeverity.Warning, message);
        }

        return new object?[] { resolution.Items };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        // "Paths" is what earlier versions read and wrote; "Entries" adds the identity of each element.
        data["Paths"] = new JArray(_entries.Select(e => PickedEntry.Parse(e).Path));
        data["Entries"] = new JArray(_entries);
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        var source = data["Entries"] as JArray ?? data["Paths"] as JArray;
        _entries = source != null
            ? source.Select(t => t.ToString()).ToList()
            : new List<string>();
        _lastSelected = -1;
        _lastNotLocated = 0;
        _note = string.Empty;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CapturedCount));
        OnPropertyChanged(nameof(CapturedSummary));
    }
}
