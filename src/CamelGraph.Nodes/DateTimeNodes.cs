using System;
using System.Globalization;
using System.Text.RegularExpressions;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// Date and time nodes. Values are <see cref="DateTime"/>; the engine's
/// coercion parses invariant-culture date strings on the way in.
/// </summary>
[NodeCategory("DateTime")]
public static class DateTimeNodes
{
    /// <summary>
    /// The current local date and time, captured when the node executes.
    /// Re-runs of a clean graph reuse the cached value; mark the node dirty
    /// (or edit an upstream input) to refresh it.
    /// </summary>
    /// <returns>The current local date and time.</returns>
    [NodeName("DateTime.Now")]
    [return: NodeName("dateTime")]
    [NodeDescription(
        "Returns the current local date and time (captured at execution). At the top level the value is kept until you run " +
        "the node again; inside a node group every call asks the clock again, so for one timestamp shared by a whole report " +
        "make it outside the group and pass it in through a Group Input.")]
    [NodeSearchTags("today", "current", "clock", "time", "timestamp")]
    public static DateTime Now()
    {
        return DateTime.Now;
    }

    /// <summary>
    /// Formats a date/time as text using a .NET format string and the
    /// invariant culture (e.g. "yyyy-MM-dd", "HH:mm").
    /// </summary>
    /// <param name="dateTime">The date/time to format.</param>
    /// <param name="format">.NET date format string.</param>
    /// <returns>The formatted text.</returns>
    [NodeName("DateTime.Format")]
    [return: NodeName("text")]
    [NodeDescription(
        "Formats a date/time as text using a .NET format string (invariant culture): yyyy-MM-dd gives 2026-07-10, dd/MM/yyyy " +
        "gives 10/07/2026, HH:mm gives 14:30, MMMM gives the English month name.")]
    [NodeSearchTags("tostring", "date", "time", "pattern")]
    public static string Format(DateTime dateTime, string format = "yyyy-MM-dd HH:mm:ss")
    {
        if (string.IsNullOrEmpty(format))
        {
            format = "yyyy-MM-dd HH:mm:ss";
        }

        try
        {
            return dateTime.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            throw new FormatException(
                "DateTime.Format: '" + format + "' is not a date format. Use letters such as yyyy-MM-dd HH:mm:ss " +
                "(yyyy year, MM month, dd day, HH hour, mm minute, ss second).");
        }
    }

