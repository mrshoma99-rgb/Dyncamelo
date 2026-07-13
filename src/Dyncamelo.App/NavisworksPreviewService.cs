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
/// </summary>
public sealed class NavisworksPreviewService : IPreviewService
{
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
            }
            else
            {
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
