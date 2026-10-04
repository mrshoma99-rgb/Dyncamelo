using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The pure half of Model.Remove with several models: removing a model renumbers the ones after it, so a list of models must be
/// resolved to positions in the document as it is NOW, and the positions then removed from the highest down. Removing 0 and then
/// 1 one at a time takes out the original models 0 and 2, because the second index is read after the first removal. Pure (no
/// Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ModelRemoval
{
    /// <summary>The positions to remove, each once, the highest first.</summary>
    /// <param name="positions">The positions of the models to remove, resolved against the models as they are before any removal; repeats and any order are fine.</param>
    /// <returns>The distinct positions in descending order, the order in which removing them leaves the others where they were.</returns>
    public static List<int> RemovalOrder(IEnumerable<int> positions)
    {
        var distinct = new SortedSet<int>(positions);
        var order = new List<int>(distinct.Count);
        foreach (var position in distinct)
        {
            order.Insert(0, position);
        }

        return order;
    }
}
