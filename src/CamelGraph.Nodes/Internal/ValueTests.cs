using System;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using CamelGraph.Core.Types;

namespace CamelGraph.Nodes.Internal;

/// <summary>
/// The one definition of "does this value pass this test?" shared by <c>Logic.Compare</c>, <c>Logic.Switch</c>,
/// <c>Logic.IsBetween</c>, <c>List.FilterByValue</c> and <c>Table.Filter</c>. Numbers compare by value, and text that reads as a
/// number is compared as a number when the other side is one (a property read as "12" against 12). Text that reads as a date is
/// compared as a date when the other side is a date, or when both sides are ISO dates ("2026-10-01"). Text is compared ignoring
/// case unless the caller says otherwise. A missing value (null, or blank text) is "no value": it is never greater or less than
/// anything, so it does not pass <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c> or <c>&lt;=</c>; <c>isNull</c> / <c>isEmpty</c> are the tests that
/// select it. Everything else follows <see cref="ValueComparison"/>.
/// </summary>
/// <summary>A pattern ran past its time limit on one text. A filter node catches this to say which item or row it was.</summary>
internal sealed class PatternTimedOutException : InvalidOperationException
{
    public PatternTimedOutException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

internal static class ValueTests
{
    /// <summary>The operators offered in the dropdowns, in display order.</summary>
    internal static readonly string[] Operators =
    {
        "==", "!=", ">", ">=", "<", "<=",
        "contains", "!contains", "startsWith", "endsWith", "matches", "regex",
        "isNull", "notNull", "isEmpty", "notEmpty",
    };

    /// <summary>Canonical spelling of an operator, accepting the word forms (equals, greater, …).</summary>
    internal static string Normalize(string? op, string nodeName)
    {
        var key = (op ?? string.Empty).Trim();
        switch (key.ToLowerInvariant())
        {
            case "==":
            case "=":
            case "equals":
            case "equal":
                return "==";
            case "!=":
            case "<>":
            case "notequals":
            case "notequal":
                return "!=";
            case ">":
            case "greater":
            case "greaterthan":
                return ">";
            case ">=":
            case "greaterorequal":
            case "greaterthanorequal":
                return ">=";
            case "<":
            case "less":
            case "lessthan":
                return "<";
            case "<=":
            case "lessorequal":
            case "lessthanorequal":
                return "<=";
            case "contains":
                return "contains";
            case "!contains":
            case "notcontains":
            case "doesnotcontain":
                return "!contains";
            case "startswith":
                return "startsWith";
            case "endswith":
                return "endsWith";
            case "matches":
            case "wildcard":
            case "like":
                return "matches";
            case "regex":
            case "regexp":
                return "regex";
            case "isnull":
                return "isNull";
            case "notnull":
            case "isnotnull":
                return "notNull";
            case "isempty":
                return "isEmpty";
            case "notempty":
            case "isnotempty":
                return "notEmpty";
            default:
                throw new ArgumentException(
                    nodeName + ": '" + op + "' is not a known test. Use one of: " + string.Join(", ", Operators) + ".", "operator");
        }
    }

    /// <summary>Applies a (normalized) operator to a left and right value.</summary>
    internal static bool Test(string op, object? left, object? right, bool ignoreCase, string nodeName)
    {
        switch (op)
        {
            case "isNull":
                return left == null;
            case "notNull":
                return left != null;
            case "isEmpty":
                return IsEmpty(left);
            case "notEmpty":
                return !IsEmpty(left);
            case "==":
                return AreEqual(left, right, ignoreCase);
            case "!=":
                return !AreEqual(left, right, ignoreCase);
            case ">":
                return TryOrder(left, right, nodeName, out var greater) && greater > 0;
            case ">=":
                return TryOrder(left, right, nodeName, out var atLeast) && atLeast >= 0;
            case "<":
                return TryOrder(left, right, nodeName, out var smaller) && smaller < 0;
            case "<=":
                return TryOrder(left, right, nodeName, out var atMost) && atMost <= 0;
        }

        // The remaining tests work on text.
        if (left == null)
        {
            return op == "!contains";
        }

        var text = TypeCoercion.FormatValue(left);
        var pattern = right == null ? string.Empty : TypeCoercion.FormatValue(right);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        switch (op)
        {
            case "contains":
                return text.IndexOf(pattern, comparison) >= 0;
            case "!contains":
                return text.IndexOf(pattern, comparison) < 0;
            case "startsWith":
                return text.StartsWith(pattern, comparison);
            case "endsWith":
                return text.EndsWith(pattern, comparison);
            case "matches":
                return IsMatch(Wildcard(pattern, ignoreCase), text, nodeName);
            case "regex":
                return IsMatch(RegexFor(pattern, ignoreCase, nodeName), text, nodeName);
            default:
                throw new ArgumentException(nodeName + ": '" + op + "' is not a known test.", "operator");
        }
    }

