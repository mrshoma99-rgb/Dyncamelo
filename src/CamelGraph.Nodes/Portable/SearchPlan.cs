using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>How Search.ByProperty (and the live search sets) compares a property with the value.</summary>
public enum SearchMode
{
    /// <summary>The property equals the value (any data type it could be stored as).</summary>
    Equal,

    /// <summary>The property's text contains the value.</summary>
    Contains,

    /// <summary>The property's text matches the wildcard pattern (* and ?).</summary>
    Wildcard,

    /// <summary>The property is a number greater than the value.</summary>
    GreaterThan,

    /// <summary>The property is a number greater than or equal to the value.</summary>
    GreaterOrEqual,

    /// <summary>The property is a number less than the value.</summary>
    LessThan,

    /// <summary>The property is a number less than or equal to the value.</summary>
    LessOrEqual,

    /// <summary>The item carries the property (or the tab) at all; the value is not used.</summary>
    Exists,
}

/// <summary>
/// The pure half of the property searches: what the <c>mode</c> text means, and what a <c>value</c> input turns into. A value that is a
/// list is "any of these": every entry becomes one alternative of ONE search, so the model is walked once however many values there
/// are (a list used to be turned into the text of its .NET type and matched nothing). Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class SearchPlan
{
    private SearchPlan(SearchMode mode, bool wasList, IReadOnlyList<object?> values, IReadOnlyList<string> texts, IReadOnlyList<double> numbers)
    {
        Mode = mode;
        WasList = wasList;
        Values = values;
        Texts = texts;
        Numbers = numbers;
    }

    /// <summary>The comparison.</summary>
    public SearchMode Mode { get; }

    /// <summary>True when the value input held a list (several alternatives, or none).</summary>
    public bool WasList { get; }

    /// <summary>The distinct values to match, in the order given (null entries kept for <see cref="SearchMode.Equal"/>). Empty for <see cref="SearchMode.Exists"/>.</summary>
    public IReadOnlyList<object?> Values { get; }

    /// <summary>The distinct texts, for <see cref="SearchMode.Contains"/> and <see cref="SearchMode.Wildcard"/>.</summary>
    public IReadOnlyList<string> Texts { get; }

    /// <summary>The distinct numbers, for the four comparisons.</summary>
    public IReadOnlyList<double> Numbers { get; }

    /// <summary>True when no item can match: a comparison against an empty list of values.</summary>
    public bool MatchesNothing => Mode != SearchMode.Exists && Values.Count == 0;

    /// <summary>The text of the mode as the dropdown writes it (">=", "equals", ...).</summary>
    public string ModeText => ModeToText(Mode);

    /// <summary>The modes in the order the dropdown offers them.</summary>
    public static IReadOnlyList<string> ModeNames { get; } = new[] { "equals", "contains", "wildcard", ">", ">=", "<", "<=", "exists" };

    /// <summary>Reads the mode text of the node: the dropdown values, and also the words GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual. Empty means equals.</summary>
    /// <param name="mode">The text.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException">The text is not a mode; the message lists the valid ones.</exception>
    public static SearchMode ParseMode(string? mode)
    {
        switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "equals":
            case "equal":
            case "=":
            case "==":
                return SearchMode.Equal;
            case "contains":
                return SearchMode.Contains;
            case "wildcard":
                return SearchMode.Wildcard;
            case ">":
            case "greaterthan":
                return SearchMode.GreaterThan;
            case ">=":
            case "greaterthanorequal":
                return SearchMode.GreaterOrEqual;
            case "<":
            case "lessthan":
                return SearchMode.LessThan;
            case "<=":
            case "lessthanorequal":
                return SearchMode.LessOrEqual;
            case "exists":
            case "has":
                return SearchMode.Exists;
            default:
                throw new ArgumentException(
                    "Unknown mode '" + mode + "'. Use equals, contains, wildcard, >, >=, <, <= or exists.", nameof(mode));
        }
    }

    /// <summary>True for the four numeric comparisons.</summary>
    /// <param name="mode">The mode.</param>
    public static bool IsComparison(SearchMode mode) =>
        mode == SearchMode.GreaterThan || mode == SearchMode.GreaterOrEqual || mode == SearchMode.LessThan || mode == SearchMode.LessOrEqual;

    /// <summary>The dropdown text of a mode.</summary>
    /// <param name="mode">The mode.</param>
    public static string ModeToText(SearchMode mode)
    {
        switch (mode)
        {
            case SearchMode.Contains: return "contains";
            case SearchMode.Wildcard: return "wildcard";
            case SearchMode.GreaterThan: return ">";
            case SearchMode.GreaterOrEqual: return ">=";
            case SearchMode.LessThan: return "<";
            case SearchMode.LessOrEqual: return "<=";
            case SearchMode.Exists: return "exists";
            default: return "equals";
        }
    }

    /// <summary>
    /// Turns the mode and the value input into the alternatives of one search. A list (or any collection except text) is a list of
    /// alternatives, a nested list is flattened, repeats are dropped; anything else is a single value.
    /// </summary>
    /// <param name="modeText">The mode text of the node.</param>
    /// <param name="value">What the value input holds.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">The mode is unknown, or a value does not fit the mode (no text for contains, a word where a number is needed).</exception>
    public static SearchPlan Create(string? modeText, object? value)
    {
        var mode = ParseMode(modeText);
        if (mode == SearchMode.Exists)
        {
            return new SearchPlan(mode, false, Array.Empty<object?>(), Array.Empty<string>(), Array.Empty<double>());
        }

        var wasList = IsList(value);
        var entries = Expand(value);
        var values = new List<object?>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var texts = new List<string>();
        var numbers = new List<double>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var where = wasList ? "value " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + entries.Count.ToString(CultureInfo.InvariantCulture) : "value";
            switch (mode)
            {
                case SearchMode.Contains:
                case SearchMode.Wildcard:
                    var text = AsText(entry, ModeToText(mode), where);
                    if (mode == SearchMode.Wildcard && text.Length == 0)
                    {
                        throw new ArgumentException("No wildcard pattern provided (use * and ?)" + (wasList ? " for " + where : string.Empty) + ".", "value");
                    }

                    if (seen.Add(text))
                    {
                        values.Add(text);
                        texts.Add(text);
                    }

                    break;
                case SearchMode.Equal:
                    if (seen.Add(KeyOf(entry)))
                    {
                        values.Add(entry);
                    }

                    break;
                default:
                    var number = AsNumber(entry, ModeToText(mode), where);
                    if (seen.Add(number.ToString("R", CultureInfo.InvariantCulture)))
                    {
                        values.Add(number);
                        numbers.Add(number);
                    }

                    break;
            }
        }

        return new SearchPlan(mode, wasList, values, texts, numbers);
    }

    /// <summary>True when the value is a collection of alternatives (anything enumerable except text and a dictionary).</summary>
    /// <param name="value">What the value input holds.</param>
    public static bool IsList(object? value) => value is IEnumerable && !(value is string) && !(value is IDictionary);

    /// <summary>The alternatives in a value: its entries when it is a list (nested lists flattened), otherwise the value itself.</summary>
    /// <param name="value">What the value input holds.</param>
    public static IReadOnlyList<object?> Expand(object? value)
    {
        var entries = new List<object?>();
        Collect(value, entries);
        return entries;
    }

    private static void Collect(object? value, List<object?> entries)
    {
        if (IsList(value))
        {
            foreach (var entry in (IEnumerable)value!)
            {
                Collect(entry, entries);
            }

            return;
        }

        entries.Add(value);
    }

    private static string KeyOf(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        return value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    // " (value 2 of 5 is empty)" for an entry of a list, "" for a single value (whose message needs no position).
    private static string Where(string where, string what) =>
        string.Equals(where, "value", StringComparison.Ordinal) ? string.Empty : " (" + where + what + ")";

    /// <summary>A value as the text of a contains or wildcard search.</summary>
    /// <param name="value">The value.</param>
    /// <param name="mode">The mode text, for the message.</param>
    /// <param name="where">Which value it is ("value 2 of 5"), for the message.</param>
    private static string AsText(object? value, string mode, string where)
    {
        if (value == null)
        {
            throw new ArgumentNullException("value", "No search text provided for mode '" + mode + "'" + Where(where, " is empty") + ".");
        }

        return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>A value as the number of a comparison.</summary>
    /// <param name="value">The value.</param>
    /// <param name="mode">The mode text, for the message.</param>
    /// <param name="where">Which value it is ("value 2 of 5"), for the message.</param>
    private static double AsNumber(object? value, string mode, string where)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException("value", "No number provided for mode '" + mode + "'" + Where(where, " is empty") + ".");
            case double d:
                return d;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            case IConvertible convertible when !(value is string) && !(value is bool) && !(value is DateTime) && !(value is char):
                return convertible.ToDouble(CultureInfo.InvariantCulture);
            default:
                throw new ArgumentException("Mode '" + mode + "' compares with a number; '" + value + "'" + Where(where, string.Empty) + " is not one.", "value");
        }
    }
}
