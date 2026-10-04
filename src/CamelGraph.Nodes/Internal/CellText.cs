using System;
using System.Globalization;
using CamelGraph.Core.Types;

namespace CamelGraph.Nodes;

/// <summary>How a spreadsheet or CSV cell reads as text.</summary>
internal static class CellText
{
    /// <summary>Empty for null, the text itself for a string, otherwise the value as CamelGraph shows it everywhere.</summary>
    /// <param name="cell">The cell value.</param>
    internal static string Format(object? cell)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        if (cell is string text)
        {
            return text;
        }

        return TypeCoercion.FormatValue(cell);
    }

    /// <summary>
    /// Like <see cref="Format"/> for a cell that is written to a text file: a date is written the one way every program reads the
    /// same (ISO 8601, <c>2026-10-04 14:30:00</c>) instead of the US month-first order that Excel reads wrongly in most countries.
    /// </summary>
    /// <param name="cell">The cell value.</param>
    internal static string FormatForFile(object? cell)
    {
        switch (cell)
        {
            case DateTime moment:
                return Iso(moment);
            case DateTimeOffset offset:
                return Iso(offset.DateTime) + " " + offset.ToString("zzz", CultureInfo.InvariantCulture);
            default:
                return Format(cell);
        }
    }

    /// <summary>A date and time as <c>yyyy-MM-dd HH:mm:ss</c>, with a fraction of a second only when there is one.</summary>
    /// <param name="moment">The date and time.</param>
    internal static string Iso(DateTime moment)
    {
        var text = moment.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
        return moment.Kind == DateTimeKind.Utc ? text + "Z" : text;
    }
}
