using System;
using System.Collections;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// What a clash node found when it unpacked an input that may be one thing or a (nested) list of things:
/// the things in order, whether it was a list at all, and how many empty entries were left out.
/// </summary>
/// <typeparam name="T">The kind of thing expected (clash result, clash test...).</typeparam>
[IsVisibleInLibrary(false)]
public sealed class ClashInputList<T>
    where T : class
{
    internal ClashInputList(List<T> items, bool wasSequence, int skippedNulls, object? firstWrong)
    {
        Items = items;
        WasSequence = wasSequence;
        SkippedNulls = skippedNulls;
        FirstWrong = firstWrong;
    }

    /// <summary>The things found, nested lists flattened, in input order.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>True when the input was a list (even a list of one or none); false when it was a single value.</summary>
    public bool WasSequence { get; }

    /// <summary>How many empty (null) entries were left out.</summary>
    public int SkippedNulls { get; }

    /// <summary>The first entry that was not a <typeparamref name="T"/>, or null when all were.</summary>
    public object? FirstWrong { get; }
}

/// <summary>
/// Unpacks the inputs of the clash nodes that take "one result, or a list of results" in a single object-typed port.
/// Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashInputs
{
    /// <summary>
    /// Flattens a value that is one thing or a list (of lists...) of things. A text is one thing, never a list of characters.
    /// </summary>
    /// <typeparam name="T">The kind of thing expected.</typeparam>
    /// <param name="value">The wired value.</param>
    /// <returns>The things, whether the input was a list, how many nulls were left out and the first entry of the wrong kind.</returns>
    public static ClashInputList<T> Flatten<T>(object? value)
        where T : class
    {
        var items = new List<T>();
        var nulls = 0;
        object? firstWrong = null;
        var wasSequence = value is IEnumerable && !(value is string);
        Collect(value, items, ref nulls, ref firstWrong);
        return new ClashInputList<T>(items, wasSequence, nulls, firstWrong);
    }

    private static void Collect<T>(object? value, List<T> items, ref int nulls, ref object? firstWrong)
        where T : class
    {
        if (value == null)
        {
            nulls++;
            return;
        }

        if (value is T match)
        {
            items.Add(match);
            return;
        }

        if (value is IEnumerable sequence && !(value is string))
        {
            foreach (var element in sequence)
            {
                Collect(element, items, ref nulls, ref firstWrong);
            }

            return;
        }

        firstWrong = firstWrong ?? value;
    }
}
