using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.UI.Services;

namespace Dyncamelo.App;

/// <summary>
/// Reflects a selected node's output model items into the live Navisworks
/// selection, so users can see in the viewport (and the selection tree) what a
/// node collected — the Dynamo-style "preview". Runs on the Navisworks main
/// thread (the editor's UI thread) and never lets a preview disrupt the host.
/// The service only ever touches the live selection while it owns it: until a
/// preview has actually been applied, Show/Clear calls leave the user's own
/// selection alone — graphs reading Selection.Current depend on that.
/// </summary>
public sealed class NavisworksPreviewService : IPreviewService
{
    private bool _previewApplied;

    /// <inheritdoc />
    public void ShowPreview(IReadOnlyList<object?> outputs)
    {
        try
        {
            var document = DyncameloHost.DocumentService.ActiveDocument;
            if (document == null || document.IsClear)
            {
                return;
            }

            var items = new List<ModelItem>();
            if (outputs != null)
            {
                foreach (var output in outputs)
                {
                    Collect(output, items);
                }
            }

            if (items.Count > 0)
            {
                document.CurrentSelection.CopyFrom(items);
                _previewApplied = true;
            }
            else if (_previewApplied)
            {
                // The previously previewed node now has nothing to show —
                // release the selection we took over. A node that never showed
                // anything must not clear what the user picked themselves.
                _previewApplied = false;
                document.CurrentSelection.Clear();
            }
        }
        catch (Exception)
        {
            // A preview must never throw into the host or a graph run.
        }
    }

    /// <inheritdoc />
    public void ClearPreview()
    {
        if (!_previewApplied)
        {
            return;
        }

        _previewApplied = false;
        try
        {
            var document = DyncameloHost.DocumentService.ActiveDocument;
            if (document != null && !document.IsClear)
            {
                document.CurrentSelection.Clear();
            }
        }
        catch (Exception)
        {
        }
    }

    private static void Collect(object? value, List<ModelItem> into)
    {
        if (value is ModelItem item)
        {
            into.Add(item);
        }
        else if (value is IEnumerable sequence && !(value is string))
        {
            foreach (var element in sequence)
            {
                Collect(element, into);
            }
        }
    }
}
