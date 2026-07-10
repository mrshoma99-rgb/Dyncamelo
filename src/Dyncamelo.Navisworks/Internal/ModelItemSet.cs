using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// An identity set of model items. Two <see cref="ModelItem"/> wrappers for the
/// same scene node are distinct CLR objects, so membership is keyed on
/// <see cref="ModelItem.InstanceHashCode"/> and confirmed with
/// <see cref="ModelItem.IsSameInstance"/>. Internal — never surfaced as a node.
/// </summary>
internal sealed class ModelItemSet
{
    private readonly Dictionary<int, List<ModelItem>> _buckets = new Dictionary<int, List<ModelItem>>();

    /// <summary>Adds an item; returns false when the same scene node is already present.</summary>
    internal bool Add(ModelItem item)
    {
        var hash = item.InstanceHashCode;
        if (!_buckets.TryGetValue(hash, out var bucket))
        {
            bucket = new List<ModelItem>();
            _buckets[hash] = bucket;
        }

        foreach (var existing in bucket)
        {
            if (existing.IsSameInstance(item))
            {
                return false;
            }
        }

        bucket.Add(item);
        return true;
    }
}
