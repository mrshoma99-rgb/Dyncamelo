using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace CamelGraph.Core.Types;

/// <summary>
/// Turns a value into display text with a limit, for the Watch nodes and the Script Player. A model easily gives a list of a
/// hundred thousand elements; <see cref="TypeCoercion.FormatValue"/> would build a text of tens of megabytes for a text box to
/// lay out. Here the first items are written and the rest is counted ("… 99,000 more").
/// </summary>
public static class ValueText
{
    /// <summary>Longest text shown for a single string value (the rest is counted).</summary>
    public const int MaxStringLength = 50000;

    /// <summary>
    /// Formats <paramref name="value"/> like <see cref="TypeCoercion.FormatValue"/> (nested lists as "[a, b, [c]]", dictionaries as
    /// "{key : value}", numbers in invariant culture) but writes at most <paramref name="maxItems"/> items in all; what is left out is
    /// summarised as "… N more" at the end of the list it was cut from.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="maxItems">How many items (leaves of a nested list, entries of a dictionary) to write at most.</param>
    public static string Format(object? value, int maxItems)
    {
        var budget = new Budget { Left = maxItems < 0 ? 0 : maxItems };
        var builder = new StringBuilder();
        Write(builder, value, budget, top: true);
        return builder.ToString();
    }

    /// <summary>"1,234" in invariant culture.</summary>
    /// <param name="count">The number.</param>
    public static string Count(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    private sealed class Budget
    {
        public int Left;
    }

    private static void Write(StringBuilder builder, object? value, Budget budget, bool top)
    {
        if (value == null)
        {
            budget.Left--;
            builder.Append("null");
            return;
        }

        if (value is string text)
        {
            budget.Left--;
            if (text.Length > MaxStringLength && top)
            {
                builder.Append(text, 0, MaxStringLength).Append("… ").Append(Count(text.Length - MaxStringLength)).Append(" more characters");
            }
            else
            {
                builder.Append(text);
            }

            return;
        }

        if (value is IDictionary dictionary)
        {
            builder.Append('{');
            var first = true;
            long skipped = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (budget.Left <= 0)
                {
                    skipped++;
                    continue;
                }

                if (!first)
                {
                    builder.Append(", ");
                }

                first = false;
                Write(builder, entry.Key, new Budget { Left = 1 }, top: false);
                builder.Append(" : ");
                Write(builder, entry.Value, budget, top: false);
            }

            AppendMore(builder, skipped, first);
            if (first && skipped == 0)
            {
                budget.Left--;      // an empty container still takes room
            }

            builder.Append('}');
            return;
        }

        if (value is IEnumerable enumerable)
        {
            builder.Append('[');
            var first = true;
            long skipped = 0;
            foreach (var item in enumerable)
            {
                if (budget.Left <= 0)
                {
                    skipped++;
                    continue;
                }

                if (!first)
                {
                    builder.Append(", ");
                }

                first = false;
                Write(builder, item, budget, top: false);
            }

            AppendMore(builder, skipped, first);
            if (first && skipped == 0)
            {
                budget.Left--;      // an empty container still takes room
            }

            builder.Append(']');
            return;
        }

        budget.Left--;
        if (value is IFormattable formattable)
        {
            builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append(value.ToString() ?? string.Empty);
        }
    }

    private static void AppendMore(StringBuilder builder, long skipped, bool nothingWritten)
    {
        if (skipped > 0)
        {
            builder.Append(nothingWritten ? string.Empty : ", ").Append("… ").Append(Count(skipped)).Append(" more");
        }
    }
}
