using System;
using System.Globalization;

namespace Dyncamelo.Core.Editing;

/// <summary>Step, precision and clamping helpers shared by the inline number field.</summary>
public static class NumberFormat
{
    /// <summary>
    /// The default increment for a field: 1 for integers, otherwise one decade
    /// below the default's magnitude (0.01 under 1, 0.1 under 10, 1 under 100, …).
    /// </summary>
    public static double NiceStep(double defaultValue, bool isInteger)
    {
        if (isInteger)
        {
            return 1d;
        }

        var magnitude = Math.Abs(defaultValue);
        if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude < 1d)
        {
            return 0.01d;
        }

        return Math.Pow(10d, Math.Floor(Math.Log10(magnitude)) - 1d);
    }

    /// <summary>Number of decimals to show for a step (0..6).</summary>
    public static int Decimals(double step)
    {
        if (double.IsNaN(step) || double.IsInfinity(step) || step <= 0d || step >= 1d)
        {
            return 0;
        }

        var decimals = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
        return Math.Max(0, Math.Min(6, decimals));
    }

    /// <summary>Formats a value with the step's precision, trimming trailing zeros, plus an optional unit.</summary>
    public static string Format(double value, double step, string? unit = null)
    {
        var text = value.ToString("F" + Decimals(step).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (text.IndexOf('.') >= 0)
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        if (text == "-0")
        {
            text = "0";
        }

        return string.IsNullOrEmpty(unit) ? text : text + " " + unit;
    }

    /// <summary>Clamps a value into [min, max]; NaN yields <paramref name="fallback"/>.</summary>
    public static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return value < min ? min : value > max ? max : value;
    }

    /// <summary>Rounds to the nearest multiple of <paramref name="step"/> (used by Ctrl-snap).</summary>
    public static double Snap(double value, double step)
    {
        if (step <= 0d || double.IsNaN(step))
        {
            return value;
        }

        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }
}
