using System;
using System.Collections;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// What to say when a list arrives at a socket that takes ONE point, vector or matrix. Those sockets accept a plain list of numbers
/// as a single value ([x, y, z] is one point), so they cannot simply be mapped over a list: a list of three points and a list of
/// three numbers look alike to the engine. The node therefore recognises "a list of values" itself and says how to run once per
/// value (List Levels on the socket) instead of failing on a raw conversion error. Pure (no Navisworks types), so it is
/// unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class SeveralValues
{
    /// <summary>
    /// True when the list holds several values where one was expected: it contains a list (a point written as numbers, one of
    /// several) or an object that is not a number or text (a point, vector or matrix object). A flat list of numbers or numeric
    /// text is ONE value and returns false.
    /// </summary>
    /// <param name="list">The list that arrived.</param>
    /// <returns>True when it is a list of values rather than one value written as numbers.</returns>
    public static bool HoldsSeveral(IList list)
    {
        if (list == null)
        {
            return false;
        }

        foreach (var element in list)
        {
            if (element == null)
            {
                continue;
            }

            if (element is IList && !(element is string))
            {
                return true;
            }

            if (!IsNumberLike(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the list is several matrices rather than one: nested lists that are not exactly four rows of four numbers
    /// (four rows of four is one matrix), or objects that are not numbers.
    /// </summary>
    /// <param name="list">The list that arrived.</param>
    /// <returns>True when it holds more than one matrix.</returns>
    public static bool HoldsSeveralMatrices(IList list)
    {
        if (list == null)
        {
            return false;
        }

        foreach (var element in list)
        {
            if (element is IList inner && !(element is string))
            {
                if (inner.Count != 4 || list.Count != 4)
                {
                    return true;
                }
            }
            else if (element != null && !IsNumberLike(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The sentence for a person: what arrived, what the input takes, and how to run the node once per value.</summary>
    /// <param name="thing">What the input takes, singular ("point", "vector", "matrix").</param>
    /// <param name="list">The list that arrived.</param>
    /// <returns>One or two plain sentences, no type names.</returns>
    public static string Describe(string thing, IList list)
    {
        var count = list == null ? 0 : list.Count;
        var nested = false;
        if (list != null)
        {
            foreach (var element in list)
            {
                if (element is IList && !(element is string))
                {
                    nested = true;
                    break;
                }
            }
        }

        var level = nested ? "L2" : "L1";
        var why = nested
            ? "(L1 would cut each list into single numbers)."
            : string.Empty;
        return "This input takes one " + thing + " but received a list of " + count.ToString(CultureInfo.InvariantCulture) + " values. " +
               "To run the node once for each " + thing + ", right-click the input, choose List Levels and set " + level +
               (why.Length == 0 ? "." : " " + why);
    }

    private static bool IsNumberLike(object value)
    {
        if (value is string)
        {
            return true; // numeric text is read as a number by the converters; anything else fails there with its own message
        }

        if (value is bool || value is char || value is DateTime)
        {
            return false;
        }

        return value is IConvertible;
    }
}