    internal static bool IsEmpty(object? value)
    {
        switch (value)
        {
            case null:
                return true;
            case string text:
                return text.Length == 0;
            case IDictionary dictionary:
                return dictionary.Count == 0;
            case ICollection collection:
                return collection.Count == 0;
            default:
                return false;
        }
    }

    /// <summary>
    /// The one meaning of "equal" for the Logic nodes: text ignores case when asked (the Logic nodes always ask), numbers
    /// compare by value, text that reads as a number equals that number, text that reads as a date equals that date, and two
    /// lists are equal when they have the same length and every pair of items is equal by these same rules.
    /// </summary>
    internal static bool AreEqual(object? a, object? b, bool ignoreCase)
    {
        if (a is string textA && b is string textB)
        {
            if (string.Equals(textA, textB, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return true;
            }

            return TryDates(a, b, out var firstDate, out var secondDate) && firstDate.Equals(secondDate);
        }

        if (TryNumbers(a, b, out var x, out var y))
        {
            return x.Equals(y);
        }

        if (TryDates(a, b, out var dateA, out var dateB))
        {
            return dateA.Equals(dateB);
        }

        if (a is IList listA && b is IList listB && !(a is string) && !(b is string))
        {
            if (listA.Count != listB.Count)
            {
                return false;
            }

            for (var i = 0; i < listA.Count; i++)
            {
                if (!AreEqual(listA[i], listB[i], ignoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        return ValueComparison.AreEqual(a, b);
    }

    /// <summary>
    /// Orders two values: numbers numerically (text that reads as a number counts as one next to a number), dates
    /// chronologically (text that reads as a date counts as one next to a date, or when both are ISO dates), other text
    /// ignoring case. Values that cannot be ordered throw a sentence that says which kinds were met.
    /// </summary>
    internal static int Order(object? a, object? b, string nodeName)
    {
        if (TryNumbers(a, b, out var x, out var y))
        {
            return x.CompareTo(y);
        }

        if (TryDates(a, b, out var dateA, out var dateB))
        {
            return dateA.CompareTo(dateB);
        }

        if (a is string textA && b is string textB)
        {
            return string.Compare(textA, textB, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return ValueComparison.Compare(a, b);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                nodeName + ": cannot compare " + KindName(a) + " with " + KindName(b) + ". Compare numbers, text or dates." +
                (a is IList || b is IList
                    ? " To test every item of a list, set List Levels to @L1 on the list's input."
                    : string.Empty),
                ex);
        }
    }

    /// <summary>
    /// The ordering used by the greater / less tests: false when either side is "no value" (null or blank text), or when a
    /// number is set against text that is not a number. Those pairs simply do not pass, so one empty cell cannot stop a whole
    /// filter. Values of kinds that can never be ordered (a date against yes/no) still throw.
    /// </summary>
    internal static bool TryOrder(object? a, object? b, string nodeName, out int order)
    {
        order = 0;
        if (IsNoValue(a) || IsNoValue(b))
        {
            return false;
        }

        if (!TryNumbers(a, b, out _, out _) &&
            ((ValueComparison.IsNumeric(a!) && b is string) || (ValueComparison.IsNumeric(b!) && a is string)))
        {
            return false;
        }

        order = Order(a, b, nodeName);
        return true;
    }

    /// <summary>
    /// True when both sides have a value but cannot be ordered: a number set against text that is not a number. <see cref="TryOrder"/>
    /// lets such a pair fail the test silently; a filter that wants to tell the user how many items it left out asks this first.
    /// </summary>
    internal static bool CannotBeOrdered(object? a, object? b)
    {
        if (IsNoValue(a) || IsNoValue(b))
        {
            return false;
        }

        return !TryNumbers(a, b, out _, out _) &&
               ((ValueComparison.IsNumeric(a!) && b is string) || (ValueComparison.IsNumeric(b!) && a is string));
    }

    /// <summary>True for null and for text that is empty or only white space: a missing value, which is never greater or less.</summary>
    internal static bool IsNoValue(object? value)
    {
        return value == null || (value is string text && string.IsNullOrWhiteSpace(text));
    }

    // A date written the way the file formats write it: 2026-10-01, 2026-10-01 14:30, 2026-10-01T14:30:00Z.
    private static readonly Regex IsoDateText = new Regex(
        @"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?)?(Z|[+-]\d{2}:?\d{2})?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Dates: a DateTime, or text that reads as a date when the other side is a date, or when both sides are ISO dates.
    private static bool TryDates(object? a, object? b, out DateTime first, out DateTime second)
    {
        first = default;
        second = default;
        if (a == null || b == null)
        {
            return false;
        }

        var aDate = a is DateTime;
        var bDate = b is DateTime;
        if (aDate && bDate)
        {
            first = (DateTime)a;
            second = (DateTime)b;
            return true;
        }

        if (aDate && b is string textB)
        {
            first = (DateTime)a;
            return TryParseDate(textB, requireIso: false, out second);
        }

        if (bDate && a is string textA)
        {
            second = (DateTime)b;
            return TryParseDate(textA, requireIso: false, out first);
        }

        return a is string isoA && b is string isoB &&
               TryParseDate(isoA, requireIso: true, out first) && TryParseDate(isoB, requireIso: true, out second);
    }

    // Text next to a real date must still look like a date: it needs a four-digit year ("9.5" and "12:30" parse as dates in
    // .NET, but nobody means them). Two ISO texts are dates only when both are written the ISO way.
    private static bool TryParseDate(string text, bool requireIso, out DateTime date)
    {
        date = default;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || (requireIso ? !IsoDateText.IsMatch(trimmed) : !HasYear.IsMatch(trimmed)))
        {
            return false;
        }

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static readonly Regex HasYear = new Regex(@"(^|\D)\d{4}(\D|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Numbers, or text that reads as a number when the other side is a number.
    private static bool TryNumbers(object? a, object? b, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (a == null || b == null)
        {
            return false;
        }

        var aNumber = ValueComparison.IsNumeric(a);
        var bNumber = ValueComparison.IsNumeric(b);
        if (aNumber && bNumber)
        {
            x = ValueComparison.ToDouble(a);
            y = ValueComparison.ToDouble(b);
            return true;
        }

        if (aNumber && b is string textB && double.TryParse(textB.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
        {
            x = ValueComparison.ToDouble(a);
            return true;
        }

        if (bNumber && a is string textA && double.TryParse(textA.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x))
        {
            y = ValueComparison.ToDouble(b);
            return true;
        }

        return false;
    }

    // "a list", "a number", "text", "a date", "yes/no", "a dictionary", "nothing" for the messages.
    private static string KindName(object? value)
    {
        switch (value)
        {
            case null:
                return "an empty value";
            case string _:
                return "text";
            case bool _:
                return "a yes/no value";
            case DateTime _:
                return "a date";
            case IDictionary _:
                return "a dictionary";
            case IList _:
                return "a list";
        }

        return ValueComparison.IsNumeric(value) ? "a number" : "a value of another kind (" + value.GetType().Name + ")";
    }

    // The same two-second limit as the String.Regex nodes, with their wording.
    private static bool IsMatch(Regex regex, string text, string nodeName)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new PatternTimedOutException(
                nodeName + ": the pattern took longer than 2 seconds on one of the texts and was stopped. Simplify the pattern.", ex);
        }
    }

    private static Regex Wildcard(string pattern, bool ignoreCase)
    {
        var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return RegexCache.Get(expression, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    }

    private static Regex RegexFor(string pattern, bool ignoreCase, string nodeName)
    {
        try
        {
            return RegexCache.Get(pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(nodeName + ": the pattern '" + pattern + "' is not a valid regular expression: " + ex.Message, "value");
        }
    }
}
