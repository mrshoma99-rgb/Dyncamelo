using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// List arithmetic and tallies: sums, averages, spread, counts per value, histograms, filters by value, and a few list
/// reshaping helpers. Numbers are read from any numeric type, or text that looks like a number; nulls and blank text are
/// skipped. A list with no numbers gives an empty result and a warning (a sum is 0 and a product 1), never an error.
/// </summary>
[NodeCategory("List")]
public static class ListStatsNodes
{
    /// <summary>Adds up the numbers of a list.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped; text such as "12.5" is read as a number).</param>
    /// <returns>The total; 0 for an empty list.</returns>
    [NodeName("List.Sum")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("sum")]
    [NodeDescription("Adds up the numbers of a list. Nulls and blank text (an empty cell) are skipped, text such as \"12.5\" is read as a number, and an empty list gives 0. A list inside the list is an error: set the input to @L2 for one total per sublist.")]
    [NodeSearchTags("total", "add", "aggregate", "quantity", "qto", "sum")]
    public static double Sum([MultiInput] IList<object?> list)
    {
        var total = 0d;
        foreach (var value in Numbers(list, "List.Sum"))
        {
            total += value;
        }

        return total;
    }

    /// <summary>Multiplies the numbers of a list.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped).</param>
    /// <returns>The product; 1 for an empty list.</returns>
    [NodeName("List.Product")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("product")]
    [NodeDescription("Multiplies the numbers of a list. Nulls and blank text are skipped and an empty list gives 1. A list inside the list is an error: set the input to @L2 for one product per sublist.")]
    [NodeSearchTags("multiply", "aggregate", "times")]
    public static double Product([MultiInput] IList<object?> list)
    {
        var product = 1d;
        foreach (var value in Numbers(list, "List.Product"))
        {
            product *= value;
        }

        return product;
    }

    /// <summary>The mean of the numbers of a list.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped).</param>
    /// <returns>The arithmetic mean; empty (with a warning) when the list has no numbers.</returns>
    [NodeName("List.Average")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("average")]
    [NodeDescription("The arithmetic mean of the numbers of a list. Nulls and blank text are skipped; with no numbers at all (an empty list) the result is empty and the node shows a warning instead of an error. A list inside the list is an error: set the input to @L2 for one average per sublist.")]
    [NodeSearchTags("mean", "aggregate", "avg", "kpi")]
    public static double? Average([MultiInput] IList<object?> list)
    {
        var values = Numbers(list, "List.Average");
        if (values.Count == 0)
        {
            return WarnNoNumbers("there is no average");
        }

        return values.Sum() / values.Count;
    }

    /// <summary>The middle value of the numbers of a list.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped).</param>
    /// <returns>The median (the mean of the two middle values when the count is even); empty (with a warning) when the list has no numbers.</returns>
    [NodeName("List.Median")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("median")]
    [NodeDescription("The middle value of the numbers of a list (the mean of the two middle ones when the count is even); the same as List.Percentile at 50. Nulls and blank text are skipped; with no numbers at all the result is empty and the node shows a warning instead of an error.")]
    [NodeSearchTags("middle", "aggregate", "typical")]
    public static double? Median([MultiInput] IList<object?> list)
    {
        var values = Numbers(list, "List.Median");
        if (values.Count == 0)
        {
            return WarnNoNumbers("there is no median");
        }

        return PercentileOf(values, 50d);
    }

    /// <summary>A percentile of the numbers of a list.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped).</param>
    /// <param name="percent">Which percentile, 0 to 100 (50 is the median, 90 the value 90 % of the list lies below).</param>
    /// <returns>The value at that percentile, interpolated linearly (like Excel's PERCENTILE.INC); empty (with a warning) when the list has no numbers.</returns>
    [NodeName("List.Percentile")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("value")]
    [NodeDescription("The value below which a given percentage of the numbers lie (linear interpolation, like Excel's PERCENTILE.INC); 50 is the median, 0 the smallest and 100 the largest number. Nulls and blank text are skipped; with no numbers at all the result is empty and the node shows a warning instead of an error.")]
    [NodeSearchTags("quantile", "quartile", "aggregate", "percent", "median")]
    public static double? Percentile([MultiInput] IList<object?> list, [NodeRange(0, 100)] double percent = 90d)
    {
        if (double.IsNaN(percent) || percent < 0d || percent > 100d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent), "List.Percentile needs a percentage from 0 to 100; got " + percent.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var values = Numbers(list, "List.Percentile");
        if (values.Count == 0)
        {
            return WarnNoNumbers("there is no percentile");
        }

        return PercentileOf(values, percent);
    }

