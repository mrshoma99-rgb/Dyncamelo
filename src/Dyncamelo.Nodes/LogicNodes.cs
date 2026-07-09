using System;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

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
    [NodeDescription("Returns one of two values depending on a boolean condition.")]
    [NodeSearchTags("condition", "branch", "ternary", "switch")]
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
    [NodeSearchTags("&&", "conjunction", "boolean")]
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
    [NodeSearchTags("||", "disjunction", "boolean")]
    public static bool Or(bool a, bool b)
    {
        return a || b;
    }

    /// <summary>Logical negation of a boolean.</summary>
    /// <param name="value">The value to negate.</param>
    /// <returns>True when the input is false.</returns>
    [NodeName("Not")]
    [NodeDescription("Inverts a boolean value.")]
    [NodeSearchTags("!", "negate", "invert", "boolean")]
    public static bool Not(bool value)
    {
        return !value;
    }

    /// <summary>
    /// Value equality with numeric tolerance for boxed types: any two numbers
    /// compare by numeric value (2 equals 2.0), strings compare ordinally and
    /// everything else uses <see cref="object.Equals(object)"/>.
    /// </summary>
    /// <param name="a">First value.</param>
    /// <param name="b">Second value.</param>
    /// <returns>True when the values are considered equal.</returns>
    [NodeName("Equals")]
    [NodeDescription("Tests whether two values are equal (numbers compare by value regardless of numeric type).")]
    [NodeSearchTags("==", "equal", "same", "compare")]
    public static bool EqualTo(object? a, object? b)
    {
        return ValueComparison.AreEqual(a, b);
    }

    /// <summary>Tests whether the first number is greater than the second.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when a &gt; b.</returns>
    [NodeName("GreaterThan")]
    [NodeDescription("Returns true when the first number is greater than the second.")]
    [NodeSearchTags(">", "compare", "larger")]
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
    [NodeSearchTags("<", "compare", "smaller")]
    public static bool LessThan(double a, double b)
    {
        return a < b;
    }
}

/// <summary>
/// Shared value-comparison helpers used by Logic and List nodes: numbers of any
/// boxed CLR type compare by numeric value, strings ordinally, everything else
/// by <see cref="object.Equals(object)"/>.
/// </summary>
[IsVisibleInLibrary(false)]
internal static class ValueComparison
{
    /// <summary>Tests two values for node-level equality.</summary>
    internal static bool AreEqual(object? a, object? b)
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

        return a.Equals(b);
    }

    /// <summary>Hash code consistent with <see cref="AreEqual"/> (numbers hash by double value).</summary>
    internal static int GetValueHashCode(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        return IsNumeric(value) ? ToDouble(value).GetHashCode() : value.GetHashCode();
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
