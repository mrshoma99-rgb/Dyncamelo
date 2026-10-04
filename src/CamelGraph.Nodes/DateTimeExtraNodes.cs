using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

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
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
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
    /// Adds an amount of time to a date/time, in the unit you choose. Seconds, minutes, hours, days and weeks accept
    /// fractions (1.5 hours = 90 minutes) and negative values go back. Months and years are calendar steps: the day of month
    /// is kept where possible and otherwise moves to the last day of the target month (31 January plus 1 month is 28 or 29
    /// February); they take whole numbers, and a fraction is rounded with a warning.
    /// </summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="amount">How much to add, in the chosen unit (negative values go back).</param>
    /// <param name="unit">"seconds", "minutes", "hours", "days" (default), "weeks", "months" or "years" (any case).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.Add")]
    [return: NodeName("dateTime")]
    [NodeDescription(
        "Adds an amount of time to a date/time in a chosen unit — seconds, minutes, hours, days, weeks, months or years " +
        "(negative values go back). Days, hours and the smaller units take fractions (0.5 days = 12 hours); months and years " +
        "are calendar steps that clamp to the end of a shorter month (31 January + 1 month = 28 February) and take whole " +
        "numbers. For working days use DateTime.AddWorkdays.")]
    [NodeSearchTags(
        "offset", "shift", "schedule", "date", "later", "earlier", "time", "plus", "minus", "move", "delay", "duration",
        "AddDays", "AddHours", "AddMinutes", "AddMonths", "AddYears", "month", "year", "hour", "minute", "day", "week")]
    public static DateTime Add(
        DateTime dateTime,
        double amount,
        [NodeChoices("seconds", "minutes", "hours", "days", "weeks", "months", "years")] string unit = "days")
    {
        return AddUnits("DateTime.Add", "amount", dateTime, amount, unit);
    }

    /// <summary>
    /// Adds calendar months. The day of month is kept where possible and otherwise moves to the last day of the
    /// target month (31 January plus 1 month is 28 or 29 February). Negative values go back. Retired: use DateTime.Add.
    /// </summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="months">Months to add (negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddMonths")]
    [NodeDeprecated("DateTime.Add")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds calendar months to a date/time (the day clamps to the end of a shorter month).")]
    [NodeSearchTags("offset", "shift", "schedule", "month", "later", "earlier")]
    public static DateTime AddMonths(DateTime dateTime, int months)
    {
        return AddUnits("DateTime.AddMonths", "months", dateTime, months, "months");
    }

    /// <summary>Adds calendar years (29 February plus 1 year is 28 February). Negative values go back. Retired: use DateTime.Add.</summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="years">Years to add (negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddYears")]
    [NodeDeprecated("DateTime.Add")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds calendar years to a date/time (29 February clamps to 28 February in a common year).")]
    [NodeSearchTags("offset", "shift", "schedule", "year", "later", "earlier")]
    public static DateTime AddYears(DateTime dateTime, int years)
    {
        return AddUnits("DateTime.AddYears", "years", dateTime, years, "years");
    }

    /// <summary>Adds hours. Fractional values (1.5 = 90 minutes) and negative values are allowed. Retired: use DateTime.Add.</summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="hours">Hours to add (fractional and negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddHours")]
    [NodeDeprecated("DateTime.Add")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds hours to a date/time (fractional and negative values allowed).")]
    [NodeSearchTags("offset", "shift", "schedule", "hour", "later", "earlier", "time")]
    public static DateTime AddHours(DateTime dateTime, double hours)
    {
        return AddUnits("DateTime.AddHours", "hours", dateTime, hours, "hours");
    }

    /// <summary>Adds minutes. Fractional values and negative values are allowed. Retired: use DateTime.Add.</summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="minutes">Minutes to add (fractional and negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddMinutes")]
    [NodeDeprecated("DateTime.Add")]
    [return: NodeName("dateTime")]
    [NodeDescription("Adds minutes to a date/time (fractional and negative values allowed).")]
    [NodeSearchTags("offset", "shift", "schedule", "minute", "later", "earlier", "time")]
    public static DateTime AddMinutes(DateTime dateTime, double minutes)
    {
        return AddUnits("DateTime.AddMinutes", "minutes", dateTime, minutes, "minutes");
    }

    /// <summary>
    /// Moves a date/time by a number of working days, skipping the weekend. The time of day is kept. A negative number goes
    /// back. Starting on a weekend day, the first working day counted is the next one in that direction (Saturday plus 1 is
    /// Monday); 0 days leaves the date as it is. Public holidays are not known.
    /// </summary>
    /// <param name="dateTime">The date/time to move.</param>
    /// <param name="days">Working days to add (whole number, negative goes back).</param>
    /// <param name="weekend">Which two days are not working days: "Saturday and Sunday" (default) or "Friday and Saturday".</param>
    /// <returns>The date/time that many working days later (or earlier).</returns>
    [NodeName("DateTime.AddWorkdays")]
    [return: NodeName("dateTime")]
    [NodeDescription(
        "Moves a date/time by a number of working days, skipping the weekend (Saturday and Sunday, or Friday and Saturday): " +
        "Friday + 1 is Monday, Monday - 1 is Friday, Saturday + 1 is Monday. The time of day is kept. Public holidays are " +
        "not known: move the result on by hand when one falls in between.")]
    [NodeSearchTags("workday", "working day", "business day", "weekday", "skip weekend", "schedule", "offset", "shift", "later", "earlier", "4d")]
    public static DateTime AddWorkdays(
        DateTime dateTime,
        int days,
        [NodeChoices("Saturday and Sunday", "Friday and Saturday")] string weekend = "Saturday and Sunday")
    {
        var (first, second) = WeekendDays("DateTime.AddWorkdays", weekend);
        return Shift("DateTime.AddWorkdays", () =>
        {
            if (days == 0)
            {
                return dateTime;
            }

            var step = days > 0 ? 1 : -1;
            long remaining = Math.Abs((long)days);
            var date = dateTime;
            if (IsWeekendDay(date.DayOfWeek, first, second))
            {
                // The first working day counted is the next one on the way.
                do
                {
                    date = date.AddDays(step);
                }
                while (IsWeekendDay(date.DayOfWeek, first, second));
                remaining--;
            }

            // On a working day five working days are exactly one calendar week.
            date = date.AddDays(7d * step * (remaining / 5));
            for (var left = remaining % 5; left > 0; left--)
            {
                do
                {
                    date = date.AddDays(step);
                }
                while (IsWeekendDay(date.DayOfWeek, first, second));
            }

            return date;
        });
    }

    /// <summary>Tests whether a date falls on the weekend.</summary>
    /// <param name="dateTime">The date/time to test.</param>
    /// <param name="weekend">Which two days are the weekend: "Saturday and Sunday" (default) or "Friday and Saturday".</param>
    /// <returns>True on a weekend day.</returns>
    [NodeName("DateTime.IsWeekend")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isWeekend")]
    [NodeDescription("True when a date falls on the weekend (Saturday and Sunday, or Friday and Saturday) — a mask for filtering a schedule down to working days.")]
    [NodeSearchTags("weekday", "workday", "working day", "business day", "saturday", "sunday", "schedule", "filter")]
    public static bool IsWeekend(DateTime dateTime, [NodeChoices("Saturday and Sunday", "Friday and Saturday")] string weekend = "Saturday and Sunday")
    {
        var (first, second) = WeekendDays("DateTime.IsWeekend", weekend);
        return IsWeekendDay(dateTime.DayOfWeek, first, second);
    }

    /// <summary>
    /// The signed distance from one date/time to another in a unit you choose (end minus start). Seconds, minutes, hours,
    /// days and weeks are exact elapsed time with fractions. Months and years count calendar months: from 10 January to
    /// 10 March is exactly 2 months, and a part of a month is the fraction of the month it has covered. A negative result means
    /// the end is before the start.
    /// </summary>
    /// <param name="start">The starting date/time.</param>
    /// <param name="end">The ending date/time.</param>
    /// <param name="unit">"seconds", "minutes", "hours", "days" (default), "weeks", "months" or "years" (any case).</param>
    /// <returns>The signed difference in that unit.</returns>
    [NodeName("DateTime.Difference")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("difference")]
    [NodeDescription(
        "How far apart two date/times are in a chosen unit — seconds, minutes, hours, days, weeks, months or years (end minus " +
        "start, so a negative number means the end is earlier). Months and years count calendar months (10 January to " +
        "10 March is exactly 2) with the rest as a fraction; the other units are elapsed time with fractions. For how old " +
        "something is, wire DateTime.Now into end.")]
    [NodeSearchTags("difference", "between", "duration", "span", "elapsed", "age", "old", "since", "overdue", "interval", "how long", "DaysBetween", "AgeInDays")]
    public static double Difference(
        DateTime start,
        DateTime end,
        [NodeChoices("seconds", "minutes", "hours", "days", "weeks", "months", "years")] string unit = "days")
    {
        var span = end - start;
        switch (NormalizeTimeUnit("DateTime.Difference", unit))
        {
            case "seconds":
                return span.TotalSeconds;
            case "minutes":
                return span.TotalMinutes;
            case "hours":
                return span.TotalHours;
            case "weeks":
                return span.TotalDays / 7d;
            case "months":
                return CalendarMonths(start, end);
            case "years":
                return CalendarMonths(start, end) / 12d;
            default:
                return span.TotalDays;
        }
    }

    /// <summary>
    /// Converts an Excel date serial number to a date/time. Excel stores a date as the number of days since 30 December 1899
    /// and the time of day as the fraction: 46000.5 is noon on that day. This is what <c>Excel.ReadFromFile</c> and
    /// <c>Table.FromExcelFile</c> give for a date cell.
    /// </summary>
    /// <param name="serial">The Excel serial number (days, with the time of day as a fraction).</param>
    /// <param name="dateSystem">"1900" (default, Excel for Windows and current Excel for Mac) or "1904" (workbooks from old Excel for Mac).</param>
    /// <returns>The date/time (kind not specified).</returns>
    [NodeName("DateTime.FromExcelSerial")]
    [return: NodeName("dateTime")]
    [NodeDescription(
        "Converts an Excel date serial number (days since 1899-12-30, the time of day as the fraction; 46000.5 is noon) to a " +
        "date/time — what Excel.ReadFromFile and Table.FromExcelFile hand over for a date cell. Wire the column in and the " +
        "whole column converts. Use dateSystem 1904 for workbooks that use the 1904 date system. Dates before March 1900 " +
        "come out one day later than Excel shows them, because Excel counts a 29 February 1900 that never existed.")]
    [NodeSearchTags("excel", "xlsx", "serial", "date", "spreadsheet", "oadate", "convert", "import", "column")]
    public static DateTime FromExcelSerial(double serial, [NodeChoices("1900", "1904")] string dateSystem = "1900")
    {
        if (double.IsNaN(serial) || double.IsInfinity(serial))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serial),
                "DateTime.FromExcelSerial: the serial number must be a finite number (got " + serial.ToString("R", CultureInfo.InvariantCulture) + ").");
        }

        var system = (dateSystem ?? string.Empty).Trim();
        if (system.Length != 0 && system != "1900" && system != "1904")
        {
            throw new ArgumentException(
                "DateTime.FromExcelSerial: dateSystem must be \"1900\" or \"1904\" (got '" + dateSystem + "').",
                nameof(dateSystem));
        }

        // Excel's 1904 system starts 1462 days later than the 1900 one.
        var days = system == "1904" ? serial + 1462d : serial;
        try
        {
            return DateTime.FromOADate(days);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                "DateTime.FromExcelSerial: " + serial.ToString("R", CultureInfo.InvariantCulture) +
                " is not a date (the serial number counts days from 1900; a date is between 0 and about 2,958,465).",
                ex);
        }
    }

    /// <summary>
    /// Today's date on this computer (local time) at midnight. Like DateTime.Now it is captured when the node runs
    /// and reused until the node is marked dirty.
    /// </summary>
    /// <returns>The local date at 00:00:00.</returns>
    [NodeName("DateTime.Today")]
    [return: NodeName("dateTime")]
    [NodeDescription(
        "Returns today's local date at midnight (captured at execution). At the top level the value is kept until you run " +
        "the node again; inside a node group every call asks the clock again, so for one date shared by a whole report make " +
        "it outside the group and pass it in through a Group Input.")]
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
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
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
    /// A list of dates from start to end, both included, stepping by a number of days, weeks, months or years (fractional
    /// day steps such as 0.5 are allowed; month and year steps are whole numbers and every item is counted from the start,
    /// so monthly dates from 31 January are 31 January, 28 February, 31 March). An end before the start gives an empty
    /// list. A range of more than 100000 items is refused.
    /// </summary>
    /// <param name="start">The first date.</param>
    /// <param name="end">The last date (included when the step lands on it).</param>
    /// <param name="step">How many units between two items; must be greater than 0 (a whole number for months and years).</param>
    /// <param name="unit">"days" (default), "weeks", "months" or "years" (any case).</param>
    /// <returns>The dates, in order.</returns>
    [NodeName("DateTime.Range")]
    [NodeAliases("CamelGraph.Nodes.DateTimeExtraNodes.Range@System.DateTime,System.DateTime,double")]
    [PortAlias("stepDays", "step")]
    [return: NodeName("dates")]
    [NodeDescription(
        "Creates a list of dates from a start to an end (both included) with a step in days, weeks, months or years " +
        "(\"every month\" lands on the same day of each month, clamped to the month's end); at most 100000 items.")]
    [NodeSearchTags("sequence", "series", "calendar", "schedule", "every", "days", "timeline", "monthly", "weekly", "yearly", "month", "week", "year")]
    public static IList<DateTime> Range(
        DateTime start,
        DateTime end,
        double step = 1,
        [NodeChoices("days", "weeks", "months", "years")] string unit = "days")
    {
        if (double.IsNaN(step) || double.IsInfinity(step) || step <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                "DateTime.Range: step must be a number greater than 0 (got " +
                step.ToString("R", CultureInfo.InvariantCulture) + ").");
        }

        var period = NormalizeTimeUnit("DateTime.Range", unit);
        if (period == "months" || period == "years")
        {
            return RangeByMonths(start, end, step, period == "years");
        }

        if (period != "days" && period != "weeks")
        {
            throw new ArgumentException(
                "DateTime.Range: the unit must be \"days\", \"weeks\", \"months\" or \"years\" (got '" + unit + "').", nameof(unit));
        }

        var stepDays = period == "weeks" ? step * 7d : step;
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
                "DateTime.Range: a step of " + step.ToString("R", CultureInfo.InvariantCulture) + " " + period +
                " between the two dates would make " + count.ToString(CultureInfo.InvariantCulture) +
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
    /// Retired: DateTime.DaysBetween does the same with start = the date and end = the reference.
    /// </summary>
    /// <param name="dateTime">The date/time to measure the age of, e.g. when a clash was created.</param>
    /// <param name="reference">The date/time to measure up to, e.g. DateTime.Now.</param>
    /// <returns>The age in days (reference minus dateTime).</returns>
    [NodeName("DateTime.AgeInDays")]
    [NodeDeprecated("DateTime.DaysBetween")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("days")]
    [NodeDescription("Returns how many days old a date/time is at a reference date/time (reference minus date; fractional days included).")]
    [NodeSearchTags("age", "old", "elapsed", "since", "overdue", "duration", "days")]
    public static double AgeInDays(DateTime dateTime, DateTime reference)
    {
        return DateTimeNodes.DaysBetween(dateTime, reference);
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

    // ── Helpers (private or internal, so the loader does not import them) ───

    // ── Units, shared by DateTime.Add / Difference / Range and the retired Add nodes ─────────────

    // The words behind the unit dropdowns; singular spellings and a few short forms are accepted too.
    internal static string NormalizeTimeUnit(string node, string? unit)
    {
        var key = (unit ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "":
            case "day":
            case "days":
            case "d":
                return "days";
            case "second":
            case "seconds":
            case "sec":
            case "secs":
            case "s":
                return "seconds";
            case "minute":
            case "minutes":
            case "min":
            case "mins":
                return "minutes";
            case "hour":
            case "hours":
            case "h":
                return "hours";
            case "week":
            case "weeks":
            case "w":
                return "weeks";
            case "month":
            case "months":
                return "months";
            case "year":
            case "years":
            case "y":
                return "years";
            default:
                throw new ArgumentException(
                    node + ": the unit must be seconds, minutes, hours, days, weeks, months or years (got '" + unit + "').",
                    nameof(unit));
        }
    }

    // Adds an amount in a unit. The retired DateTime.AddDays / AddHours / ... call it with their own node and parameter names,
    // so their messages still name the node the user placed.
    internal static DateTime AddUnits(string node, string parameter, DateTime dateTime, double amount, string unit)
    {
        RequireFinite(node, parameter, amount);
        switch (NormalizeTimeUnit(node, unit))
        {
            case "seconds":
                return Shift(node, () => dateTime.AddSeconds(amount));
            case "minutes":
                return Shift(node, () => dateTime.AddMinutes(amount));
            case "hours":
                return Shift(node, () => dateTime.AddHours(amount));
            case "weeks":
                return Shift(node, () => dateTime.AddDays(amount * 7d));
            case "months":
                var months = WholeNumber(node, "months", amount);
                return Shift(node, () => dateTime.AddMonths(months));
            case "years":
                var years = WholeNumber(node, "years", amount);
                return Shift(node, () => dateTime.AddYears(years));
            default:
                return Shift(node, () => dateTime.AddDays(amount));
        }
    }

    // Months and years are added whole: 1.5 becomes 2, and the node says so.
    private static int WholeNumber(string node, string what, double amount)
    {
        var rounded = Math.Round(amount, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) > int.MaxValue)
        {
            throw OutOfRange(node, null);
        }

        if (rounded != amount)
        {
            NodeWarnings.Add(
                "Whole " + what + " only: " + amount.ToString("R", CultureInfo.InvariantCulture) + " was rounded to " +
                rounded.ToString("R", CultureInfo.InvariantCulture) + ".");
        }

        return (int)rounded;
    }

    // Whole calendar months from start to end plus the part of the next month that has passed; negative when end is earlier.
    private static double CalendarMonths(DateTime start, DateTime end)
    {
        if (end < start)
        {
            return -CalendarMonths(end, start);
        }

        var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        var anchor = start.AddMonths(months);
        if (anchor > end)
        {
            months--;
            anchor = start.AddMonths(months);
        }

        try
        {
            var next = start.AddMonths(months + 1);
            if (next > anchor)
            {
                return months + (end - anchor).Ticks / (double)(next - anchor).Ticks;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // The month after the last supported one is not a date: the part of it is not counted.
        }

        return months;
    }

    // Monthly and yearly steps are counted from the start (start + n * step), so a month end does not drift.
    private static IList<DateTime> RangeByMonths(DateTime start, DateTime end, double step, bool years)
    {
        if (step < 1d || step != Math.Floor(step))
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                "DateTime.Range: with months or years the step must be a whole number of 1 or more (got " +
                step.ToString("R", CultureInfo.InvariantCulture) + ").");
        }

        var result = new List<DateTime>();
        if (end < start)
        {
            return result;
        }

        var monthsPerStep = step * (years ? 12d : 1d);
        for (var i = 0L; ; i++)
        {
            var offset = i * monthsPerStep;
            if (offset > int.MaxValue)
            {
                break;
            }

            DateTime item;
            try
            {
                item = start.AddMonths((int)offset);
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }

            if (item > end)
            {
                break;
            }

            if (result.Count >= MaxRangeItems)
            {
                throw new ArgumentException(
                    "DateTime.Range: a step of " + step.ToString("R", CultureInfo.InvariantCulture) + " " + (years ? "year(s)" : "month(s)") +
                    " between the two dates would make more than " + MaxRangeItems.ToString(CultureInfo.InvariantCulture) +
                    " items; the limit is " + MaxRangeItems.ToString(CultureInfo.InvariantCulture) +
                    ". Use a larger step or a shorter period.");
            }

            result.Add(item);
        }

        return result;
    }

    // "Saturday and Sunday" (default) or "Friday and Saturday".
    private static (DayOfWeek First, DayOfWeek Second) WeekendDays(string node, string? weekend)
    {
        var key = (weekend ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0 || key == "saturday and sunday" || key == "saturday, sunday" || key == "sat-sun")
        {
            return (DayOfWeek.Saturday, DayOfWeek.Sunday);
        }

        if (key == "friday and saturday" || key == "friday, saturday" || key == "fri-sat")
        {
            return (DayOfWeek.Friday, DayOfWeek.Saturday);
        }

        throw new ArgumentException(
            node + ": weekend must be \"Saturday and Sunday\" or \"Friday and Saturday\" (got '" + weekend + "').",
            nameof(weekend));
    }

    private static bool IsWeekendDay(DayOfWeek day, DayOfWeek first, DayOfWeek second)
    {
        return day == first || day == second;
    }

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

    internal static void RequireFinite(string node, string parameter, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                parameter,
                node + ": " + parameter + " must be a finite number (got " + value.ToString("R", CultureInfo.InvariantCulture) + ").");
        }
    }

    // Runs a date calculation and turns "the result left the supported range" into a message for the user.
    internal static T Shift<T>(string node, Func<T> calculation)
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

    internal static ArgumentException OutOfRange(string node, Exception? inner)
    {
        return new ArgumentException(
            node + ": the result is outside the supported dates (0001-01-01 to 9999-12-31). Use a smaller offset.",
            inner);
    }
}
