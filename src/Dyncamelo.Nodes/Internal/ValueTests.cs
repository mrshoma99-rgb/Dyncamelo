using System;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes.Internal;

/// <summary>
/// The one definition of "does this value pass this test?" shared by <c>Logic.Compare</c> and <c>List.FilterByValue</c>. Numbers
/// compare by value, and text that reads as a number is compared as a number when the other side is one (a property read as
/// "12" against 12). Everything else follows <see cref="ValueComparison"/>.
/// </summary>
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
                return Order(left, right, nodeName) > 0;
            case ">=":
                return Order(left, right, nodeName) >= 0;
            case "<":
                return Order(left, right, nodeName) < 0;
            case "<=":
                return Order(left, right, nodeName) <= 0;
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
                return Wildcard(pattern, ignoreCase).IsMatch(text);
            case "regex":
                return RegexFor(pattern, ignoreCase, nodeName).IsMatch(text);
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

    internal static bool AreEqual(object? a, object? b, bool ignoreCase)
    {
        if (a is string textA && b is string textB)
        {
            return string.Equals(textA, textB, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        if (TryNumbers(a, b, out var x, out var y))
        {
            return x.Equals(y);
        }

        return ValueComparison.AreEqual(a, b);
    }

    internal static int Order(object? a, object? b, string nodeName)
    {
        if (TryNumbers(a, b, out var x, out var y))
        {
            return x.CompareTo(y);
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
            throw new InvalidOperationException(nodeName + ": " + ex.Message, ex);
        }
    }

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
