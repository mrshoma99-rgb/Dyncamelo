using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// More date and time nodes: calendar parts, month/year arithmetic, period boundaries, date ranges and Unix time.
/// Like <see cref="DateTimeNodes"/> they are culture-independent: weekday names are English and weeks follow ISO 8601.
/// </summary>
[NodeCategory("DateTime")]
public static class DateTimeExtraNodes
{
    private const int MaxRangeItems = 100000;

    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Splits a date/time into its calendar parts. The weekday is the English name ("Monday"). The week number and
    /// week-year follow ISO 8601 (weeks start on Monday and week 1 is the week containing the year's first Thursday),
    /// so the first days of January can belong to week 52 or 53 of the previous isoYear. The quarter is 1 to 4.
    /// </summary>
    /// <param name="dateTime">The date/time to split.</param>
    /// <returns>Dictionary with "year", "month", "day", "hour", "minute", "second", "weekday", "dayOfYear", "isoWeek", "isoYear" and "quarter".</returns>
    [NodeName("DateTime.Components")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [MultiReturn("year", "month", "day", "hour", "minute", "second", "weekday", "dayOfYear", "isoWeek", "isoYear", "quarter")]
    [PortKinds("integer", "integer", "integer", "integer", "integer", "integer", "text", "integer", "integer", "integer", "integer")]
    [NodeDescription("Splits a date/time into year, month, day, hour, minute, second, English weekday name, day of year, ISO week, ISO year and quarter.")]
    [NodeSearchTags("deconstruct", "parts", "year", "month", "weekday", "week number", "iso week", "quarter")]
    public static Dictionary<string, object> Components(DateTime dateTime)
    {
        // ISO 8601: the week belongs to the year that holds its Thursday.
        var mondayBased = ((int)dateTime.DayOfWeek + 6) % 7;
        var thursday = dateTime.Date.AddDays(3 - mondayBased);

        return new Dictionary<string, object>
        {
            ["year"] = dateTime.Year,
            ["month"] = dateTime.Month,
            ["day"] = dateTime.Day,
            ["hour"] = dateTime.Hour,
            ["minute"] = dateTime.Minute,
            ["second"] = dateTime.Second,
            ["weekday"] = dateTime.DayOfWeek.ToString(),
            ["dayOfYear"] = dateTime.DayOfYear,
            ["isoWeek"] = (thursday.DayOfYear - 1) / 7 + 1,
            ["isoYear"] = thursday.Year,
            ["quarter"] = (dateTime.Month - 1) / 3 + 1,
        };
    }

    /// <summary>
    /// Adds calendar months. The day of month is kept where possible and otherwise moves to the last day of the
    /// target month (31 January plus 1 month is 28 or 29 February). Negative values go back.
    /// </summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="months">Months to add (negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddMonths")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds calendar months to a date/time (the day clamps to the end of a shorter month).")]
    [NodeSearchTags("offset", "shift", "schedule", "month", "later", "earlier")]
    public static DateTime AddMonths(DateTime dateTime, int months)
    {
        return Shift("DateTime.AddMonths", () => dateTime.AddMonths(months));
    }

    /// <summary>Adds calendar years (29 February plus 1 year is 28 February). Negative values go back.</summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="years">Years to add (negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddYears")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds calendar years to a date/time (29 February clamps to 28 February in a common year).")]
    [NodeSearchTags("offset", "shift", "schedule", "year", "later", "earlier")]
    public static DateTime AddYears(DateTime dateTime, int years)
    {
        return Shift("DateTime.AddYears", () => dateTime.AddYears(years));
    }

    /// <summary>Adds hours. Fractional values (1.5 = 90 minutes) and negative values are allowed.</summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="hours">Hours to add (fractional and negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddHours")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds hours to a date/time (fractional and negative values allowed).")]
    [NodeSearchTags("offset", "shift", "schedule", "hour", "later", "earlier", "time")]
    public static DateTime AddHours(DateTime dateTime, double hours)
    {
        RequireFinite("DateTime.AddHours", nameof(hours), hours);
        return Shift("DateTime.AddHours", () => dateTime.AddHours(hours));
    }

    /// <summary>Adds minutes. Fractional values and negative values are allowed.</summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="minutes">Minutes to add (fractional and negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddMinutes")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds minutes to a date/time (fractional and negative values allowed).")]
    [NodeSearchTags("offset", "shift", "schedule", "minute", "later", "earlier", "time")]
    public static DateTime AddMinutes(DateTime dateTime, double minutes)
    {
        RequireFinite("DateTime.AddMinutes", nameof(minutes), minutes);
        return Shift("DateTime.AddMinutes", () => dateTime.AddMinutes(minutes));
    }

    /// <summary>
    /// Today's date on this computer (local time) at midnight. Like DateTime.Now it is captured when the node runs
    /// and reused until the node is marked dirty.
    /// </summary>
    /// <returns>The local date at 00:00:00.</returns>
    [NodeName("DateTime.Today")]
    [return: NodeName("dateTime")]
    [NodeDescription("Returns today's local date at midnight (captured at execution).")]
    [NodeSearchTags("current", "date", "now", "midnight", "day")]
    public static DateTime Today()
    {
        return DateTime.Today;
    }

    /// <summary>Compares two date/times (the time zone kind is ignored, as DateTime.DaysBetween does).</summary>
    /// <param name="a">The first date/time.</param>
    /// <param name="b">The second date/time.</param>
    /// <returns>-1 when a is earlier than b, 0 when they are equal, 1 when a is later than b.</returns>
    [NodeName("DateTime.Compare")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("result")]
    [NodeDescription("Compares two date/times: -1 when the first is earlier, 0 when equal, 1 when it is later.")]
    [NodeSearchTags("earlier", "later", "before", "after", "equal", "order", "sort")]
    public static int Compare(DateTime a, DateTime b)
    {
        return Math.Sign(DateTime.Compare(a, b));
    }

    /// <summary>
    /// The start of the day, week, month, quarter or year that contains a date/time, at midnight. Weeks start on
    /// Monday unless firstDayOfWeek says "Sunday". Quarters are January, April, July and October.
    /// </summary>
    /// <param name="dateTime">The date/time inside the period.</param>
    /// <param name="unit">"day", "week", "month", "quarter" or "year" (any case).</param>
    /// <param name="firstDayOfWeek">"Monday" or "Sunday" (any case); only used for "week".</param>
    /// <returns>The first instant (00:00:00) of the period.</returns>
    [NodeName("DateTime.StartOf")]
    [return: NodeName("dateTime")]
    [NodeDescription("Returns midnight at the start of the day, week, month, quarter or year containing a date/time.")]
    [NodeSearchTags("floor", "truncate", "period", "first day", "beginning", "week", "month", "quarter", "year")]
    public static DateTime StartOf(
        DateTime dateTime,
        [NodeChoices("day", "week", "month", "quarter", "year")] string unit = "week",
        [NodeChoices("Monday", "Sunday")] string firstDayOfWeek = "Monday")
    {
        return StartOfPeriod("DateTime.StartOf", dateTime, unit, firstDayOfWeek, out _);
    }

    /// <summary>
    /// The last moment of the day, week, month, quarter or year that contains a date/time: the start of the NEXT
    /// period minus one millisecond (so the end of 2026-07-10 is 2026-07-10 23:59:59.999). Compare with "&lt;=" to test
    /// membership of the period.
    /// </summary>
    /// <param name="dateTime">The date/time inside the period.</param>
    /// <param name="unit">"day", "week", "month", "quarter" or "year" (any case).</param>
    /// <param name="firstDayOfWeek">"Monday" or "Sunday" (any case); only used for "week".</param>
    /// <returns>The start of the next period minus one millisecond.</returns>
    [NodeName("DateTime.EndOf")]
    [return: NodeName("dateTime")]
    [NodeDescription("Returns the last millisecond of the day, week, month, quarter or year containing a date/time (start of the next period minus 1 ms).")]
    [NodeSearchTags("ceiling", "period", "last day", "finish", "deadline", "week", "month", "quarter", "year")]
    public static DateTime EndOf(
        DateTime dateTime,
        [NodeChoices("day", "week", "month", "quarter", "year")] string unit = "week",
        [NodeChoices("Monday", "Sunday")] string firstDayOfWeek = "Monday")
    {
        var start = StartOfPeriod("DateTime.EndOf", dateTime, unit, firstDayOfWeek, out var normalizedUnit);
        return Shift("DateTime.EndOf", () =>
        {
            DateTime next;
            switch (normalizedUnit)
            {
                case "day": next = start.AddDays(1); break;
                case "week": next = start.AddDays(7); break;
                case "month": next = start.AddMonths(1); break;
                case "quarter": next = start.AddMonths(3); break;
                default: next = start.AddYears(1); break;
            }

            return next.AddMilliseconds(-1);
        });
    }

    /// <summary>
    /// A list of dates from start to end, both included, stepping by a number of days (fractional steps such as 0.5
    /// are allowed). An end before the start gives an empty list. A range of more than 100000 items is refused.
    /// </summary>
    /// <param name="start">The first date.</param>
    /// <param name="end">The last date (included when the step lands on it).</param>
    /// <param name="stepDays">Days between two items; must be greater than 0.</param>
    /// <returns>The dates, in order.</returns>
    [NodeName("DateTime.Range")]
    [return: NodeName("dates")]
    [NodeDescription("Creates a list of dates from a start to an end (both included) with a step in days; at most 100000 items.")]
    [NodeSearchTags("sequence", "series", "calendar", "schedule", "every", "days", "timeline")]
    public static IList<DateTime> Range(DateTime start, DateTime end, double stepDays = 1)
    {
        if (double.IsNaN(stepDays) || double.IsInfinity(stepDays) || stepDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepDays),
                "DateTime.Range: stepDays must be a number greater than 0 (got " +
                stepDays.ToString("R", CultureInfo.InvariantCulture) + ").");
        }

