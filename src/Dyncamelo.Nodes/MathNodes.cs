using System;
using System.Globalization;
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

    /// <summary>Absolute value of a number.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The absolute value.</returns>
    [NodeName("Math.Abs")]
    [NodeDescription("Returns the absolute value of a number.")]
    [NodeSearchTags("absolute", "magnitude", "unsigned")]
    public static double Abs(double number)
    {
        return Math.Abs(number);
    }

    /// <summary>Raises a number to a power.</summary>
    /// <param name="base">The base.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>base raised to exponent.</returns>
    [NodeName("Math.Pow")]
    [NodeDescription("Raises the first number to the power of the second.")]
    [NodeSearchTags("power", "exponent", "^", "raise")]
    public static double Pow(double @base, double exponent)
    {
        return Math.Pow(@base, exponent);
    }

    /// <summary>
    /// Square root of a number. Negative inputs raise an error (surfaced as the
    /// node's Error state) instead of silently returning NaN.
    /// </summary>
    /// <param name="number">The number (must be non-negative).</param>
    /// <returns>The square root.</returns>
    [NodeName("Math.Sqrt")]
    [NodeDescription("Returns the square root of a non-negative number.")]
    [NodeSearchTags("root", "radical")]
    public static double Sqrt(double number)
    {
        if (number < 0d)
        {
            throw new ArgumentException(
                "Math.Sqrt requires a non-negative number; got " + number.ToString(CultureInfo.InvariantCulture) + ".",
                nameof(number));
        }

        return Math.Sqrt(number);
    }

    /// <summary>Rounds a number down to the nearest integer.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The largest integer less than or equal to the number.</returns>
    [NodeName("Math.Floor")]
    [NodeDescription("Rounds a number down to the nearest integer.")]
    [NodeSearchTags("round", "down", "truncate")]
    public static double Floor(double number)
    {
        return Math.Floor(number);
    }

    /// <summary>Rounds a number up to the nearest integer.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The smallest integer greater than or equal to the number.</returns>
    [NodeName("Math.Ceiling")]
    [NodeDescription("Rounds a number up to the nearest integer.")]
    [NodeSearchTags("round", "up", "ceil")]
    public static double Ceiling(double number)
    {
        return Math.Ceiling(number);
    }

    /// <summary>
    /// Linearly remaps a value from one range to another, e.g. mapping a
    /// property value in 0–100 onto 0–1 for a color gradient. Values outside
    /// the source range extrapolate (they are not clamped).
    /// </summary>
    /// <param name="value">The value to remap.</param>
    /// <param name="fromLow">Low end of the source range.</param>
    /// <param name="fromHigh">High end of the source range (must differ from fromLow).</param>
    /// <param name="toLow">Low end of the target range.</param>
    /// <param name="toHigh">High end of the target range.</param>
    /// <returns>The remapped value.</returns>
    [NodeName("Math.MapRange")]
    [NodeDescription("Linearly remaps a value from one range to another (values outside the range extrapolate).")]
    [NodeSearchTags("remap", "scale", "normalize", "interpolate", "gradient")]
    public static double MapRange(double value, double fromLow, double fromHigh, double toLow, double toHigh)
    {
        if (fromLow.Equals(fromHigh))
        {
            throw new ArgumentException(
                "Math.MapRange requires fromLow and fromHigh to differ (both are " +
                fromLow.ToString(CultureInfo.InvariantCulture) + ").",
                nameof(fromHigh));
        }

        return toLow + (value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow);
    }

    /// <summary>
    /// A random number in a range. With the default seed (-1) each execution
    /// draws a fresh value; a seed of 0 or greater makes the result
    /// deterministic (the same seed always yields the same number).
    /// </summary>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="max">Exclusive upper bound (must be at least min).</param>
    /// <param name="seed">-1 for non-deterministic; 0 or greater for a repeatable value.</param>
    /// <returns>A random number in [min, max).</returns>
    [NodeName("Math.Random")]
    [NodeDescription("Returns a random number in a range (seed >= 0 makes it deterministic).")]
    [NodeSearchTags("rand", "noise", "seed", "dice")]
    public static double Random(double min = 0d, double max = 1d, int seed = -1)
    {
        if (max < min)
        {
            throw new ArgumentException(
                "Math.Random requires max (" + max.ToString(CultureInfo.InvariantCulture) +
                ") to be at least min (" + min.ToString(CultureInfo.InvariantCulture) + ").",
                nameof(max));
        }

        double sample;
        if (seed >= 0)
        {
            sample = new Random(seed).NextDouble();
        }
        else
        {
            // A shared generator (guarded for replication over lists, which
            // calls this many times in quick succession) avoids the identical
            // values that per-call, time-seeded Random instances would produce.
            lock (SharedRandomLock)
            {
                sample = SharedRandom.NextDouble();
            }
        }

        return min + sample * (max - min);
    }

    private static readonly Random SharedRandom = new Random();
    private static readonly object SharedRandomLock = new object();
}
