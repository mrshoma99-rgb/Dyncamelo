using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Gives every distinct model item a key for the length of one run: its InstanceGuid when the source format has one, otherwise a
/// number handed out per scene node (two wrappers of one scene node are the same node, see <see cref="ModelItemSet"/>). Unlike the
/// display-name path, two sibling elements that share a name never get the same key. Used to recognise the same element in
/// results that come from different tests. Internal — never surfaced as a node.
/// </summary>
internal sealed class ModelItemKeyer
{
    private readonly Dictionary<int, List<KeyValuePair<ModelItem, int>>> _byHash = new Dictionary<int, List<KeyValuePair<ModelItem, int>>>();
    private int _next;

    /// <summary>The key of an item; empty for no item.</summary>
    internal string Key(ModelItem? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var guid = item.InstanceGuid;
        if (guid != Guid.Empty)
        {
            return "guid:" + guid.ToString("N");
        }

        var hash = item.InstanceHashCode;
        if (!_byHash.TryGetValue(hash, out var bucket))
        {
            bucket = new List<KeyValuePair<ModelItem, int>>();
            _byHash[hash] = bucket;
        }

        foreach (var entry in bucket)
        {
            if (entry.Key.IsSameInstance(item))
            {
                return "node:" + entry.Value;
            }
        }

        var id = _next++;
        bucket.Add(new KeyValuePair<ModelItem, int>(item, id));
        return "node:" + id;
    }
}
