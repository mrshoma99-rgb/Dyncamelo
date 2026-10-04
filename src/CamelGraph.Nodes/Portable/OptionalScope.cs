using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>What an optional list input that narrows a node's work was given.</summary>
public enum ScopeKind
{
    /// <summary>The input was not wired: the node works on everything.</summary>
    Everything,

    /// <summary>The input holds items: the node works on exactly those.</summary>
    Given,

    /// <summary>The input was wired but holds nothing (an upstream search found nothing): the node has nothing to work on.</summary>
    Nothing,
}

/// <summary>
/// The rule for an optional list input such as the <c>items</c> of Model.Statistics: an input that is not wired means "the whole
/// document", an input that is wired and empty means "nothing", never "the whole document" by accident. The engine hands a node
/// <c>null</c> for an input that is not wired and an empty list for a wired empty one, so the two can be told apart. Pure (no
/// Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class OptionalScope
{
    /// <summary>Classifies an optional list input and returns the items it holds (null entries are left out).</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">What the node received: null when unwired.</param>
    /// <param name="given">The items that were given, without null entries; empty for <see cref="ScopeKind.Everything"/> and <see cref="ScopeKind.Nothing"/>.</param>
    /// <returns>Which of the three cases applies.</returns>
    public static ScopeKind Classify<T>(IEnumerable<T>? items, out List<T> given)
        where T : class
    {
        given = new List<T>();
        if (items == null)
        {
            return ScopeKind.Everything;
        }

        foreach (var item in items)
        {
            if (item != null)
            {
                given.Add(item);
            }
        }

        return given.Count == 0 ? ScopeKind.Nothing : ScopeKind.Given;
    }
}
