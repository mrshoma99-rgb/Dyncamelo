using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// More tests and choices: a comparison that works on numbers, text and dates, ranges, exclusive-or, and ways to pick a value
/// by index or by matching a case.
/// </summary>
[NodeCategory("Logic")]
public static class LogicExtraNodes
{
    /// <summary>Compares two values with a chosen test.</summary>
    /// <param name="a">The value to test. A list is tested as one value; set List Levels to @L1 on this input to test every element instead.</param>
    /// <param name="b">What to test it against (unused by the isNull / notNull / isEmpty / notEmpty tests).</param>
    /// <param name="test">The test: ==, !=, &gt;, &gt;=, &lt;, &lt;=, contains, !contains, startsWith, endsWith, matches (wildcards * and ?), regex, isNull, notNull, isEmpty, notEmpty.</param>
    /// <param name="ignoreCase">True (default) ignores upper/lower case when comparing text.</param>
    /// <returns>True when the test passes.</returns>
    [NodeName("Logic.Compare")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription(
        "Compares two values — numbers, text or dates — with a test chosen from a list (==, >, contains, matches, regex …). " +
        "Text that reads as a number is compared as a number, and text that reads as a date (2026-10-01) is compared as a " +
        "date when the other side is a date. Text ignores upper and lower case unless ignoreCase is off (the regex test is " +
        "the same). A missing value (empty or blank) is never greater or less than anything, so it does not pass > >= < <=; " +
        "use isNull or isEmpty to find it. Equals and Logic.NotEquals follow the same rules for == and !=. A list on a is " +
        "tested as ONE value: to test every element set List Levels to @L1 on a (or use List.FilterByValue to split a list). " +
        "For numbers within a tolerance use Math.IsClose.")]
    [NodeSearchTags("compare", "greater", "less", "equal", "date", "text", "contains", "wildcard", "regex", "test", "tolerance")]
    public static bool Compare(
        [AcceptsNull] object? a,
        [AcceptsNull] object? b = null,
        [NodeChoices("==", "!=", ">", ">=", "<", "<=", "contains", "!contains", "startsWith", "endsWith", "matches", "regex", "isNull", "notNull", "isEmpty", "notEmpty")]
        string test = "==",
        bool ignoreCase = true)
    {
        var op = ValueTests.Normalize(test, "Logic.Compare");
        return ValueTests.Test(op, a, b, ignoreCase, "Logic.Compare");
    }

    /// <summary>Tests whether two values are different.</summary>
    /// <param name="a">First value. A list is compared as one value; set List Levels to @L1 to compare every element instead.</param>
    /// <param name="b">Second value.</param>
    /// <returns>True when the values are not equal.</returns>
    [NodeName("Logic.NotEquals")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription(
        "True when two values are different — the opposite of Equals, with the same rules: numbers compare by value, text " +
        "ignores upper and lower case, text that reads as a number or a date is compared as one, and two lists are equal when " +
        "they have the same items in the same order. A list is compared as ONE value; to test every element of a list set " +
        "List Levels to @L1 on a.")]
    [NodeSearchTags("!=", "different", "not equal", "unequal")]
    public static bool NotEquals([AcceptsNull] object? a, [AcceptsNull] object? b)
    {
        return !ValueTests.AreEqual(a, b, ignoreCase: true);
    }

    /// <summary>Exclusive or: true when exactly one input is true.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when the two inputs differ.</returns>
    [NodeName("Logic.Xor")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription("True when exactly one of the two inputs is true.")]
    [NodeSearchTags("exclusive or", "either", "one of")]
    public static bool Xor(bool a, bool b)
    {
        return a != b;
    }

    /// <summary>Tests whether a value lies within a range.</summary>
    /// <param name="value">The value to test (number, text or date). A list tests every element and gives a list of results. A missing value (empty or blank) is never between.</param>
    /// <param name="min">The lower bound. A list pairs with the values.</param>
    /// <param name="max">The upper bound. A list pairs with the values.</param>
    /// <param name="inclusive">True (default) counts the bounds themselves as inside.</param>
    /// <returns>True when the value is within the range.</returns>
    [NodeName("Logic.IsBetween")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription(
        "True when a number, text or date lies between a lower and an upper bound (bounds included by default). Text that " +
        "reads as a number or, next to a date, as a date is compared as one. A list on value (or on a bound) gives a list " +
        "of answers, one per element, so a whole column can be tested at once. A missing value, or text that is not a " +
        "number next to numbers, is not between; an empty bound is an error. To test a number against a target within a " +
        "tolerance use Math.IsClose.")]
    [NodeSearchTags("range", "within", "between", "inside", "interval", "tolerance")]
    public static bool IsBetween([ScalarInput, AcceptsNull] object? value, [ScalarInput] object? min, [ScalarInput] object? max, bool inclusive = true)
    {
        if (min == null || max == null)
        {
            throw new ArgumentNullException(
                min == null ? nameof(min) : nameof(max),
                "Logic.IsBetween needs both bounds; the " + (min == null ? "minimum" : "maximum") + " is empty. Wire a number, text or date into it.");
        }

        if (!ValueTests.TryOrder(value, min, "Logic.IsBetween", out var low) ||
            !ValueTests.TryOrder(value, max, "Logic.IsBetween", out var high))
        {
            return false;
        }

        return inclusive ? low >= 0 && high <= 0 : low > 0 && high < 0;
    }

    /// <summary>Picks one of several options by position.</summary>
    /// <param name="index">Which option, counting from 0.</param>
    /// <param name="options">The options (wire several nodes into the one socket, or a list).</param>
    /// <returns>The option at that index.</returns>
    [NodeName("Logic.Choose")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("value")]
    [NodeDescription(
        "Picks one of several options by position (0 = first) — a multi-way If. All options are always computed, so a node " +
        "wired into an option that is not picked still runs: to skip nodes use Flow.When.")]
    [NodeSearchTags("select", "pick", "switch", "index", "option", "multiplexer", "flow", "when")]
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
    /// <param name="value">The value to look for. A list looks every element up and gives a list of results (a status column becomes a colour column).</param>
    /// <param name="cases">The values it can be.</param>
    /// <param name="results">What to give for each case (same length as the cases).</param>
    /// <param name="fallback">What to give when no case matches (nothing by default).</param>
    /// <returns>The result for the first matching case, or the fallback.</returns>
    [NodeName("Logic.Switch")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("value")]
    [NodeDescription(
        "Gives the result that goes with the first case equal to the value, otherwise the fallback — a status-to-colour or " +
        "code-to-name mapping. Matching follows Equals: text ignores upper and lower case and text that reads as a number " +
        "matches that number. A list on value is looked up element by element and gives a list of results. All results are " +
        "computed whether or not they are picked; to skip nodes use Flow.When.")]
    [NodeSearchTags("case", "match", "map", "lookup", "translate", "switch", "select", "flow", "when")]
    public static object? Switch([ScalarInput, AcceptsNull] object? value, IList<object?> cases, IList<object?> results, object? fallback = null)
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
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
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
