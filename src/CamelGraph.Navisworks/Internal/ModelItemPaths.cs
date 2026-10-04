using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Stable references to model items as "modelIndex:childIdx/childIdx/…" paths through the model tree, so a set of
/// items can be stored in a graph file (Captured Selection, the model-element inputs) and found again later. A path alone points at
/// whatever sits at those positions today, so a stored pick also carries the item's identity (its instance GUID, or its name when it
/// has no GUID: see <see cref="PickedEntry"/>) and is checked against it when it is resolved. Old graph files hold paths only and still
/// resolve (without the check).
/// </summary>
internal static class ModelItemPaths
{
    /// <summary>The result of turning stored picks back into items: the items found, in the order picked, and how many were not.</summary>
    internal sealed class PickResolution
    {
        /// <summary>Creates a result.</summary>
        internal PickResolution(List<ModelItem> items, int total)
        {
            Items = items;
            Total = total;
        }

        /// <summary>The items found, in the order they were picked.</summary>
        internal List<ModelItem> Items { get; }

        /// <summary>How many picks there were.</summary>
        internal int Total { get; }

        /// <summary>How many picks could not be found (the model changed, or it is another model).</summary>
        internal int Missing => Total - Items.Count;
    }

    /// <summary>
    /// Remembers where each child sits among its parent's children for the length of one capture, so finding the position of thousands of
    /// siblings reads each parent's children once instead of once per child (which was quadratic).
    /// </summary>
    internal sealed class PathIndexCache
    {
        private readonly Dictionary<ModelItem, Dictionary<ModelItem, int>> _byParent =
            new Dictionary<ModelItem, Dictionary<ModelItem, int>>(ModelItemIdentityComparer.Instance);

        /// <summary>The position of <paramref name="child"/> under <paramref name="parent"/>, or -1 when this cache cannot say.</summary>
        internal int IndexOf(ModelItem parent, ModelItem child)
        {
            if (!_byParent.TryGetValue(parent, out var positions))
            {
                positions = new Dictionary<ModelItem, int>(ModelItemIdentityComparer.Instance);
                var index = 0;
                foreach (var sibling in parent.Children)
                {
                    if (!positions.ContainsKey(sibling))
                    {
                        positions.Add(sibling, index);
                    }

                    index++;
                }

                _byParent[parent] = positions;
            }

            return positions.TryGetValue(child, out var found) ? found : -1;
        }
    }

    /// <summary>A stable "modelIndex:childIdx/childIdx/…" path for an item (null when it cannot be located).</summary>
    internal static string? ComputePath(Document doc, ModelItem item, PathIndexCache? cache = null)
    {
        var indices = new List<int>();
        var current = item;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            var found = cache != null ? cache.IndexOf(parent, current) : -1;
            if (found < 0)
            {
                // The way it was always found (and the way out when the cache cannot place the item).
                var index = 0;
                foreach (var child in parent.Children)
                {
                    if (child.Equals(current))
                    {
                        found = index;
                        break;
                    }

                    index++;
                }
            }

            if (found < 0)
            {
                return null;
            }

            indices.Add(found);
            current = parent;
        }

        // 'current' is now a model root — find which model it belongs to.
        var modelIndex = ModelIndexOfRoot(doc, current);
        if (modelIndex < 0)
        {
            return null;
        }