    /// <summary>How spread out the numbers of a list are.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped); two are needed for a sample.</param>
    /// <param name="sample">True divides by n-1 (a sample of a larger population); false (default) divides by n.</param>
    /// <returns>The standard deviation; empty (with a warning) when there are not enough numbers.</returns>
    [NodeName("List.StandardDeviation")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("standardDeviation")]
    [NodeDescription("The standard deviation of the numbers of a list (population by default; tick 'sample' to divide by n-1, which needs at least two numbers). Nulls and blank text are skipped; with too few numbers the result is empty and the node shows a warning instead of an error.")]
    [NodeSearchTags("spread", "variance", "deviation", "stdev", "statistics")]
    public static double? StandardDeviation([MultiInput] IList<object?> list, bool sample = false)
    {
        var values = Numbers(list, "List.StandardDeviation");
        if (values.Count == 0)
        {
            return WarnNoNumbers("there is no standard deviation");
        }

        if (sample && values.Count < 2)
        {
            NodeWarnings.Add("The 'sample' option needs at least two numbers, and the 'list' input has one, so there is no standard deviation. The result is empty.");
            return null;
        }

        var mean = values.Sum() / values.Count;
        var squares = 0d;
        foreach (var value in values)
        {
            squares += (value - mean) * (value - mean);
        }

        return Math.Sqrt(squares / (sample ? values.Count - 1 : values.Count));
    }

    /// <summary>The usual summary figures of a list of numbers in one go.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped).</param>
    /// <returns>count, sum, min, max, average, median and standardDeviation; everything but count and sum is empty for no numbers.</returns>
    [NodeName("List.Statistics")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("count", "sum", "min", "max", "average", "median", "standardDeviation")]
    [PortKinds("integer", "number", "number", "number", "number", "number", "number")]
    [NodeDescription("Count, sum, minimum, maximum, average, median and standard deviation of a list of numbers in one node. Nulls and blank text are skipped and not counted; with no numbers at all the count is 0, the sum is 0, the rest is empty and the node shows a warning. Text that reads as a number is compared as a number here (List.MaximumItem compares text as text).")]
    [NodeSearchTags("summary", "describe", "aggregate", "kpi", "qto", "min", "max", "statistics")]
    public static Dictionary<string, object> Statistics([MultiInput] IList<object?> list)
    {
        var values = Numbers(list, "List.Statistics");
        var result = new Dictionary<string, object>
        {
            ["count"] = values.Count,
            ["sum"] = values.Sum(),
            ["min"] = null!,
            ["max"] = null!,
            ["average"] = null!,
            ["median"] = null!,
            ["standardDeviation"] = null!,
        };
        if (values.Count == 0)
        {
            WarnNoNumbers("the minimum, maximum, average, median and standard deviation are empty");
            return result;
        }

        var mean = values.Sum() / values.Count;
        var squares = values.Sum(v => (v - mean) * (v - mean));
        result["min"] = values.Min();
        result["max"] = values.Max();
        result["average"] = mean;
        result["median"] = PercentileOf(values, 50d);
        result["standardDeviation"] = Math.Sqrt(squares / values.Count);
        return result;
    }

    /// <summary>Adds a running total along a list of numbers.</summary>
    /// <param name="list">The numbers (an empty item, null or blank text, is an error here, so positions stay aligned).</param>
    /// <returns>A list as long as the input: each item is the sum of everything up to and including it.</returns>
    [NodeName("List.CumulativeSum")]
    [NodeCategory("List.Statistics")]
    [return: NodeName("totals")]
    [NodeDescription("A running total: each item is the sum of the list up to and including that position. An empty item (null or blank text) is an error, so the totals stay lined up with the list; remove or fill empty items first (List.FilterByValue with the notEmpty test, or List.ReplaceNulls). A list inside the list is an error: set the input to @L2 for one running total per sublist.")]
    [NodeSearchTags("running", "accumulate", "progress", "s-curve")]
    public static IList<double> CumulativeSum(IList<object?> list)
    {
        Require(list, "List.CumulativeSum");
        var totals = new List<double>(list.Count);
        var running = 0d;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsBlank(list[i]))
            {
                throw new ArgumentException(
                    "List.CumulativeSum: item " + i.ToString(CultureInfo.InvariantCulture) +
                    " is empty; remove or fill empty items first (List.FilterByValue with the notEmpty test, or List.ReplaceNulls).");
            }

