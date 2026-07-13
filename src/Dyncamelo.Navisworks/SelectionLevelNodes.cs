using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Nodes that re-resolve model items to another level of the selection tree,
/// mirroring Navisworks Options &gt; Interface &gt; Selection &gt; "Resolution".
/// </summary>
[NodeCategory("Navisworks.Selection")]
public static class SelectionLevelNodes
{
    /// <summary>
    /// Maps every item to its representative at a selection-resolution level,
    /// like changing Options &gt; Interface &gt; Selection &gt; Resolution and
    /// re-picking in Navisworks. Levels, in Navisworks terms:
    /// <list type="bullet">
    /// <item><description><c>File</c> — the model file node the item came from (the root of its path).</description></item>
    /// <item><description><c>Layer</c> — the layer containing the item (the file node when the model has no layers).</description></item>
    /// <item><description><c>FirstObject</c> — the highest object in the path below any files, layers and collections (the outermost composite/insert object).</description></item>
    /// <item><description><c>LastObject</c> — the last object in the path: the composite/insert object closest to the geometry, or the item itself when there is none. This is Navisworks' default resolution.</description></item>
    /// <item><description><c>LastUnique</c> — the last (deepest) item in the path that is not multiply instanced, i.e. not shared by several inserts (Navisworks' "last unique" resolution).</description></item>
    /// <item><description><c>Geometry</c> — the geometry leaves themselves (an item above geometry expands to every geometry leaf beneath it).</description></item>
    /// <item><description><c>Self</c> — no resolution; the items pass through unchanged.</description></item>
    /// </list>
    /// Resolution walks up the selection tree, so an item already sitting above
    /// the requested level is returned unchanged (except <c>Geometry</c>, which
    /// collects the leaves beneath it). The output is deduplicated and keeps the
    /// input order.
    /// </summary>
    /// <param name="modelItems">The model items to resolve.</param>
    /// <param name="level">The resolution level: File, Layer, FirstObject, LastObject, LastUnique, Geometry or Self (case insensitive; spaces allowed, e.g. "Last Object").</param>
    /// <returns>The resolved items (duplicates removed, input order preserved).</returns>
    [NodeName("Selection.Resolve")]
    [NodeDescription("Re-selects items at another selection-tree level — File, Layer, FirstObject, LastObject, LastUnique, Geometry — like Navisworks' selection resolution option (Options > Interface > Selection).")]
    [NodeSearchTags("selection", "resolve", "resolution", "level", "file", "layer", "first object", "last object", "last unique", "geometry", "parent", "ancestor")]
    [return: NodeName("items")]
    public static List<ModelItem> Resolve(
        IEnumerable<ModelItem> modelItems,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string level = "LastObject")
    {
        if (modelItems == null)
        {
            throw new ArgumentNullException(nameof(modelItems), "No model items provided.");
        }

        return SelectionLevels.Resolve(modelItems, SelectionLevels.Parse(level));
    }
}

/// <summary>
/// The selection-resolution levels of Navisworks (Options &gt; Interface &gt;
/// Selection &gt; Resolution), plus <see cref="Self"/> for "no resolution".
/// </summary>
internal enum SelectionLevel
{
    /// <summary>No resolution — items pass through unchanged.</summary>
    Self,

    /// <summary>The model file node the item came from (root of its path).</summary>
    File,

    /// <summary>The layer containing the item.</summary>
    Layer,

    /// <summary>The outermost object below files, layers and collections.</summary>
    FirstObject,

    /// <summary>The innermost composite/insert object (Navisworks' default).</summary>
    LastObject,

    /// <summary>The innermost item that is not multiply instanced.</summary>
    LastUnique,

    /// <summary>The geometry leaves themselves.</summary>
    Geometry,
}

/// <summary>
/// Shared resolver behind <c>Selection.Resolve</c> and the <c>resolveTo</c>
/// inputs on the Search/Selection pickers. Internal — never surfaced as nodes.
/// </summary>
internal static class SelectionLevels
{
    /// <summary>
    /// Parses a user-supplied level name. Case insensitive; spaces, hyphens and
    /// underscores are ignored ("last object" == "LastObject"). Throws with the
    /// full list of valid names on anything else.
    /// </summary>
    internal static SelectionLevel Parse(string? level)
    {
        var normalized = (level ?? string.Empty)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();

        switch (normalized)
        {
            case "self": return SelectionLevel.Self;
            case "file": return SelectionLevel.File;
            case "layer": return SelectionLevel.Layer;
            case "firstobject": return SelectionLevel.FirstObject;
            case "lastobject": return SelectionLevel.LastObject;
            case "lastunique": return SelectionLevel.LastUnique;
            case "geometry": return SelectionLevel.Geometry;
            default:
                throw new ArgumentException(
                    "'" + level + "' is not a selection resolution level. Use one of: " +
                    "Self, File, Layer, FirstObject, LastObject, LastUnique, Geometry " +
                    "(like Options > Interface > Selection > Resolution in Navisworks).");
        }
    }

