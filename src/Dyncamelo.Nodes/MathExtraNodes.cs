using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes.Internal;

namespace Dyncamelo.Nodes;

/// <summary>
/// More arithmetic: trigonometry, logarithms, clamping, percentages, sequences and a one-line formula node. Like the
/// basics in <see cref="MathNodes"/>, every operand is a double and a list on any input replicates the node over it.
/// </summary>
[NodeCategory("Math")]
public static class MathExtraNodes
{
    private const double MaxSequenceLength = 1_000_000;

    /// <summary>Sine of an angle.</summary>
    /// <param name="angle">The angle.</param>
    /// <param name="unit">The unit of the angle: degrees (default) or radians.</param>
    /// <returns>The sine, between -1 and 1.</returns>
    [NodeName("Math.Sin")]
    [return: NodeName("value")]
    [NodeDescription("Sine of an angle (degrees by default).")]
    [NodeSearchTags("trigonometry", "sine", "angle", "wave")]
    public static double Sin(double angle, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        return Math.Sin(ToRadians(angle, unit, "Math.Sin"));
    }

    /// <summary>Cosine of an angle.</summary>
    /// <param name="angle">The angle.</param>
    /// <param name="unit">The unit of the angle: degrees (default) or radians.</param>
    /// <returns>The cosine, between -1 and 1.</returns>
    [NodeName("Math.Cos")]
    [return: NodeName("value")]
    [NodeDescription("Cosine of an angle (degrees by default).")]
    [NodeSearchTags("trigonometry", "cosine", "angle")]
    public static double Cos(double angle, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        return Math.Cos(ToRadians(angle, unit, "Math.Cos"));
    }

    /// <summary>Tangent of an angle.</summary>
    /// <param name="angle">The angle.</param>
    /// <param name="unit">The unit of the angle: degrees (default) or radians.</param>
    /// <returns>The tangent.</returns>
    [NodeName("Math.Tan")]
    [return: NodeName("value")]
    [NodeDescription("Tangent of an angle (degrees by default); slope = tan(angle).")]
    [NodeSearchTags("trigonometry", "tangent", "angle", "slope")]
    public static double Tan(double angle, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        return Math.Tan(ToRadians(angle, unit, "Math.Tan"));
    }

    /// <summary>Angle whose sine is the given value.</summary>
    /// <param name="value">A number from -1 to 1.</param>
    /// <param name="unit">The unit of the result: degrees (default) or radians.</param>
    /// <returns>The angle.</returns>
    [NodeName("Math.Asin")]
    [return: NodeName("angle")]
    [NodeDescription("Inverse sine: the angle whose sine is the value (result in degrees by default).")]
    [NodeSearchTags("trigonometry", "arcsine", "inverse")]
    public static double Asin(double value, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        RequireUnitRange(value, "Math.Asin");
        return FromRadians(Math.Asin(value), unit, "Math.Asin");
    }

    /// <summary>Angle whose cosine is the given value.</summary>
    /// <param name="value">A number from -1 to 1.</param>
    /// <param name="unit">The unit of the result: degrees (default) or radians.</param>
    /// <returns>The angle.</returns>
    [NodeName("Math.Acos")]
    [return: NodeName("angle")]
    [NodeDescription("Inverse cosine: the angle whose cosine is the value (result in degrees by default).")]
    [NodeSearchTags("trigonometry", "arccosine", "inverse")]
    public static double Acos(double value, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        RequireUnitRange(value, "Math.Acos");
        return FromRadians(Math.Acos(value), unit, "Math.Acos");
    }

    /// <summary>Angle whose tangent is the given value.</summary>
    /// <param name="value">Any number (rise over run).</param>
    /// <param name="unit">The unit of the result: degrees (default) or radians.</param>
    /// <returns>The angle.</returns>
    [NodeName("Math.Atan")]
    [return: NodeName("angle")]
    [NodeDescription("Inverse tangent: the angle for a slope (result in degrees by default).")]
    [NodeSearchTags("trigonometry", "arctangent", "inverse", "slope")]
    public static double Atan(double value, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        return FromRadians(Math.Atan(value), unit, "Math.Atan");
    }

    /// <summary>Angle of the vector (x, y) measured from the X axis, in the right quadrant.</summary>
    /// <param name="y">The Y (rise) component.</param>
    /// <param name="x">The X (run) component.</param>
    /// <param name="unit">The unit of the result: degrees (default) or radians.</param>
    /// <returns>The angle from -180 to 180 degrees.</returns>
    [NodeName("Math.Atan2")]
    [return: NodeName("angle")]
    [NodeDescription("Angle of the direction (x, y) from the X axis, from -180 to 180 degrees — the heading between two points.")]
    [NodeSearchTags("trigonometry", "heading", "bearing", "direction", "angle")]
    public static double Atan2(double y, double x, [NodeChoices("degrees", "radians")] string unit = "degrees")
    {
        return FromRadians(Math.Atan2(y, x), unit, "Math.Atan2");
    }