            running += ToNumber(list[i], i, "List.CumulativeSum");
            totals.Add(running);
        }

        return totals;
    }

    /// <summary>Counts how many times each distinct value occurs.</summary>
    /// <param name="list">The values to tally (nulls are counted as one value, shown empty).</param>
    /// <returns>The distinct values in order of first appearance and how many times each occurs.</returns>
    [NodeName("List.CountValues")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("values", "counts")]
    [PortKinds("", "integer*")]
    [NodeDescription("Tallies a list: each distinct value, in order of first appearance, with the number of times it occurs. Numbers compare by value, text with its case, and lists and dictionaries by their content. (It tallies the values themselves; List.GroupByKey groups items by a separate list of keys.)")]
    [NodeSearchTags("tally", "frequency", "distribution", "group", "count", "how many of each", "countby", "count by")]
    public static Dictionary<string, object> CountBy(IList<object?> list) => Tally(list, "List.CountValues");

    private static Dictionary<string, object> Tally(IList<object?> list, string nodeName)
    {
        Require(list, nodeName);
        var values = new List<object?>();
        var counts = new List<object?>();
        var index = new Dictionary<object, int>(new ValueEquality());
        var nullIndex = -1;
        foreach (var item in list)
        {
            int position;
            if (item == null)
            {
                if (nullIndex < 0)
                {
                    nullIndex = values.Count;
                    values.Add(null);
                    counts.Add(0);
                }

                position = nullIndex;
            }
            else if (!index.TryGetValue(item, out position))
            {
                position = values.Count;
                index[item] = position;
                values.Add(item);
                counts.Add(0);
            }

            counts[position] = (int)counts[position]! + 1;
        }

        return new Dictionary<string, object> { ["values"] = values, ["counts"] = counts };
    }

    /// <summary>Finds the values that occur more than once.</summary>
    /// <param name="list">The values to check.</param>
    /// <returns>The repeated values (first appearance order) and how many times each occurs.</returns>
    [NodeName("List.Duplicates")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("duplicates", "counts")]
    [PortKinds("", "integer*")]
    [NodeDescription("The values that occur more than once, with how many times each occurs — duplicate GUIDs, marks or names. Values compare as in List.CountValues (text with its case); nulls are not duplicates.")]
    [NodeSearchTags("duplicate", "repeated", "twice", "unique", "check", "audit")]
    public static Dictionary<string, object> Duplicates(IList<object?> list)
    {
        var tally = Tally(list, "List.Duplicates");
        var values = (List<object?>)tally["values"];
        var counts = (List<object?>)tally["counts"];
        var duplicates = new List<object?>();
        var duplicateCounts = new List<object?>();
        for (int i = 0; i < values.Count; i++)
        {
            if ((int)counts[i]! > 1 && values[i] != null)
            {
                duplicates.Add(values[i]);
                duplicateCounts.Add(counts[i]);
            }
        }

        return new Dictionary<string, object> { ["duplicates"] = duplicates, ["counts"] = duplicateCounts };
    }

    /// <summary>The value that occurs most often.</summary>
    /// <param name="list">The values to check (at least one non-null).</param>
    /// <returns>The most common value (the first to appear wins a tie) and how many times it occurs.</returns>
    [NodeName("List.MostCommon")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("item", "count")]
    [PortKinds("", "integer")]
    [NodeDescription("The value that occurs most often in a list (the earliest wins a tie) and how many times. Values compare as in List.CountValues (text with its case); nulls are not counted. A list with nothing but nulls is an error.")]
    [NodeSearchTags("mode", "frequent", "popular", "typical")]
    public static Dictionary<string, object> MostCommon(IList<object?> list)
    {
        var tally = Tally(list, "List.MostCommon");
        var values = (List<object?>)tally["values"];
        var counts = (List<object?>)tally["counts"];
        var best = -1;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] != null && (best < 0 || (int)counts[i]! > (int)counts[best]!))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            throw new InvalidOperationException("List.MostCommon needs at least one item that is not null.");
        }

        return new Dictionary<string, object> { ["item"] = values[best]!, ["count"] = counts[best]! };
    }

    /// <summary>Groups numbers into equal-width bins and counts them.</summary>
    /// <param name="list">The numbers (nulls and blank text are skipped).</param>
    /// <param name="bins">How many bins, from the smallest to the largest value.</param>
    /// <returns>For each bin: its lower edge, upper edge, count and a text label; empty lists (with a warning) when there are no numbers.</returns>
    [NodeName("List.Histogram")]
    [NodeCategory("List.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("lower", "upper", "counts", "labels")]
    [PortKinds("number*", "number*", "integer*", "text*")]
    [NodeDescription("Splits the range of the numbers into equal bins and counts how many fall in each — the data behind a distribution chart. Nulls and blank text are skipped; with no numbers at all the four lists are empty and the node shows a warning instead of an error.")]
    [NodeSearchTags("distribution", "bins", "buckets", "chart", "frequency", "range")]
    public static Dictionary<string, object> Histogram([MultiInput] IList<object?> list, [NodeRange(1, 1000)] int bins = 10)
    {
        if (bins < 1 || bins > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(bins), "List.Histogram makes 1 to 1000 bins; got " + bins.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var values = Numbers(list, "List.Histogram");
        if (values.Count == 0)
        {
            WarnNoNumbers("there are no bins to count");
            return new Dictionary<string, object>
            {
                ["lower"] = new List<object?>(),
                ["upper"] = new List<object?>(),
                ["counts"] = new List<object?>(),
                ["labels"] = new List<object?>(),
            };
        }

        var min = values.Min();
        var max = values.Max();
        var width = (max - min) / bins;
        var lower = new List<object?>(bins);
        var upper = new List<object?>(bins);
        var labels = new List<object?>(bins);
        var counts = new int[bins];
        foreach (var value in values)
        {
            var bin = width <= 0d ? 0 : (int)Math.Floor((value - min) / width);
            counts[Math.Min(bin, bins - 1)]++;
        }

        for (int i = 0; i < bins; i++)
        {
            var from = min + width * i;
            var to = i == bins - 1 ? max : min + width * (i + 1);
            lower.Add(from);
            upper.Add(to);
            labels.Add(from.ToString("0.##", CultureInfo.InvariantCulture) + " – " + to.ToString("0.##", CultureInfo.InvariantCulture));
        }

        return new Dictionary<string, object>
        {
            ["lower"] = lower,
            ["upper"] = upper,
            ["counts"] = counts.Select(c => (object?)c).ToList(),
            ["labels"] = labels,
        };
    }

    /// <summary>Keeps the items that pass a test, in one node instead of compare, mask and filter.</summary>
    /// <param name="list">The items to filter.</param>
    /// <param name="test">The test: ==, !=, &gt;, &gt;=, &lt;, &lt;=, contains, !contains, startsWith, endsWith, matches (wildcards * and ?), regex, isNull, notNull, isEmpty, notEmpty, in, notIn.</param>
    /// <param name="value">What to test against (not used by the isNull / notNull / isEmpty / notEmpty tests). For in and notIn a list of values, or a text with the values separated by commas or semicolons.</param>
    /// <param name="keys">Optional: a list of the same length whose values are tested instead of the items themselves (filter model items by a property value).</param>
    /// <param name="ignoreCase">True (default) ignores upper/lower case in text tests.</param>
    /// <returns>The items that passed, the items that did not, and the true/false mask.</returns>
    [NodeName("List.FilterByValue")]
    [MultiReturn("matched", "rejected", "mask")]
    [PortKinds("", "", "boolean*")]
    [NodeDescription("Keeps the items that pass a test such as > 100, contains \"wall\", matches \"A-*\" or in [\"L01\", \"L02\"] — optionally testing a parallel list of keys instead; returns the matches, the rest and the mask. The tests > >= < <= never pass an item that has no value (null or blank text) or one that cannot be compared with the value (text against a number; the node then warns and names the first). The test in keeps the items equal to any of the values in a list (or in a text with the values separated by commas or semicolons); notIn keeps the others. Text tests ignore case unless ignoreCase is off; regex needs a pattern of its own and stops after 2 seconds.")]
    [NodeSearchTags("filter", "where", "select", "keep", "compare", "mask", "search", "query", "in", "any of", "one of", "membership")]
    public static Dictionary<string, object> FilterByValue(
        IList<object?> list,
        [NodeChoices("==", "!=", ">", ">=", "<", "<=", "contains", "!contains", "startsWith", "endsWith", "matches", "regex", "isNull", "notNull", "isEmpty", "notEmpty", "in", "notIn")]
        string test = "==",
        object? value = null,
        IList<object?>? keys = null,
        bool ignoreCase = true)
    {
        Require(list, "List.FilterByValue");
        if (keys != null && keys.Count != list.Count)
        {
            throw new ArgumentException(
                "List.FilterByValue: the list has " + list.Count.ToString(CultureInfo.InvariantCulture) + " item(s) but 'keys' has " +
                keys.Count.ToString(CultureInfo.InvariantCulture) + "; they must be the same length.");
        }

        var membership = ListMembership.TryParse(test, out var negate);
        var op = membership ? string.Empty : ValueTests.Normalize(test, "List.FilterByValue");
        var ordering = op == ">" || op == ">=" || op == "<" || op == "<=";
        var members = membership ? ListMembership.Members(value, ignoreCase) : null;
        var valueHasNoValue = ordering && IsBlank(value);
        var uncomparable = 0;
        var firstUncomparable = -1;

        var matched = new List<object?>();
        var rejected = new List<object?>();
        var mask = new List<object?>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var subject = keys != null ? keys[i] : list[i];
            bool passed;
            if (membership)
            {
                passed = members!.Contains(subject) != negate;
            }
            else if (ordering && (valueHasNoValue || IsBlank(subject)))
            {
                // No value is not smaller or larger than anything.
                passed = false;
            }
            else
            {
                try
                {
                    passed = ValueTests.Test(op, subject, value, ignoreCase, "List.FilterByValue");
                }
                catch (InvalidOperationException) when (ordering)
                {
                    // Text against a number (or two values of unrelated types): they have no order, so the item does not pass.
                    passed = false;
                    if (uncomparable++ == 0)
                    {
                        firstUncomparable = i;
                    }
                }
                catch (RegexMatchTimeoutException ex)
                {
                    throw new InvalidOperationException(
                        "List.FilterByValue: matching the pattern '" + TypeCoercion.FormatValue(value) + "' on item " + i.ToString(CultureInfo.InvariantCulture) +
                        " took longer than 2 seconds and was stopped. Simplify the pattern (avoid nested repeats such as '(a+)+') or match a shorter text.",
                        ex);
                }
            }

            mask.Add(passed);
            (passed ? matched : rejected).Add(list[i]);
        }

        if (uncomparable > 0)
        {
            var first = keys != null ? keys[firstUncomparable] : list[firstUncomparable];
            NodeWarnings.Add(
                uncomparable.ToString(CultureInfo.InvariantCulture) + " of " + list.Count.ToString(CultureInfo.InvariantCulture) +
                " items cannot be compared with '" + TypeCoercion.FormatValue(value) + "' (the first is item " + firstUncomparable.ToString(CultureInfo.InvariantCulture) +
                ", '" + TypeCoercion.FormatValue(first) + "') and did not pass the " + op + " test.");
        }

        return new Dictionary<string, object> { ["matched"] = matched, ["rejected"] = rejected, ["mask"] = mask };
    }

    /// <summary>Pairs each item with its position.</summary>
    /// <param name="list">The list.</param>
    /// <returns>A list of [index, item] pairs, indexes starting at 0.</returns>
    [NodeName("List.WithIndex")]
    [return: NodeName("pairs")]
    [NodeDescription("Pairs each item with its position: [[0, item0], [1, item1], …].")]
    [NodeSearchTags("enumerate", "index", "number", "position", "row number")]
    public static IList<object?> WithIndex(IList<object?> list)
    {
        Require(list, "List.WithIndex");
        var pairs = new List<object?>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            pairs.Add(new List<object?> { i, list[i] });
        }

        return pairs;
    }

    /// <summary>Pairs the items of two lists by position.</summary>
    /// <param name="first">The first list.</param>
    /// <param name="second">The second list.</param>
    /// <returns>[first0, second0], [first1, second1], … as long as the shorter list.</returns>
    [NodeName("List.Zip")]
    [return: NodeName("pairs")]
    [NodeDescription("Pairs two lists by position: [[a0, b0], [a1, b1], …], as long as the shorter list. When the lists differ in length the extra items of the longer one are left out and the node shows a warning that says how many.")]
    [NodeSearchTags("pair", "combine", "merge", "interleave", "together")]
    public static IList<object?> Zip(IList<object?> first, IList<object?> second)
    {
        Require(first, "List.Zip");
        if (second == null)
        {
            throw new ArgumentNullException(nameof(second), "List.Zip requires two lists. Wire a list into the 'second' input.");
        }

        var count = Math.Min(first.Count, second.Count);
        if (first.Count != second.Count)
        {
            var longer = first.Count > second.Count ? "first" : "second";
            NodeWarnings.Add(
                "The 'first' list has " + first.Count.ToString(CultureInfo.InvariantCulture) + " items and the 'second' has " + second.Count.ToString(CultureInfo.InvariantCulture) +
                ", so the pairs stop at " + count.ToString(CultureInfo.InvariantCulture) + " and the last " + Math.Abs(first.Count - second.Count).ToString(CultureInfo.InvariantCulture) +
                " item(s) of the '" + longer + "' list are left out.");
        }

        var pairs = new List<object?>(count);
        for (int i = 0; i < count; i++)
        {
            pairs.Add(new List<object?> { first[i], second[i] });
        }

        return pairs;
    }

    /// <summary>Pairs each item with the next one.</summary>
    /// <param name="list">The list.</param>
    /// <param name="cyclic">True also pairs the last item with the first.</param>
    /// <returns>[item0, item1], [item1, item2], … (one pair fewer than items, or as many with 'cyclic').</returns>
    [NodeName("List.Pairs")]
    [return: NodeName("pairs")]
    [NodeDescription("Pairs each item with the next one: [[a, b], [b, c], …] — consecutive points, segments, steps.")]
    [NodeSearchTags("neighbours", "consecutive", "segments", "window", "adjacent")]
    public static IList<object?> Pairs(IList<object?> list, bool cyclic = false)
    {
        Require(list, "List.Pairs");
        var pairs = new List<object?>();
        for (int i = 0; i + 1 < list.Count; i++)
        {
            pairs.Add(new List<object?> { list[i], list[i + 1] });
        }

        if (cyclic && list.Count > 1)
        {
            pairs.Add(new List<object?> { list[list.Count - 1], list[0] });
        }

        return pairs;
    }

    /// <summary>Sorts a list from largest to smallest.</summary>
    /// <param name="list">The list to sort.</param>
    /// <returns>A new list, largest first, by the same ordering rule as List.Sort.</returns>
    [NodeName("List.SortDescending")]
    [NodeDeprecated("List.Sort with 'descending' ticked")]
    [return: NodeName("list")]
    [NodeDescription("Returns the list sorted descending (largest first; text alphabetically from Z), by the same rule as List.Sort.")]
    [NodeSearchTags("order", "descending", "reverse", "biggest first", "top")]
    public static IList<object?> SortDescending(IList<object?> list) => ListNodes.SortItems(list, descending: true, "List.SortDescending");

    /// <summary>Puts the items in a pseudo-random order that is the same every time for the same seed.</summary>
    /// <param name="list">The list.</param>
    /// <param name="seed">Same seed, same order; change it for a different order.</param>
    /// <returns>A shuffled copy.</returns>
    [NodeName("List.Shuffle")]
    [return: NodeName("list")]
    [NodeDescription("Shuffles a list; the same seed always gives the same order, so a run can be repeated.")]
    [NodeSearchTags("random", "mix", "sample", "permute")]
    public static IList<object?> Shuffle(IList<object?> list, int seed = 0)
    {
        Require(list, "List.Shuffle");
        var shuffled = new List<object?>(list);
        var random = new Random(seed);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            var swap = shuffled[i];
            shuffled[i] = shuffled[j];
            shuffled[j] = swap;
        }

        return shuffled;
    }

    /// <summary>Takes items from the start while a mask stays true.</summary>
    /// <param name="list">The items.</param>
    /// <param name="mask">One true/false per item (same length).</param>
    /// <returns>The items before the first false.</returns>
    [NodeName("List.TakeWhile")]
    [return: NodeName("list")]
    [NodeDescription("Takes items from the start of the list for as long as the mask is true (stops at the first false). The mask is read as in List.FilterByBoolMask: one true or false per item, a null counts as false, and a list or a value that is not true or false is an error.")]
    [NodeSearchTags("head", "until", "leading", "prefix")]
    public static IList<object?> TakeWhile(IList<object?> list, IList<object?> mask)
    {
        var stop = FirstFalse(list, mask, "List.TakeWhile");
        return list.Take(stop).ToList();
    }

    /// <summary>Drops items from the start while a mask stays true.</summary>
    /// <param name="list">The items.</param>
    /// <param name="mask">One true/false per item (same length).</param>
    /// <returns>The items from the first false on.</returns>
    [NodeName("List.DropWhile")]
    [return: NodeName("list")]
    [NodeDescription("Drops items from the start of the list for as long as the mask is true; keeps everything from the first false. The mask is read as in List.FilterByBoolMask: one true or false per item, a null counts as false, and a list or a value that is not true or false is an error.")]
    [NodeSearchTags("skip", "leading", "tail", "prefix")]
    public static IList<object?> DropWhile(IList<object?> list, IList<object?> mask)
    {
        var stop = FirstFalse(list, mask, "List.DropWhile");
        return list.Skip(stop).ToList();
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private static int FirstFalse(IList<object?> list, IList<object?> mask, string nodeName)
    {
        Require(list, nodeName);
        if (mask == null)
        {
            throw new ArgumentNullException(nameof(mask), nodeName + " requires a mask of true/false values. Wire one into the 'mask' input.");
        }

        if (mask.Count != list.Count)
        {
            throw new ArgumentException(
                nodeName + ": the list has " + list.Count.ToString(CultureInfo.InvariantCulture) + " item(s) but the mask has " +
                mask.Count.ToString(CultureInfo.InvariantCulture) + "; they must be the same length.");
        }

        for (int i = 0; i < mask.Count; i++)
        {
            if (!ListNodes.ReadSplitMask(mask[i], i, nodeName))
            {
                return i;
            }
        }

        return mask.Count;
    }

    private static void Require(IList<object?>? list, string nodeName)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list), nodeName + " requires a list. Wire a list into the 'list' input.");
        }
    }

    /// <summary>Says on the node that the list had no numbers and what follows from it; returns null so a caller can return it as its empty result.</summary>
    private static double? WarnNoNumbers(string consequence)
    {
        NodeWarnings.Add("The 'list' input has no numbers (it is empty, or holds only nulls and blank text), so " + consequence + ".");
        return null;
    }

    /// <summary>True for an item with no value in a column of numbers: null, or text that is empty or only spaces.</summary>
    private static bool IsBlank(object? item) => item == null || (item is string text && text.Trim().Length == 0);

    private static List<double> Numbers(IList<object?> list, string nodeName)
    {
        Require(list, nodeName);
        var numbers = new List<double>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            if (!IsBlank(list[i]))
            {
                numbers.Add(ToNumber(list[i], i, nodeName));
            }
        }

        return numbers;
    }

    private static double ToNumber(object? item, int index, string nodeName)
    {
        if (item is IList && !(item is string))
        {
            throw new ArgumentException(
                nodeName + ": item " + index.ToString(CultureInfo.InvariantCulture) + " is a list, not a number. " +
                "For one result per sublist set this input to @L2 (right-click, List Levels); for one result for everything flatten the list first (List.Flatten).");
        }

        if (item != null && ValueComparison.IsNumeric(item))
        {
            return ValueComparison.ToDouble(item);
        }

        if (item is string text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (TypeCoercion.TryCoerce(item, typeof(double), out var coerced) && coerced is double number)
        {
            return number;
        }

        throw new ArgumentException(
            nodeName + ": item " + index.ToString(CultureInfo.InvariantCulture) + " ('" + TypeCoercion.FormatValue(item) + "') is not a number.");
    }

    // Linear interpolation between closest ranks (Excel PERCENTILE.INC).
    private static double PercentileOf(List<double> values, double percent)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var rank = percent / 100d * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }

    private sealed class ValueEquality : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => ValueComparison.AreEqual(x, y);

        public int GetHashCode(object obj) => ValueComparison.GetValueHashCode(obj);
    }

    private sealed class ValueOrder : IComparer<object?>
    {
        public int Compare(object? x, object? y) => ValueComparison.Compare(x, y);
    }
}
