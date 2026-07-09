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
}
