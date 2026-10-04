using System;
using System.Collections;
using System.Globalization;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// Boolean and comparison nodes. Boolean inputs replicate over lists;
/// the branch values of <see cref="If"/> are untyped and pass through whole.
/// </summary>
[NodeCategory("Logic")]
public static class LogicNodes
{
    /// <summary>
    /// Selects one of two values based on a condition. The branch inputs are
    /// untyped, so entire lists pass through unsplit; a list of booleans on
    /// <paramref name="test"/> replicates the choice per element.
    /// </summary>
    /// <param name="test">The condition to evaluate.</param>
    /// <param name="trueValue">Value returned when the condition is true.</param>
    /// <param name="falseValue">Value returned when the condition is false.</param>
    /// <returns>Either <paramref name="trueValue"/> or <paramref name="falseValue"/>.</returns>
    [NodeName("If")]
    [NodeDescription(
        "Returns one of two values depending on a boolean condition. Both branches are always computed, so a node wired into " +
        "the unused branch still runs: to skip nodes (a modifying node, a slow search) use Flow.When instead. A list of " +
        "booleans on test gives a list of choices.")]
    [NodeSearchTags("condition", "branch", "ternary", "switch", "flow", "when", "Logic.If")]
    public static object? If(bool test, object? trueValue, object? falseValue)
    {
        return test ? trueValue : falseValue;
    }

    /// <summary>Logical AND of two booleans.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when both operands are true.</returns>
    [NodeName("And")]
    [NodeDescription("Returns true only when both inputs are true.")]
    [NodeSearchTags("&&", "conjunction", "boolean", "Logic.And")]
    public static bool And(bool a, bool b)
    {
        return a && b;
    }

    /// <summary>Logical OR of two booleans.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when at least one operand is true.</returns>
    [NodeName("Or")]
    [NodeDescription("Returns true when at least one input is true.")]
    [NodeSearchTags("||", "disjunction", "boolean", "Logic.Or")]
    public static bool Or(bool a, bool b)
    {
        return a || b;
    }

    /// <summary>Whether a value is null.</summary>
    /// <param name="value">The value to test. A list is tested as a whole (a list is never null). To test each element of a list, set this input's List Levels to @L1: an empty element then gives true.</param>
    /// <returns>True when the value is null.</returns>
    [NodeName("IsNull")]
    [NodeDescription(
        "True when the value is null (nothing came out). Wired to a list it tests the LIST, which is never null; set the " +
        "input's List Levels to @L1 to test every element instead, and each empty element gives true. That gives the bool " +
        "mask for List.FilterByBoolMask (pair with Not to keep the non-nulls). Text that is empty or blank is not null: " +
        "use IsNullOrEmpty or String.IsBlank for that.")]
    [NodeSearchTags("null", "is", "check", "missing", "empty", "mask", "test", "Logic.IsNull")]
    [return: NodeName("isNull")]
    public static bool IsNull([AcceptsNull] object? value)
    {
        return value == null;
    }

    /// <summary>Whether a value is null or empty.</summary>
    /// <param name="value">The value to test: null, an empty string, an empty list and an empty dictionary all count as empty. To test each element of a list, set this input's List Levels to @L1: an empty element then gives true.</param>
    /// <returns>True when the value is null or empty.</returns>
    [NodeName("IsNullOrEmpty")]
    [NodeDescription(
        "True when the value is null, an empty string, an empty list or an empty dictionary — the guard " +
        "for \"did anything come out?\" checks before If branches. NOTE: wired to a list directly it " +
        "tests the LIST (empty or not); set the input's List Levels to @L1 to test each element instead, and each empty " +
        "element gives true. Text of only spaces is not empty here: String.IsBlank counts it as blank.")]
    [NodeSearchTags("null", "empty", "is", "check", "missing", "guard", "test", "Logic.IsNullOrEmpty")]
    [return: NodeName("isEmpty")]
    public static bool IsNullOrEmpty([AcceptsNull] object? value)
    {
        switch (value)
        {
            case null:
                return true;
            case string text:
                return text.Length == 0;
            case System.Collections.IDictionary dictionary:
                return dictionary.Count == 0;
            case System.Collections.ICollection collection:
                return collection.Count == 0;
            default:
                return false;
        }
    }

