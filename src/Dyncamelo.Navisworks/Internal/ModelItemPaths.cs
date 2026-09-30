using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// Stable references to model items as "modelIndex:childIdx/childIdx/…" paths through the model tree, so a set of
/// items can be stored in a graph file (Captured Selection, the model-element inputs) and found again later.
/// </summary>
internal static class ModelItemPaths
{
    /// <summary>A stable "modelIndex:childIdx/childIdx/…" path for an item (null when it cannot be located).</summary>
    internal static string? ComputePath(Document doc, ModelItem item)
    {
        var indices = new List<int>();
        var current = item;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            var index = 0;
            var found = -1;
            foreach (var child in parent.Children)
            {
                if (child.Equals(current))
                {
                    found = index;
                    break;
                }

                index++;
            }

            if (found < 0)
            {
                return null;
            }

            indices.Add(found);
            current = parent;
        }

        // 'current' is now a model root — find which model it belongs to.
        var modelIndex = -1;
        for (int i = 0; i < doc.Models.Count; i++)
        {
            if (doc.Models[i].RootItem.Equals(current))
            {
                modelIndex = i;
                break;
            }
        }

        if (modelIndex < 0)
        {
            return null;
        }

        indices.Reverse();
        return modelIndex.ToString(CultureInfo.InvariantCulture) + ":" +
               string.Join("/", indices.Select(n => n.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Walks a path back to a model item, or null when the tree has changed since capture.</summary>
    internal static ModelItem? ResolvePath(Document doc, string path)
    {
        try
        {
            var colon = path.IndexOf(':');
            if (colon < 0)
            {
                return null;
            }

            var modelIndex = int.Parse(path.Substring(0, colon), CultureInfo.InvariantCulture);
            if (modelIndex < 0 || modelIndex >= doc.Models.Count)
            {
                return null;
            }

            var current = doc.Models[modelIndex].RootItem;
            var rest = path.Substring(colon + 1);
            if (rest.Length > 0)
            {
                foreach (var segment in rest.Split('/'))
                {
                    var idx = int.Parse(segment, CultureInfo.InvariantCulture);
                    current = current.Children.ElementAt(idx);
                }
            }

            return current;
        }
        catch (Exception)
        {
            // The model changed since capture (item removed / re-indexed) — skip it.
            return null;
        }
    }

    /// <summary>Paths of the given items (items that cannot be located are skipped).</summary>
    internal static List<string> ComputePaths(Document doc, IEnumerable<ModelItem> items)
    {
        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = ComputePath(doc, item);
            if (path != null)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>The items behind the paths (paths that no longer resolve are skipped).</summary>
    internal static List<ModelItem> ResolvePaths(Document doc, IEnumerable<string> paths)
    {
        var items = new List<ModelItem>();
        foreach (var path in paths)
        {
            var item = ResolvePath(doc, path);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return items;
    }
}
