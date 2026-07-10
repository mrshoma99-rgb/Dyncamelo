using System;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

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
    [NodeDescription("Returns the current local date and time (captured at execution).")]
    [NodeSearchTags("today", "current", "clock", "time")]
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
    [NodeDescription("Formats a date/time as text using a .NET format string (invariant culture).")]
    [NodeSearchTags("tostring", "date", "time", "pattern")]
    public static string Format(DateTime dateTime, string format = "yyyy-MM-dd HH:mm:ss")
    {
        if (string.IsNullOrEmpty(format))
        {
            format = "yyyy-MM-dd HH:mm:ss";
        }

        return dateTime.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses text as a date/time (invariant culture). With the default empty
    /// format any standard date syntax is accepted (e.g. "2026-07-10 14:30");
    /// a .NET format string (e.g. "dd/MM/yyyy") makes the parse exact.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="format">Optional exact .NET date format string.</param>
    /// <returns>The parsed date/time.</returns>
    [NodeName("DateTime.Parse")]
    [return: NodeName("dateTime")]
    [NodeDescription("Parses text as a date/time, optionally with an exact .NET format string.")]
    [NodeSearchTags("fromstring", "convert", "date", "time")]
    public static DateTime Parse(string text, string format = "")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("DateTime.Parse requires a date/time string such as \"2026-07-10 14:30\".", nameof(text));
        }

        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(format))
        {
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            throw new FormatException(
                "DateTime.Parse cannot parse '" + text + "'. Use an ISO-style date such as \"2026-07-10 14:30\" " +
                "or supply an exact format string (e.g. \"dd/MM/yyyy\").");
        }

        if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        throw new FormatException("DateTime.Parse cannot parse '" + text + "' with the format '" + format + "'.");
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
    /// (0.5 = 12 hours) and negative values move backwards in time.
    /// </summary>
    /// <param name="dateTime">The date/time to offset.</param>
    /// <param name="days">Days to add (fractional and negative values allowed).</param>
    /// <returns>The offset date/time.</returns>
    [NodeName("DateTime.AddDays")]
    [return: NodeName("dateTime")]
    [NodeDescription("Offsets a date/time by a number of days (fractional and negative values allowed).")]
    [NodeSearchTags("offset", "shift", "schedule", "date")]
    public static DateTime AddDays(DateTime dateTime, double days)
    {
        return dateTime.AddDays(days);
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
    [NodeDescription("Returns the signed number of days between two date/times (end minus start).")]
    [NodeSearchTags("difference", "duration", "span", "elapsed")]
    public static double DaysBetween(DateTime start, DateTime end)
    {
        return (end - start).TotalDays;
    }
}