    /// <summary>Logical negation of a boolean.</summary>
    /// <param name="value">The value to negate.</param>
    /// <returns>True when the input is false.</returns>
    [NodeName("Not")]
    [NodeDescription("Inverts a boolean value.")]
    [NodeSearchTags("!", "negate", "invert", "boolean", "Logic.Not")]
    public static bool Not(bool value)
    {
        return !value;
    }

    /// <summary>
    /// Value equality, the same rules as <c>Logic.Compare</c> with <c>==</c>: numbers compare by value (2 equals 2.0), text
    /// ignores upper and lower case, text that reads as a number equals that number, text that reads as a date equals that
    /// date, and two lists are equal when every pair of items is.
    /// </summary>
    /// <param name="a">First value. A list is compared as one value; set List Levels to @L1 to compare every element instead.</param>
    /// <param name="b">Second value.</param>
    /// <returns>True when the values are considered equal.</returns>
    [NodeName("Equals")]
    [NodeDescription(
        "Tests whether two values are equal, the same way as Logic.Compare with ==: numbers compare by value (2 equals 2.0), " +
        "text ignores upper and lower case, text that reads as a number equals that number (\"5\" equals 5), text that reads " +
        "as a date equals that date, and two lists are equal when they have the same items in the same order. A list is " +
        "compared as ONE value; to test every element of a list set List Levels to @L1 on a (an empty element is equal only to " +
        "another empty one). For a case-sensitive text test use Logic.Compare with ignoreCase off, and for numbers within a " +
        "tolerance use Math.IsClose.")]
    [NodeSearchTags("==", "equal", "same", "compare", "Logic.Equals")]
    public static bool EqualTo([AcceptsNull] object? a, [AcceptsNull] object? b)
    {
        return ValueTests.AreEqual(a, b, ignoreCase: true);
    }

    /// <summary>Tests whether the first number is greater than the second.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when a &gt; b.</returns>
    [NodeName("GreaterThan")]
    [NodeDescription("Returns true when the first number is greater than the second.")]
    [NodeSearchTags(">", "compare", "larger", "Logic.GreaterThan")]
    public static bool GreaterThan(double a, double b)
    {
        return a > b;
    }

    /// <summary>Tests whether the first number is less than the second.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when a &lt; b.</returns>
    [NodeName("LessThan")]
    [NodeDescription("Returns true when the first number is less than the second.")]
    [NodeSearchTags("<", "compare", "smaller", "Logic.LessThan")]
    public static bool LessThan(double a, double b)
    {
        return a < b;
    }

    /// <summary>Tests whether the first number is greater than or equal to the second.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when a &gt;= b.</returns>
    [NodeName("GreaterThanOrEqual")]
    [NodeDescription("Returns true when the first number is greater than or equal to the second.")]
    [NodeSearchTags(">=", "compare", "atleast", "Logic.GreaterThanOrEqual")]
    public static bool GreaterThanOrEqual(double a, double b)
    {
        return a >= b;
    }

    /// <summary>Tests whether the first number is less than or equal to the second.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when a &lt;= b.</returns>
    [NodeName("LessThanOrEqual")]
    [NodeDescription("Returns true when the first number is less than or equal to the second.")]
    [NodeSearchTags("<=", "compare", "atmost", "Logic.LessThanOrEqual")]
    public static bool LessThanOrEqual(double a, double b)
    {
        return a <= b;
    }
}

/// <summary>
/// Shared value-comparison helpers used by Logic and List nodes: numbers of any
/// boxed CLR type compare by numeric value, strings ordinally, lists element by
/// element and dictionaries entry by entry (key order does not matter), everything
/// else by <see cref="object.Equals(object)"/>.
/// </summary>
[IsVisibleInLibrary(false)]
internal static class ValueComparison
{
    // Lists inside lists inside lists... are compared recursively; a structure nested deeper than this (only a circular one can be)
    // is not compared further, because a stack overflow would take the whole host down.
    private const int MaxDepth = 64;

