using System;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// Input rules shared by the TimeLiner progress nodes and Appearance.Focus, free of Navisworks
/// types so they are unit-testable: percentages and date ranges.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ScheduleRules
{
    /// <summary>
    /// Clamps a progress percentage into 0-100: 120 becomes 100 and -5 becomes 0 (a task cannot be more
    /// than finished). Infinite values clamp the same way; NaN is an error.
    /// </summary>
    /// <param name="percent">The percentage typed or wired in.</param>
    /// <returns>A number from 0 to 100.</returns>
    /// <exception cref="ArgumentException">The value is not a number.</exception>
    public static double ClampPercent(double percent)
    {
        if (double.IsNaN(percent))
        {
            throw new ArgumentException(
                "The progress is not a number. Wire a number from 0 to 100.", nameof(percent));
        }

        return Math.Max(0.0, Math.Min(100.0, percent));
    }

    /// <summary>
    /// Converts a 0-100 percentage into the 0-1 fraction Navisworks transparency overrides take.
    /// Unlike progress this is strict: a value outside 0-100 is almost always a mistake (0.85 typed
    /// for 85, say) and would silently produce a nearly opaque model.
    /// </summary>
    /// <param name="percent">The percentage typed or wired in.</param>
    /// <param name="parameterName">The input's name, for the message.</param>
    /// <returns>A fraction from 0 to 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or outside 0-100.</exception>
    public static double PercentToFraction(double percent, string parameterName)
    {
        if (double.IsNaN(percent) || percent < 0.0 || percent > 100.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "'" + parameterName + "' is a percentage from 0 (opaque) to 100 (invisible); got " +
                percent.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return percent / 100.0;
    }

    /// <summary>Checks that a date range does not end before it starts.</summary>
    /// <param name="start">The start date.</param>
    /// <param name="end">The end date.</param>
    /// <param name="what">What the range is, for the message ("actual").</param>
    /// <param name="endParameterName">The name of the end input, for the exception.</param>
    /// <exception cref="ArgumentException">The end is before the start.</exception>
    public static void RequireDateRange(DateTime start, DateTime end, string what, string endParameterName)
    {
        if (end < start)
        {
            throw new ArgumentException(
                "The " + what + " end date (" + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                ") is before the " + what + " start date (" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ").",
                endParameterName);
        }
    }
}