        var result = new List<DateTime>();
        var span = end.Ticks - start.Ticks;
        if (span < 0)
        {
            return result;
        }

        var stepTicksExact = stepDays * TimeSpan.TicksPerDay;
        if (stepTicksExact > span)
        {
            result.Add(start);
            return result;
        }

        var stepTicks = Math.Max(1L, (long)Math.Round(stepTicksExact));
        var count = span / stepTicks + 1;
        if (count > MaxRangeItems)
        {
            throw new ArgumentException(
                "DateTime.Range: a step of " + stepDays.ToString("R", CultureInfo.InvariantCulture) +
                " day(s) between the two dates would make " + count.ToString(CultureInfo.InvariantCulture) +
                " items; the limit is " + MaxRangeItems.ToString(CultureInfo.InvariantCulture) +
                ". Use a larger step or a shorter period.");
        }

        for (long i = 0; i < count; i++)
        {
            result.Add(start.AddTicks(i * stepTicks));
        }

        return result;
    }

    /// <summary>
    /// Days from a date/time up to a reference date/time (reference minus dateTime), fractional for partial days.
    /// Wire DateTime.Now into the reference to get "how old is this?"; a date in the future gives a negative age.
    /// </summary>
    /// <param name="dateTime">The date/time to measure the age of, e.g. when a clash was created.</param>
    /// <param name="reference">The date/time to measure up to, e.g. DateTime.Now.</param>
    /// <returns>The age in days (reference minus dateTime).</returns>
    [NodeName("DateTime.AgeInDays")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("days")]
    [NodeDescription("Returns how many days old a date/time is at a reference date/time (reference minus date; fractional days included).")]
    [NodeSearchTags("age", "old", "elapsed", "since", "overdue", "duration", "days")]
    public static double AgeInDays(DateTime dateTime, DateTime reference)
    {
        return (reference - dateTime).TotalDays;
    }

    /// <summary>
    /// Converts a date/time to Unix time, the seconds since 1970-01-01 00:00:00 UTC. A UTC value is used as it is, a
    /// local value is converted to UTC, and a value without a time zone is taken to be UTC (so the result does not
    /// depend on the computer's time zone). Fractions of a second are kept.
    /// </summary>
    /// <param name="dateTime">The date/time to convert.</param>
    /// <returns>Seconds since the Unix epoch (negative before 1970).</returns>
    [NodeName("DateTime.ToUnixSeconds")]
    [return: NodeName("seconds")]
    [NodeDescription("Converts a date/time to Unix seconds since 1970-01-01 UTC (a value without a time zone is taken as UTC).")]
    [NodeSearchTags("epoch", "timestamp", "posix", "unix", "seconds", "convert")]
    public static double ToUnixSeconds(DateTime dateTime)
    {
        var utc = dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime;
        return (utc.Ticks - UnixEpoch.Ticks) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>Converts Unix seconds (since 1970-01-01 00:00:00 UTC) to a UTC date/time. Fractions of a second are kept.</summary>
    /// <param name="seconds">Seconds since the Unix epoch (negative before 1970).</param>
    /// <returns>The date/time, with kind UTC.</returns>
    [NodeName("DateTime.FromUnixSeconds")]
    [return: NodeName("dateTime")]
    [NodeDescription("Converts Unix seconds since 1970-01-01 UTC to a (UTC) date/time.")]
    [NodeSearchTags("epoch", "timestamp", "posix", "unix", "seconds", "convert")]
    public static DateTime FromUnixSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                "DateTime.FromUnixSeconds: seconds must be a finite number (got " + seconds.ToString("R", CultureInfo.InvariantCulture) + ").");
        }

        // 0001-01-01 to 9999-12-31 is about 3.2e11 seconds either side of the epoch; check before converting to ticks.
        var ticks = seconds * TimeSpan.TicksPerSecond;
        if (Math.Abs(ticks) > (double)DateTime.MaxValue.Ticks)
        {
            throw OutOfRange("DateTime.FromUnixSeconds", null);
        }

        return Shift("DateTime.FromUnixSeconds", () => new DateTime(UnixEpoch.Ticks + (long)Math.Round(ticks), DateTimeKind.Utc));
    }

    // ── Helpers (private, so the loader does not import them) ───────────────

    private static DateTime StartOfPeriod(string node, DateTime dateTime, string unit, string firstDayOfWeek, out string normalizedUnit)
    {
        var period = NormalizeUnit(node, unit);
        var weekStart = ParseFirstDay(node, firstDayOfWeek);
        normalizedUnit = period;

        return Shift(node, () =>
        {
            var day = dateTime.Date;
            switch (period)
            {
                case "day":
                    return day;
                case "week":
                    return day.AddDays(-(((int)dateTime.DayOfWeek - (int)weekStart + 7) % 7));
                case "month":
                    return day.AddDays(-(dateTime.Day - 1));
                case "quarter":
                    return day.AddDays(-(dateTime.Day - 1)).AddMonths(-((dateTime.Month - 1) % 3));
                default:
                    return day.AddDays(-(dateTime.DayOfYear - 1));
            }
        });
    }

    private static string NormalizeUnit(string node, string unit)
    {
        var period = string.IsNullOrWhiteSpace(unit) ? "week" : unit.Trim().ToLowerInvariant();
        if (period != "day" && period != "week" && period != "month" && period != "quarter" && period != "year")
        {
            throw new ArgumentException(
                node + ": unit must be \"day\", \"week\", \"month\", \"quarter\" or \"year\" (got '" + unit + "').",
                nameof(unit));
        }

        return period;
    }

    private static DayOfWeek ParseFirstDay(string node, string firstDayOfWeek)
    {
        if (string.IsNullOrWhiteSpace(firstDayOfWeek) ||
            string.Equals(firstDayOfWeek.Trim(), "Monday", StringComparison.OrdinalIgnoreCase))
        {
            return DayOfWeek.Monday;
        }

        if (string.Equals(firstDayOfWeek.Trim(), "Sunday", StringComparison.OrdinalIgnoreCase))
        {
            return DayOfWeek.Sunday;
        }

        throw new ArgumentException(
            node + ": firstDayOfWeek must be \"Monday\" or \"Sunday\" (got '" + firstDayOfWeek + "').",
            nameof(firstDayOfWeek));
    }

    private static void RequireFinite(string node, string parameter, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                parameter,
                node + ": " + parameter + " must be a finite number (got " + value.ToString("R", CultureInfo.InvariantCulture) + ").");
        }
    }

    // Runs a date calculation and turns "the result left the supported range" into a message for the user.
    private static T Shift<T>(string node, Func<T> calculation)
    {
        try
        {
            return calculation();
        }
        catch (ArgumentException ex)
        {
            throw OutOfRange(node, ex);
        }
        catch (OverflowException ex)
        {
            throw OutOfRange(node, ex);
        }
    }

    private static ArgumentException OutOfRange(string node, Exception? inner)
    {
        return new ArgumentException(
            node + ": the result is outside the supported dates (0001-01-01 to 9999-12-31). Use a smaller offset.",
            inner);
    }
}