        indices.Reverse();
        return modelIndex.ToString(CultureInfo.InvariantCulture) + ":" +
               string.Join("/", indices.Select(n => n.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The text stored for a picked item: its path plus its identity (null when the item cannot be located).</summary>
    internal static string? ComputeEntry(Document doc, ModelItem item, PathIndexCache? cache = null)
    {
        var path = ComputePath(doc, item, cache);
        return path == null ? null : PickedEntry.Encode(path, item.InstanceGuid, item.DisplayName);
    }

    /// <summary>Stored entries (path plus identity) of the given items; items that cannot be located are counted in <paramref name="notLocated"/>.</summary>
    internal static List<string> ComputeEntries(Document doc, IEnumerable<ModelItem> items, out int notLocated)
    {
        var cache = new PathIndexCache();
        var entries = new List<string>();
        notLocated = 0;
        foreach (var item in items)
        {
            var entry = ComputeEntry(doc, item, cache);
            if (entry != null)
            {
                entries.Add(entry);
            }
            else
            {
                notLocated++;
            }
        }

        return entries;
    }

    /// <summary>Walks a stored pick (a path, with or without identity) back to a model item without checking it, or null when the tree has changed since capture.</summary>
    internal static ModelItem? ResolvePath(Document doc, string pathOrEntry)
    {
        return WalkPath(doc, PickedEntry.Parse(pathOrEntry).Path);
    }

    /// <summary>
    /// The item a stored pick stands for: walked by its path and accepted when it is the picked element (or when the pick carries no
    /// identity); null when the path leads somewhere else or nowhere. Does not look the element up by GUID elsewhere (see
    /// <see cref="ResolveEntries"/>); cheap enough for the node face.
    /// </summary>
    internal static ModelItem? ResolveEntryQuick(Document doc, string entryText)
    {
        var entry = PickedEntry.Parse(entryText);
        var item = WalkPath(doc, entry.Path);
        return item != null && PickedEntry.Check(entry, item.InstanceGuid, item.DisplayName) != PickCheck.Mismatch ? item : null;
    }

    /// <summary>
    /// Turns stored picks back into items, in the order picked. A pick whose path leads to a different element (the model was rebuilt,
    /// another model appended, the script runs on another project) is looked up by its GUID in one pass over the model; what still cannot
    /// be found is counted, never replaced by whatever sits at the old position. Entries without identity (an old file) are taken by path.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="entries">The stored picks.</param>
    internal static PickResolution ResolveEntries(Document doc, IReadOnlyList<string> entries)
    {
        var parsed = new List<PickedEntry>(entries.Count);
        foreach (var text in entries)
        {
            parsed.Add(PickedEntry.Parse(text));
        }

        var slots = new ModelItem?[parsed.Count];
        var lookup = new List<int>();
        for (var i = 0; i < parsed.Count; i++)
        {
            var item = WalkPath(doc, parsed[i].Path);
            if (item != null && PickedEntry.Check(parsed[i], item.InstanceGuid, item.DisplayName) != PickCheck.Mismatch)
            {
                slots[i] = item;
            }
            else if (parsed[i].Guid != Guid.Empty)
            {
                lookup.Add(i);
            }
        }

        if (lookup.Count > 0)
        {
            var wanted = new HashSet<Guid>(lookup.Select(i => parsed[i].Guid));
            var found = new Dictionary<Guid, List<ModelItem>>();
            foreach (var candidate in ModelDataReader.AllItems(doc))
            {
                var guid = candidate.InstanceGuid;
                if (guid == Guid.Empty || !wanted.Contains(guid))
                {
                    continue;
                }

                if (!found.TryGetValue(guid, out var list))
                {
                    list = new List<ModelItem>();
                    found[guid] = list;
                }

                list.Add(candidate);
            }

            foreach (var i in lookup)
            {
                found.TryGetValue(parsed[i].Guid, out var candidates);
                var modelIndex = parsed[i].ModelIndex;
                if (PickedEntry.TryChoose(candidates, candidate => IsInModel(doc, modelIndex, candidate), out var chosen))
                {
                    slots[i] = chosen;
                }
            }
        }

        var items = new List<ModelItem>(parsed.Count);
        foreach (var slot in slots)
        {
            if (slot != null)
            {
                items.Add(slot);
            }
        }

        return new PickResolution(items, parsed.Count);
    }

    /// <summary>The items behind the stored picks (picks that cannot be found are left out).</summary>
    internal static List<ModelItem> ResolvePaths(Document doc, IEnumerable<string> entries) =>
        ResolveEntries(doc, entries.ToList()).Items;

    private static ModelItem? WalkPath(Document doc, string path)
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

    private static int ModelIndexOfRoot(Document doc, ModelItem root)
    {
        for (int i = 0; i < doc.Models.Count; i++)
        {
            if (doc.Models[i].RootItem.Equals(root))
            {
                return i;
            }
        }

        return -1;
    }

    // Whether the item lies in the model with this index (the model the pick was made in).
    private static bool IsInModel(Document doc, int modelIndex, ModelItem item)
    {
        if (modelIndex < 0 || modelIndex >= doc.Models.Count)
        {
            return false;
        }

        var root = item;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        return doc.Models[modelIndex].RootItem.Equals(root);
    }
}
