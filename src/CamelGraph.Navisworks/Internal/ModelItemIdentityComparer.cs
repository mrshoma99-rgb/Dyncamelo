using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Says when two <see cref="ModelItem"/> wrappers are the same model item: the same rule <see cref="ModelItemSet"/> uses (two
/// wrappers for one scene node are distinct CLR objects, so the hash is <see cref="ModelItem.InstanceHashCode"/> and equality is
/// <see cref="ModelItem.IsSameInstance"/>). Lets a plain <c>Dictionary</c> or <c>HashSet</c> hold model items. Internal — never
/// surfaced as a node.
/// </summary>
internal sealed class ModelItemIdentityComparer : IEqualityComparer<ModelItem>
{
    /// <summary>The one instance (the comparer has no state).</summary>
    internal static readonly ModelItemIdentityComparer Instance = new ModelItemIdentityComparer();

    private ModelItemIdentityComparer()
    {
    }

    /// <inheritdoc />
    public bool Equals(ModelItem? x, ModelItem? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        return x != null && y != null && x.IsSameInstance(y);
    }

    /// <inheritdoc />
    public int GetHashCode(ModelItem item) => item.InstanceHashCode;
}
