using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Dyncamelo.Core.Types;

/// <summary>A short, bounded description of a value for tooltips: what it is, how many items, and the first few of them.</summary>
public static class ValueSummary
{
    /// <summary>"42", "\"text\"", "3 items: [1, 2, 3]", "120 items: [1, 2, 3, … 117 more]", "no value yet".</summary>
    /// <param name="value">The value to describe (a socket's current value).</param>
    /// <param name="maxItems">How many list items to spell out.</param>
    /// <param name="maxLength">Longest text returned for any one item or scalar.</param>
    public static string Describe(object? value, int maxItems = 4, int maxLength = 60)
    {
        if (value == null)
        {
            return "no value yet";
        }

        if (value is string text)
        {
            return "\"" + Shorten(text, maxLength) + "\"";
        }

        if (value is IDictionary dictionary)
        {
            return Plural(dictionary.Count, "entry", "entries") + ": " + Shorten(TypeCoercion.FormatValue(value), maxLength);
        }

        if (value is IEnumerable enumerable)
        {
            var shown = new List<string>();
            var total = 0;
            foreach (var item in enumerable)
            {
                if (total < maxItems)
                {
                    shown.Add(Shorten(TypeCoercion.FormatValue(item), maxLength));
                }

                total++;
            }

            var inside = string.Join(", ", shown);
            if (total > shown.Count)
            {
                inside += ", … " + (total - shown.Count).ToString(CultureInfo.InvariantCulture) + " more";
            }

            return Plural(total, "item", "items") + (total == 0 ? string.Empty : ": [" + inside + "]");
        }

        return Shorten(TypeCoercion.FormatValue(value), maxLength);
    }

    private static string Plural(int count, string one, string many) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

    private static string Shorten(string text, int maxLength)
    {
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= maxLength ? text : text.Substring(0, Math.Max(1, maxLength - 1)) + "…";
    }
}