    /// <summary>
    /// Parses text as a date/time (invariant culture). With the default empty
    /// format any standard date syntax is accepted (e.g. "2026-07-10 14:30"); dates
    /// written with slashes, dots or dashes read month first unless <paramref name="dayFirst"/> is on.
    /// A .NET format string (e.g. "dd/MM/yyyy") makes the parse exact.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="format">Optional exact .NET date format string; when given, dayFirst is not used.</param>
    /// <param name="dayFirst">True to read numeric dates such as 10/07/2026, 10.07.2026 or 10-07-2026 as day, month, year (10 July); false (default) reads them month first (7 October). ISO dates (2026-07-10) are never ambiguous.</param>
    /// <returns>The parsed date/time.</returns>
    [NodeName("DateTime.Parse")]
    [NodeAliases("CamelGraph.Nodes.DateTimeNodes.Parse@string,string")]
    [return: NodeName("dateTime")]
    [NodeDescription(
        "Parses text as a date/time. ISO dates (2026-07-10, 2026-07-10 14:30) always work. Numeric dates with slashes, dots " +
        "or dashes are ambiguous: 10/07/2026 is 7 October month first (the default) and 10 July with dayFirst on. When a " +
        "date could be read either way and dayFirst is off, the node says which reading it took. An exact .NET format " +
        "(dd/MM/yyyy, dd.MM.yyyy HH:mm) overrides both and refuses anything else.")]
    [NodeSearchTags("fromstring", "convert", "date", "time", "dayfirst", "day first", "dd/mm/yyyy", "european", "ambiguous")]
    public static DateTime Parse(string text, string format = "", bool dayFirst = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("DateTime.Parse requires a date/time string such as \"2026-07-10 14:30\".", nameof(text));
        }

        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(format))
        {
            if (dayFirst && DateTime.TryParseExact(trimmed, DayFirstFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var european))
            {
                return european;
            }

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                if (!dayFirst)
                {
                    WarnIfAmbiguous(trimmed, parsed);
                }

                return parsed;
            }

            throw new FormatException(
                "DateTime.Parse cannot parse '" + text + "'. Use an ISO-style date such as \"2026-07-10 14:30\" " +
                "or supply an exact format string (e.g. \"dd/MM/yyyy\")." +
                (NumericDate.IsMatch(trimmed) && !dayFirst ? " If the date is written day first (like 13/07/2026), turn dayFirst on." : string.Empty));
        }

        bool exactMatch;
        DateTime exact;
        try
        {
            exactMatch = DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out exact);
        }
        catch (FormatException)
        {
            throw new FormatException(
                "DateTime.Parse: '" + format + "' is not a date format. Use letters such as dd/MM/yyyy HH:mm " +
                "(yyyy year, MM month, dd day, HH hour, mm minute, ss second).");
        }

        if (exactMatch)
        {
            return exact;
        }

        throw new FormatException("DateTime.Parse cannot parse '" + text + "' with the format '" + format + "'.");
    }

    // The day-first spellings: 1 or 2 digit day and month, 2 or 4 digit year, optional time, with / . or - between the parts.
    private static readonly string[] DayFirstFormats = BuildDayFirstFormats();

    private static string[] BuildDayFirstFormats()
    {
        var formats = new System.Collections.Generic.List<string>();
        foreach (var separator in new[] { "'/'", ".", "-" })
        {
            foreach (var year in new[] { "yyyy", "yy" })
            {
                var date = "d" + separator + "M" + separator + year;
                formats.Add(date);
                formats.Add(date + " H:mm");
                formats.Add(date + " H:mm:ss");
            }
        }

        return formats.ToArray();
    }

    private static readonly Regex NumericDate = new Regex(
        @"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2}|\d{4})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // 10/07/2026 read month first is 7 October; say so when 10 July was just as likely.
    private static void WarnIfAmbiguous(string text, DateTime parsed)
    {
        var match = NumericDate.Match(text);
        if (!match.Success)
        {
            return;
        }

        var first = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (first == second || first > 12 || second > 12)
        {
            return;
        }

        NodeWarnings.Add(
            "'" + text + "' could be " + parsed.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + " (month first, as read) or " +
            new DateTime(parsed.Year, parsed.Day, parsed.Month).ToString("d MMMM yyyy", CultureInfo.InvariantCulture) +
            " (day first). Turn dayFirst on for day-first dates.");
    }

    /// <summary>Builds a date from year, month and day numbers (midnight, local kind-less).</summary>
    /// <param name="year">Year, e.g. 2026.</param>
    /// <param name="month">Month, 1-12.</param>
    /// <param name="day">Day of month, 1-31.</param>
    /// <returns>The date at midnight.</returns>
    [NodeName("DateTime.ByDate")]
    [return: NodeName("dateTime")]
    [NodeDescription("Creates a date from year, month and day numbers.")]
    [NodeSearchTags("construct", "ymd", "calendar", "date")]
    public static DateTime ByDate(int year, int month, int day)
    {
        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException(
                "DateTime.ByDate: " + year.ToString(CultureInfo.InvariantCulture) + "-" +
                month.ToString(CultureInfo.InvariantCulture) + "-" +
                day.ToString(CultureInfo.InvariantCulture) +
                " is not a valid calendar date.", ex);
        }
    }

    /// <summary>
    /// Offsets a date/time by a number of days. Fractional days are supported
    /// (0.5 = 12 hours) and negative values move backwards in time. Retired: use <c>DateTime.Add</c>, which
    /// also does hours, minutes, weeks, months and years.
    /// </summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="days">Days to add (fractional and negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddDays")]
    [NodeDeprecated("DateTime.Add")]
    [return: NodeName("dateTime")]
    [NodeDescription("Offsets a date/time by a number of days (fractional and negative values allowed).")]
    [NodeSearchTags("offset", "shift", "schedule", "date")]
    public static DateTime AddDays(DateTime dateTime, double days)
    {
        return DateTimeExtraNodes.AddUnits("DateTime.AddDays", "days", dateTime, days, "days");
    }

    /// <summary>
    /// Signed number of days between two date/times (end minus start).
    /// Fractional results reflect partial days; a negative result means the
    /// end is before the start.
    /// </summary>
    /// <param name="start">The starting date/time.</param>
    /// <param name="end">The ending date/time.</param>
    /// <returns>The signed day difference.</returns>
    [NodeName("DateTime.DaysBetween")]
    [return: NodeName("days")]
    [NodeDescription(
        "Returns the signed number of days between two date/times (end minus start), with the time of day as a fraction. " +
        "For how old something is, wire the date into start and DateTime.Now into end; a date in the future then gives a " +
        "negative number. DateTime.Difference gives the same in hours, weeks, months and so on.")]
    [NodeSearchTags("difference", "duration", "span", "elapsed", "age", "old", "overdue", "since", "AgeInDays")]
    public static double DaysBetween(DateTime start, DateTime end)
    {
        return (end - start).TotalDays;
    }
}
