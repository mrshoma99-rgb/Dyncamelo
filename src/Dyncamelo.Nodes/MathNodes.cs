using System;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Arithmetic nodes. All operands are doubles; the engine's coercion converts
/// other numeric types (and numeric strings) on the way in, and replication
/// maps these nodes over lists automatically.
/// </summary>
[NodeCategory("Math")]
public static class MathNodes
{
    /// <summary>Adds two numbers.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>The sum a + b.</returns>
    [NodeName("Add")]
    [NodeDescription("Adds two numbers.")]
    [NodeSearchTags("+", "plus", "sum", "addition")]
    public static double Add(double a, double b)
    {
        return a + b;
    }

    /// <summary>Subtracts one number from another.</summary>
    /// <param name="a">Value to subtract from.</param>
    /// <param name="b">Value to subtract.</param>
    /// <returns>The difference a - b.</returns>
    [NodeName("Subtract")]
    [NodeDescription("Subtracts the second number from the first.")]
    [NodeSearchTags("-", "minus", "difference", "subtraction")]
    public static double Subtract(double a, double b)
    {
        return a - b;
    }

    /// <summary>Multiplies two numbers.</summary>
    /// <param name="a">First factor.</param>
    /// <param name="b">Second factor.</param>
    /// <returns>The product a * b.</returns>
    [NodeName("Multiply")]
    [NodeDescription("Multiplies two numbers.")]
    [NodeSearchTags("*", "times", "product", "multiplication")]
    public static double Multiply(double a, double b)
    {
        return a * b;
    }

    /// <summary>
    /// Divides one number by another using IEEE 754 semantics: dividing by zero
    /// yields Infinity (or NaN for 0 / 0) rather than throwing.
    /// </summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor.</param>
    /// <returns>The quotient a / b.</returns>
    [NodeName("Divide")]
    [NodeDescription("Divides the first number by the second.")]
    [NodeSearchTags("/", "quotient", "division")]
    public static double Divide(double a, double b)
    {
        return a / b;
    }

    /// <summary>
    /// Remainder of a division (C# <c>%</c> semantics: the result carries the
    /// sign of the dividend; a zero divisor yields NaN).
    /// </summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor.</param>
    /// <returns>The remainder of a / b.</returns>
    [NodeName("Modulo")]
    [NodeDescription("Returns the remainder of dividing the first number by the second.")]
    [NodeSearchTags("%", "mod", "remainder")]
    public static double Modulo(double a, double b)
    {
        return a % b;
    }

    /// <summary>
    /// Rounds a number to a given number of decimal digits. Midpoints round
    /// away from zero (0.5 → 1, -0.5 → -1), matching everyday expectations.
    /// </summary>
    /// <param name="number">The number to round.</param>
    /// <param name="digits">Number of decimal digits to keep (0–15).</param>
    /// <returns>The rounded number.</returns>
    [NodeName("Math.Round")]
    [NodeDescription("Rounds a number to the given number of decimal digits (midpoints round away from zero).")]
    [NodeSearchTags("round", "nearest")]
    public static double Round(double number, int digits = 0)
    {
        return Math.Round(number, digits, MidpointRounding.AwayFromZero);
    }

    /// <summary>Returns the smaller of two numbers.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>The minimum of a and b.</returns>
    [NodeName("Math.Min")]
    [NodeDescription("Returns the smaller of two numbers.")]
    [NodeSearchTags("minimum", "smallest")]
    public static double Min(double a, double b)
    {
        return Math.Min(a, b);
    }

    /// <summary>Returns the larger of two numbers.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>The maximum of a and b.</returns>
    [NodeName("Math.Max")]
    [NodeDescription("Returns the larger of two numbers.")]
    [NodeSearchTags("maximum", "largest")]
    public static double Max(double a, double b)
    {
        return Math.Max(a, b);
    }
}
