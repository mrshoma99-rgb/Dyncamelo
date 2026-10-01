using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes.Internal;

namespace Dyncamelo.Nodes;

/// <summary>
/// More tests and choices: a comparison that works on numbers, text and dates, ranges, exclusive-or, and ways to pick a value
/// by index or by matching a case.
/// </summary>
[NodeCategory("Logic")]
public static class LogicExtraNodes
{
    /// <summary>Compares two values with a chosen test.</summary>
    /// <param name="a">The value to test.</param>
    /// <param name="b">What to test it against (unused by the isNull / notNull / isEmpty / notEmpty tests).</param>
    /// <param name="test">The test: ==, !=, &gt;, &gt;=, &lt;, &lt;=, contains, !contains, startsWith, endsWith, matches (wildcards * and ?), regex, isNull, notNull, isEmpty, notEmpty.</param>
    /// <param name="ignoreCase">True (default) ignores upper/lower case when comparing text.</param>
    /// <returns>True when the test passes.</returns>
    [NodeName("Logic.Compare")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription("Compares two values — numbers, text or dates — with a test chosen from a list (==, >, contains, matches, regex …). Text that reads as a number is compared as a number.")]
    [NodeSearchTags("compare", "greater", "less", "equal", "date", "text", "contains", "wildcard", "regex", "test")]
    public static bool Compare(
        object? a,
        object? b = null,
        [NodeChoices("==", "!=", ">", ">=", "<", "<=", "contains", "!contains", "startsWith", "endsWith", "matches", "regex", "isNull", "notNull", "isEmpty", "notEmpty")]
        string test = "==",
        bool ignoreCase = true)
    {
        var op = ValueTests.Normalize(test, "Logic.Compare");
        return ValueTests.Test(op, a, b, ignoreCase, "Logic.Compare");
    }

    /// <summary>Tests whether two values are different.</summary>
    /// <param name="a">First value.</param>
    /// <param name="b">Second value.</param>
    /// <returns>True when the values are not equal.</returns>
    [NodeName("Logic.NotEquals")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription("True when two values are different (numbers compare by value regardless of numeric type).")]
    [NodeSearchTags("!=", "different", "not equal", "unequal")]
    public static bool NotEquals(object? a, object? b)
    {
        return !ValueComparison.AreEqual(a, b);
    }

    /// <summary>Exclusive or: true when exactly one input is true.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when the two inputs differ.</returns>
    [NodeName("Logic.Xor")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription("True when exactly one of the two inputs is true.")]
    [NodeSearchTags("exclusive or", "either", "one of")]
    public static bool Xor(bool a, bool b)
    {
        return a != b;
    }

    /// <summary>Tests whether a value lies within a range.</summary>
    /// <param name="value">The value to test (number, text or date).</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="inclusive">True (default) counts the bounds themselves as inside.</param>
    /// <returns>True when the value is within the range.</returns>
    [NodeName("Logic.IsBetween")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription("True when a number, text or date lies between a lower and an upper bound (bounds included by default).")]
    [NodeSearchTags("range", "within", "between", "inside", "interval", "tolerance")]
    public static bool IsBetween(object? value, object? min, object? max, bool inclusive = true)
    {
        if (value == null || min == null || max == null)
        {
            throw new ArgumentNullException(nameof(value), "Logic.IsBetween needs a value and both bounds; one of them is empty.");
        }

        var low = ValueTests.Order(value, min, "Logic.IsBetween");
        var high = ValueTests.Order(value, max, "Logic.IsBetween");
        return inclusive ? low >= 0 && high <= 0 : low > 0 && high < 0;
    }

    /// <summary>Picks one of several options by position.</summary>
    /// <param name="index">Which option, counting from 0.</param>
    /// <param name="options">The options (wire several nodes into the one socket, or a list).</param>
    /// <returns>The option at that index.</returns>
    [NodeName("Logic.Choose")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("value")]
    [NodeDescription("Picks one of several options by position (0 = first) — a multi-way If.")]
    [NodeSearchTags("select", "pick", "switch", "index", "option", "multiplexer")]
    public static object? Choose(int index, [MultiInput] IList<object?> options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options), "Logic.Choose requires options. Wire the values to choose between into the 'options' input.");
        }

        if (index < 0 || index >= options.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Logic.Choose: index " + index.ToString(CultureInfo.InvariantCulture) + " is outside the " +
                options.Count.ToString(CultureInfo.InvariantCulture) + " option(s) (counting from 0).");
        }

        return options[index];
    }

    /// <summary>Looks a value up in a list of cases and gives the matching result.</summary>
    /// <param name="value">The value to look for.</param>
    /// <param name="cases">The values it can be.</param>
    /// <param name="results">What to give for each case (same length as the cases).</param>
    /// <param name="fallback">What to give when no case matches (nothing by default).</param>
    /// <returns>The result for the first matching case, or the fallback.</returns>
    [NodeName("Logic.Switch")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("value")]
    [NodeDescription("Gives the result that goes with the first case equal to the value, otherwise the fallback — a status-to-colour or code-to-name mapping.")]
    [NodeSearchTags("case", "match", "map", "lookup", "translate", "switch", "select")]
    public static object? Switch(object? value, IList<object?> cases, IList<object?> results, object? fallback = null)
    {
        if (cases == null)
        {
            throw new ArgumentNullException(nameof(cases), "Logic.Switch requires a list of cases.");
        }

        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "Logic.Switch requires a list of results, one per case.");
        }

        if (cases.Count != results.Count)
        {
            throw new ArgumentException(
                "Logic.Switch: there are " + cases.Count.ToString(CultureInfo.InvariantCulture) + " case(s) but " +
                results.Count.ToString(CultureInfo.InvariantCulture) + " result(s); they must be the same length.");
        }

        for (int i = 0; i < cases.Count; i++)
        {
            if (ValueTests.AreEqual(value, cases[i], ignoreCase: true))
            {
                return results[i];
            }
        }

        return fallback;
    }

    /// <summary>Names the kind of a value.</summary>
    /// <param name="value">Any value.</param>
    /// <returns>One of: null, number, text, boolean, datetime, list, dictionary, or the type's name.</returns>
    [NodeName("Logic.TypeOf")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("type")]
    [NodeDescription("Names the kind of a value — number, text, boolean, datetime, list, dictionary, null or the object's type — for debugging what a node really returns.")]
    [NodeSearchTags("type", "kind", "debug", "inspect", "what is this", "class")]
    public static string TypeOf(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string _:
                return "text";
            case bool _:
                return "boolean";
            case DateTime _:
                return "datetime";
            case IDictionary _:
                return "dictionary";
            case IList _:
                return "list";
        }

        return ValueComparison.IsNumeric(value) ? "number" : value.GetType().Name;
    }
}