    /// <summary>Converts degrees to radians.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The angle in radians.</returns>
    [NodeName("Math.Radians")]
    [return: NodeName("radians")]
    [NodeDescription("Converts degrees to radians.")]
    [NodeSearchTags("convert", "angle", "degrees")]
    public static double Radians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }

    /// <summary>Converts radians to degrees.</summary>
    /// <param name="radians">The angle in radians.</param>
    /// <returns>The angle in degrees.</returns>
    [NodeName("Math.Degrees")]
    [return: NodeName("degrees")]
    [NodeDescription("Converts radians to degrees.")]
    [NodeSearchTags("convert", "angle", "radians")]
    public static double Degrees(double radians)
    {
        return radians * 180d / Math.PI;
    }

    /// <summary>The number pi.</summary>
    /// <returns>3.14159…</returns>
    [NodeName("Math.Pi")]
    [return: NodeName("pi")]
    [NodeDescription("The constant pi (3.14159…).")]
    [NodeSearchTags("constant", "3.14", "circle")]
    public static double Pi()
    {
        return Math.PI;
    }

    /// <summary>Natural logarithm.</summary>
    /// <param name="value">A positive number.</param>
    /// <returns>The natural logarithm (base e).</returns>
    [NodeName("Math.Ln")]
    [return: NodeName("value")]
    [NodeDescription("Natural logarithm (base e) of a positive number.")]
    [NodeSearchTags("logarithm", "log", "ln", "natural")]
    public static double Ln(double value)
    {
        RequirePositive(value, "Math.Ln");
        return Math.Log(value);
    }

    /// <summary>Logarithm to a chosen base.</summary>
    /// <param name="value">A positive number.</param>
    /// <param name="logBase">The base (default 10); positive and not 1.</param>
    /// <returns>The logarithm.</returns>
    [NodeName("Math.Log")]
    [return: NodeName("value")]
    [NodeDescription("Logarithm of a positive number to a base (10 by default).")]
    [NodeSearchTags("logarithm", "log10", "log2", "base")]
    public static double Log(double value, double logBase = 10d)
    {
        RequirePositive(value, "Math.Log");
        if (logBase <= 0d || logBase == 1d || double.IsNaN(logBase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(logBase), "Math.Log needs a base above 0 and different from 1; got " + logBase.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return Math.Log(value, logBase);
    }

    /// <summary>e raised to a power.</summary>
    /// <param name="power">The exponent.</param>
    /// <returns>e to the power.</returns>
    [NodeName("Math.Exp")]
    [return: NodeName("value")]
    [NodeDescription("e raised to a power (the inverse of the natural logarithm).")]
    [NodeSearchTags("exponential", "e", "power")]
    public static double Exp(double power)
    {
        return Math.Exp(power);
    }

    /// <summary>Sign of a number.</summary>
    /// <param name="number">The number.</param>
    /// <returns>-1, 0 or 1.</returns>
    [NodeName("Math.Sign")]
    [return: NodeName("sign")]
    [NodeDescription("-1 for a negative number, 0 for zero, 1 for a positive number.")]
    [NodeSearchTags("positive", "negative", "direction")]
    public static int Sign(double number)
    {
        return double.IsNaN(number) ? 0 : Math.Sign(number);
    }

    /// <summary>Flips the sign of a number.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The number multiplied by -1.</returns>
    [NodeName("Math.Negate")]
    [return: NodeName("value")]
    [NodeDescription("Flips the sign of a number (5 becomes -5).")]
    [NodeSearchTags("minus", "opposite", "invert", "flip")]
    public static double Negate(double number)
    {
        return -number;
    }

    /// <summary>Drops the decimals of a number, towards zero.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The whole part (-2.7 becomes -2).</returns>
    [NodeName("Math.Truncate")]
    [return: NodeName("value")]
    [NodeDescription("Drops the decimals, towards zero (2.7 becomes 2, -2.7 becomes -2).")]
    [NodeSearchTags("integer", "whole", "cut", "decimals")]
    public static double Truncate(double number)
    {
        return Math.Truncate(number);
    }

    /// <summary>Limits a number to a range.</summary>
    /// <param name="value">The number to limit.</param>
    /// <param name="min">The lowest allowed value.</param>
    /// <param name="max">The highest allowed value.</param>
    /// <returns>The value, or the nearest bound when it lies outside.</returns>
    [NodeName("Math.Clamp")]
    [return: NodeName("value")]
    [NodeDescription("Limits a number to a range: below the minimum gives the minimum, above the maximum gives the maximum.")]
    [NodeSearchTags("limit", "bound", "range", "saturate", "cap")]
    public static double Clamp(double value, double min, double max)
    {
        if (min > max)
        {
            throw new ArgumentException(
                "Math.Clamp needs min <= max; got min " + min.ToString(CultureInfo.InvariantCulture) +
                " and max " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>Linear interpolation between two numbers.</summary>
    /// <param name="a">The value at t = 0.</param>
    /// <param name="b">The value at t = 1.</param>
    /// <param name="t">How far from a towards b (0 to 1; outside the range extrapolates).</param>
    /// <returns>a + (b - a) * t.</returns>
    [NodeName("Math.Lerp")]
    [return: NodeName("value")]
    [NodeDescription("Linear interpolation: a at t = 0, b at t = 1, in between for values between (not limited).")]
    [NodeSearchTags("interpolate", "blend", "mix", "between")]
    public static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    /// <summary>What percentage one number is of another.</summary>
    /// <param name="part">The part.</param>
    /// <param name="total">The whole.</param>
    /// <returns>part / total * 100, or 0 when the total is 0.</returns>
    [NodeName("Math.Percent")]
    [return: NodeName("percent")]
    [NodeDescription("What percentage the part is of the total (37 of 340 gives 10.88); 0 when the total is 0, so an empty model reads 0 %.")]
    [NodeSearchTags("percentage", "ratio", "share", "kpi", "fraction", "%")]
    public static double Percent(double part, double total)
    {
        return total == 0d ? 0d : part / total * 100d;
    }

    /// <summary>Rounds a number to the nearest multiple of another.</summary>
    /// <param name="value">The number to round.</param>
    /// <param name="multiple">The step to round to (greater than 0), e.g. 0.05 or 250.</param>
    /// <returns>The nearest multiple; halves round away from zero.</returns>
    [NodeName("Math.RoundToMultiple")]
    [return: NodeName("value")]
    [NodeDescription("Rounds to the nearest multiple of a step, e.g. a length to the nearest 5 mm or a level to 0.25 m.")]
    [NodeSearchTags("round", "step", "snap", "grid", "nearest")]
    public static double RoundToMultiple(double value, double multiple)
    {
        if (!(multiple > 0d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiple), "Math.RoundToMultiple needs a step above 0; got " + multiple.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return Math.Round(value / multiple, MidpointRounding.AwayFromZero) * multiple;
    }

    /// <summary>A list of evenly spaced numbers.</summary>
    /// <param name="start">The first number.</param>
    /// <param name="count">How many numbers (0 to 1,000,000).</param>
    /// <param name="step">The distance between neighbours (negative counts down).</param>
    /// <returns>start, start + step, … (count numbers).</returns>
    [NodeName("Math.Sequence")]
    [return: NodeName("numbers")]
    [NodeDescription("A list of count numbers starting at start and growing by step (the count-based sibling of List.Range).")]
    [NodeSearchTags("range", "series", "list", "count", "steps")]
    public static IList<double> Sequence(double start, [NodeRange(0, 1000000)] int count, double step = 1d)
    {
        if (count < 0 || count > MaxSequenceLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), "Math.Sequence makes 0 to 1,000,000 numbers; got " + count.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var list = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(start + step * i);
        }

        return list;
    }

    /// <summary>
    /// Evaluates a formula over up to six numbers, a to f. A list wired to any input evaluates the formula once per item, like
    /// every other node.
    /// </summary>
    /// <param name="expression">The formula, e.g. <c>a * b + 2</c> or <c>if(a &gt; 10, a - 10, 0)</c>.</param>
    /// <param name="a">Value of a.</param>
    /// <param name="b">Value of b.</param>
    /// <param name="c">Value of c.</param>
    /// <param name="d">Value of d.</param>
    /// <param name="e">Value of e.</param>
    /// <param name="f">Value of f.</param>
    /// <returns>The result of the formula.</returns>
    [NodeName("Math.Formula")]
    [return: NodeName("result")]
    [NodeDescription(
        "Evaluates a formula such as \"a * b + 2\" or \"if(a > 10, a - 10, 0)\" over the inputs a to f. Operators + - * / % ^ and comparisons; " +
        "functions abs sqrt sin cos tan (radians) asin acos atan atan2 ln log exp pow min max round floor ceil trunc sign clamp mod hypot rad deg if; " +
        "constants pi, tau. A list on any input evaluates once per item.")]
    [NodeSearchTags("expression", "calculate", "equation", "code block", "calc", "evaluate", "math")]
    public static double Formula(string expression, double a = 0d, double b = 0d, double c = 0d, double d = 0d, double e = 0d, double f = 0d)
    {
        var compiled = FormulaParser.Compile(expression);
        return compiled(new[] { a, b, c, d, e, f });
    }

    private static double ToRadians(double angle, string unit, string nodeName)
    {
        return IsRadians(unit, nodeName) ? angle : angle * Math.PI / 180d;
    }

    private static double FromRadians(double angle, string unit, string nodeName)
    {
        return IsRadians(unit, nodeName) ? angle : angle * 180d / Math.PI;
    }

    private static bool IsRadians(string unit, string nodeName)
    {
        var key = (unit ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0 || key == "degrees" || key == "degree" || key == "deg")
        {
            return false;
        }

        if (key == "radians" || key == "radian" || key == "rad")
        {
            return true;
        }

        throw new ArgumentException(nodeName + ": the unit must be \"degrees\" or \"radians\"; got '" + unit + "'.", nameof(unit));
    }

    private static void RequireUnitRange(double value, string nodeName)
    {
        if (value < -1d || value > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), nodeName + " needs a value from -1 to 1; got " + value.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private static void RequirePositive(double value, string nodeName)
    {
        if (!(value > 0d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), nodeName + " needs a number above 0; got " + value.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}