    /// <summary>
    /// Resolves every item to its representative(s) at the requested level.
    /// Nulls are dropped; for every level except <see cref="SelectionLevel.Self"/>
    /// the output is deduplicated (by scene node identity) preserving order.
    /// </summary>
    internal static List<ModelItem> Resolve(IEnumerable<ModelItem> items, SelectionLevel level)
    {
        var results = new List<ModelItem>();
        if (level == SelectionLevel.Self)
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        var seen = new ModelItemSet();
        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            if (level == SelectionLevel.Geometry)
            {
                // Geometry is the one level that maps downward: expand to the
                // geometry leaves at or beneath the item.
                foreach (var leaf in item.DescendantsAndSelf)
                {
                    if (leaf.HasGeometry && seen.Add(leaf))
                    {
                        results.Add(leaf);
                    }
                }

                continue;
            }

            var resolved = ResolveSingle(item, level);
            if (seen.Add(resolved))
            {
                results.Add(resolved);
            }
        }

        return results;
    }

    /// <summary>
    /// Resolves one item along its ancestor path (root file first, the item
    /// itself last). Upward-only: when the item already sits above the requested
    /// level, the item itself is returned.
    /// </summary>
    private static ModelItem ResolveSingle(ModelItem item, SelectionLevel level)
    {
        var path = PathFromRoot(item);
        switch (level)
        {
            case SelectionLevel.File:
                // The root of the path is the model file node.
                return path[0];

            case SelectionLevel.Layer:
                // The nearest enclosing layer; the file node when there is none
                // (native resolution falls back to selecting the whole file).
                for (int i = path.Count - 1; i >= 0; i--)
                {
                    if (path[i].IsLayer)
                    {
                        return path[i];
                    }
                }

                return path[0];

            case SelectionLevel.FirstObject:
                // The highest item below the file that is not a layer or a
                // collection — the outermost composite/insert/geometry object.
                for (int i = 1; i < path.Count; i++)
                {
                    var candidate = path[i];
                    if (!candidate.IsLayer && !candidate.IsCollection)
                    {
                        return candidate;
                    }
                }

                return item;

            case SelectionLevel.LastObject:
                // The last object in the path: composite/insert objects take
                // precedence over their geometry, exactly like the native
                // default resolution; a bare geometry leaf resolves to itself.
                for (int i = path.Count - 1; i >= 1; i--)
                {
                    var candidate = path[i];
                    if (candidate.IsComposite || candidate.IsInsert)
                    {
                        return candidate;
                    }
                }

                return item;

            case SelectionLevel.LastUnique:
                // Navisworks' documented rule: the last object in the path that
                // is NOT multiply instanced. An item shared by several inserts
                // resolves upward to its first non-shared ancestor. When every
                // item below the root is multiply instanced, fall back to the
                // default (LastObject) resolution rather than the file node.
                for (int i = path.Count - 1; i >= 1; i--)
                {
                    if (!IsMultiplyInstanced(path[i]))
                    {
                        return path[i];
                    }
                }

                return ResolveSingle(item, SelectionLevel.LastObject);

            default:
                return item;
        }
    }

    /// <summary>The item's ancestor path, root (file node) first, the item itself last.</summary>
    private static List<ModelItem> PathFromRoot(ModelItem item)
    {
        var path = new List<ModelItem>();
        for (var current = item; current != null; current = current.Parent)
        {
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Whether the scene node is shared by more than one insert (multiply
    /// instanced). <c>ModelItem.Instances</c> enumerates every occurrence of
    /// the underlying node; a non-instanced item yields at most one.
    /// </summary>
    private static bool IsMultiplyInstanced(ModelItem item)
    {
        int count = 0;
        foreach (var instance in item.Instances)
        {
            if (++count > 1)
            {
                return true;
            }
        }

        return false;
    }
}