    /// <summary>
    /// Tests two values for node-level equality. Numbers of any numeric type are equal when they hold the same value, text is
    /// compared ordinally (case matters), two lists are equal when they have the same items in the same order, two dictionaries
    /// when they have the same keys with equal values (the order of the keys does not matter); the items and values are compared
    /// by the same rules, at every level.
    /// </summary>
    internal static bool AreEqual(object? a, object? b) => AreEqual(a, b, 0);

    /// <summary>Hash code consistent with <see cref="AreEqual"/> (numbers hash by double value, lists and dictionaries by content).</summary>
    internal static int GetValueHashCode(object? value) => GetValueHashCode(value, 0);

    private static bool AreEqual(object? a, object? b, int depth)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a).Equals(ToDouble(b));
        }

        if (a is IDictionary || b is IDictionary)
        {
            return a is IDictionary dictionaryA && b is IDictionary dictionaryB && depth < MaxDepth && DictionariesEqual(dictionaryA, dictionaryB, depth);
        }

        if (a is IList listA && b is IList listB)
        {
            return depth < MaxDepth && ListsEqual(listA, listB, depth);
        }

        return a.Equals(b);
    }

    private static bool ListsEqual(IList a, IList b, int depth)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!AreEqual(a[i], b[i], depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual(IDictionary a, IDictionary b, int depth)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (DictionaryEntry entry in a)
        {
            // Keys are matched the way the Dictionary nodes match them: a string key by the dictionary's own rules, any other
            // key by its text.
            if (!DictionaryExtraNodes.TryGetValue(b, TypeCoercion.FormatValue(entry.Key), out var other) ||
                !AreEqual(entry.Value, other, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetValueHashCode(object? value, int depth)
    {
        if (value == null)
        {
            return 0;
        }

        if (IsNumeric(value))
        {
            return ToDouble(value).GetHashCode();
        }

        if (depth >= MaxDepth)
        {
            return value is IList || value is IDictionary ? 1 : value.GetHashCode();
        }

        if (value is IDictionary dictionary)
        {
            // Entries are added, not chained, so the order of the keys does not change the hash.
            var sum = dictionary.Count;
            unchecked
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    sum += StringComparer.Ordinal.GetHashCode(TypeCoercion.FormatValue(entry.Key)) * 397 ^ GetValueHashCode(entry.Value, depth + 1);
                }
            }

            return sum;
        }

        if (value is IList list)
        {
            var hash = 17 + list.Count;
            unchecked
            {
                foreach (var item in list)
                {
                    hash = hash * 31 + GetValueHashCode(item, depth + 1);
                }
            }

            return hash;
        }

        return value.GetHashCode();
    }

    /// <summary>
    /// Orders two values: numbers numerically, strings ordinally, booleans
    /// false-before-true, otherwise via <see cref="IComparable"/> when both
    /// values share a type. Incomparable pairs throw a descriptive exception.
    /// </summary>
    internal static int Compare(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a == null)
        {
            return -1;
        }

        if (b == null)
        {
            return 1;
        }

        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a).CompareTo(ToDouble(b));
        }

        if (a is string textA && b is string textB)
        {
            return string.CompareOrdinal(textA, textB);
        }

        if (a.GetType() == b.GetType() && a is IComparable comparable)
        {
            return comparable.CompareTo(b);
        }

        throw new InvalidOperationException(
            "Cannot compare values of type '" + a.GetType().Name + "' and '" + b.GetType().Name + "'.");
    }

    /// <summary>True for boxed CLR numeric primitives and decimal.</summary>
    internal static bool IsNumeric(object value)
    {
        return value is double || value is float || value is decimal ||
               value is int || value is long || value is short || value is sbyte ||
               value is uint || value is ulong || value is ushort || value is byte;
    }

    /// <summary>Converts any boxed numeric to double (invariant culture).</summary>
    internal static double ToDouble(object value)
    {
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
}
