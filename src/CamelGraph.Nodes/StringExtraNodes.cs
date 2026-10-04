using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// More text nodes: composite formatting and templates, regular expressions, padding, searching,
/// trimming, case and number formatting. Like <see cref="StringNodes"/>, string inputs replicate over
/// lists of strings. Everything is invariant-culture, so a graph gives the same result on every machine.
/// </summary>
[NodeCategory("String")]
public static class StringExtraNodes
{
    /// <summary>How long one regular-expression operation may run before it is stopped.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The widest padded text the pad nodes will build.</summary>
    private const int MaxPadWidth = 1000000;

    /// <summary>The longest text String.Repeat will build.</summary>
    private const int MaxRepeatLength = 10000000;

    // ── Formatting ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fills a .NET composite format with the wired values: "{0}" is the first value, "{1}" the second, and so on.
    /// A format specifier after a colon formats numbers and dates ("{0:0.00}", "{1:yyyy-MM-dd}"), an alignment after a
    /// comma pads ("{0,8}"), and "{{" / "}}" are literal braces. The invariant culture is always used. Lists and other
    /// values that are neither text nor numbers/dates are shown the way String.FromObject shows them; a missing (null)
    /// value becomes empty text, as in String.Concat. The node makes ONE text per call: everything wired to 'values'
    /// (and the items of a list wired there) fills {0}, {1}, ... of that one text.
    /// </summary>
    /// <param name="format">The composite format text, e.g. "Wall {0} is {1:0.00} m high".</param>
    /// <param name="values">The values for {0}, {1}, ... in the order they are wired. For one text per row, wire a list of rows (a list of lists, for example from Table.Rows) and set this input's List Levels to @L2.</param>
    /// <returns>The formatted text.</returns>
    [NodeName("String.Format")]
    [return: NodeName("text")]
    [NodeDescription("Fills a .NET composite format such as \"{0} is {1:0.00} m\" with the wired values (invariant culture, \"{{\" and \"}}\" are literal braces). " +
        "It makes one text: all the wired values, and the items of a list wired to 'values', fill {0}, {1}, ... of that one text, and a missing value becomes empty text. " +
        "For one text per row, wire the rows (a list of lists, for example Table.Rows) to 'values' and set its List Levels to @L2, or use String.Template with Table.ToDictionaries.")]
    [NodeSearchTags("sprintf", "interpolate", "placeholder", "compose", "message", "text")]
    public static string Format(string format, [MultiInput] IList<object?> values)
    {
        if (format == null)
        {
            throw new ArgumentNullException(nameof(format), "String.Format requires a format text such as \"{0} is {1:0.00} m\". Wire text into the 'format' input.");
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "String.Format requires the values to insert. Wire them into the 'values' input.");
        }

        CheckPlaceholders(format, values.Count);

        var arguments = new object[values.Count];
        for (int i = 0; i < arguments.Length; i++)
        {
            var value = values[i];
            arguments[i] = value == null ? string.Empty : value is string || value is IFormattable ? value : TypeCoercion.FormatValue(value);
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, arguments);
        }
        catch (FormatException ex)
        {
            throw new FormatException(
                "String.Format: the format text '" + format + "' is not valid: " + ex.Message +
                " Use {0}, {1}, ... for the wired values and '{{' / '}}' for literal braces (use String.Template for {name} placeholders).",
                ex);
        }
    }

    /// <summary>
    /// Fills "{name}" placeholders from a dictionary. "{{" and "}}" are literal braces. The value is shown as
    /// String.FromObject shows it (null becomes "null"); names are matched exactly (case-sensitive) after trimming
    /// spaces inside the braces. <paramref name="onMissing"/> decides what a name that is not in the dictionary does.
    /// </summary>
    /// <param name="template">The text with {name} placeholders, e.g. "Level {level}: {count} clashes".</param>
    /// <param name="dictionary">The values by name.</param>
    /// <param name="onMissing">"keep" leaves the placeholder as written, "empty" replaces it with nothing, "error" fails.</param>
    /// <returns>The text with the placeholders replaced.</returns>
    [NodeName("String.Template")]
    [return: NodeName("text")]
    [NodeDescription("Replaces {name} placeholders in a text with the values of a dictionary (\"{{\" and \"}}\" are literal braces).")]
    [NodeSearchTags("placeholder", "mustache", "merge", "interpolate", "fill", "replace", "report", "message")]
    public static string Template(
        string template,
        IDictionary dictionary,
        [NodeChoices("keep", "empty", "error")] string onMissing = "keep")
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template), "String.Template requires a template text such as \"Level {level}\". Wire text into the 'template' input.");
        }

        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary), "String.Template requires a dictionary with the values for the placeholders. Wire one into the 'dictionary' input.");
        }

        var mode = string.IsNullOrWhiteSpace(onMissing) ? "keep" : onMissing.Trim().ToLowerInvariant();
        if (mode != "keep" && mode != "empty" && mode != "error")
        {
            throw new ArgumentException(
                "String.Template: onMissing must be \"keep\", \"empty\" or \"error\" (got '" + onMissing + "').",
                nameof(onMissing));
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            values[TypeCoercion.FormatValue(entry.Key)] = entry.Value;
        }

        var result = new StringBuilder(template.Length + 16);
        for (int i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    result.Append('{');
                    i++;
                    continue;
                }

                var close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    throw new FormatException(
                        "String.Template: the '{' at character " + (i + 1).ToString(CultureInfo.InvariantCulture) +
                        " has no closing '}'. Write '{{' for a literal brace.");
                }

                var written = template.Substring(i, close - i + 1);
                var name = template.Substring(i + 1, close - i - 1).Trim();
                if (values.TryGetValue(name, out var value))
                {
                    result.Append(TypeCoercion.FormatValue(value));
                }
                else if (mode == "keep")
                {
                    result.Append(written);
                }
                else if (mode == "error")
                {
                    throw new ArgumentException(
                        "String.Template: the dictionary has no key '" + name + "' for the placeholder " + written +
                        ". " + DescribeKeys(values.Keys) +
                        " Add the key, fix the name, or set onMissing to \"keep\" or \"empty\".",
                        nameof(dictionary));
                }

                i = close;
            }
            else if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                result.Append('}');
                i++;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats a number as text: a fixed number of decimals, an optional thousands separator (",") and an optional
    /// prefix and suffix, all in the invariant culture (decimal point "."). The prefix goes in front of the whole
    /// number including its minus sign, so -1234.5 with prefix "EUR " gives "EUR -1,234.50". Negative zero shows as 0.
    /// </summary>
    /// <param name="number">The number to format.</param>
    /// <param name="decimals">Digits after the decimal point, 0 to 15 (the number is rounded).</param>
    /// <param name="thousandsSeparator">True to group the digits in thousands with ",".</param>
    /// <param name="prefix">Text placed before the number (and its sign), e.g. "EUR ".</param>
    /// <param name="suffix">Text placed after the number, e.g. " m2".</param>
    /// <returns>The formatted text.</returns>
    [NodeName("String.FromNumber")]
    [NodeAliases("CamelGraph.Nodes.StringExtraNodes.NumberFormat@double,int,bool,string,string")]
    [return: NodeName("text")]
    [NodeDescription("Formats a number as text with fixed decimals, an optional thousands separator and a prefix/suffix (invariant culture). Formerly called Number.Format; String.ToNumber is the way back.")]
    [NodeSearchTags("number.format", "number format", "format number", "number to text", "round", "decimals", "currency", "unit", "thousands", "tostring", "display", "format")]
    public static string FromNumber(
        double number,
        [NodeRange(0, 15)] int decimals = 2,
        bool thousandsSeparator = false,
        string prefix = "",
        string suffix = "")
    {
        if (decimals < 0 || decimals > 15)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimals),
                "String.FromNumber: decimals must be between 0 and 15 (got " + decimals.ToString(CultureInfo.InvariantCulture) + ").");
        }

        var text = number.ToString((thousandsSeparator ? "N" : "F") + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (text.StartsWith("-", StringComparison.Ordinal) && IsAllZeros(text))
        {
            // .NET Framework and .NET Core disagree on "-0.00"; always show plain zero.
            text = text.Substring(1);
        }

        return (prefix ?? string.Empty) + text + (suffix ?? string.Empty);
    }

    // ── Regular expressions ─────────────────────────────────────────────────

    /// <summary>Tests whether a regular expression matches anywhere in a text.</summary>
    /// <param name="text">The text to test.</param>
    /// <param name="pattern">The .NET regular expression, e.g. "^[A-Z]{2}-\d+$".</param>
    /// <param name="ignoreCase">True to ignore upper/lower case (off by default: regular expressions are case-sensitive).</param>
    /// <returns>True when the pattern matches somewhere in the text.</returns>
    [NodeName("String.RegexIsMatch")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isMatch")]
    [NodeDescription("Tests whether a .NET regular expression matches anywhere in a text (use ^ and $ to match the whole text). Case-sensitive unless ignoreCase is switched on (String.Contains and String.IndexOf ignore case by default).")]
    [NodeSearchTags("regex", "regexp", "pattern", "match", "test", "validate", "wildcard")]
    public static bool RegexIsMatch(string text, string pattern, bool ignoreCase = false)
    {
        RequireText("String.RegexIsMatch", text);
        var regex = CreateRegex("String.RegexIsMatch", pattern, ignoreCase);
        return RunRegex("String.RegexIsMatch", pattern, () => regex.IsMatch(text));
    }

    /// <summary>
    /// Finds the first match of a regular expression. The groups are the capture groups 1 to n in the order they
    /// appear in the pattern (an optional group that did not take part gives an empty string). When nothing matches,
    /// found is false, match is empty and groups is an empty list.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="pattern">The .NET regular expression, e.g. "(\d+)-(\w+)".</param>
    /// <param name="ignoreCase">True to ignore upper/lower case (off by default: regular expressions are case-sensitive).</param>
    /// <returns>Dictionary with "found", "match" and "groups".</returns>
    [NodeName("String.RegexMatch")]
    [MultiReturn("found", "match", "groups")]
    [PortKinds("boolean", "text", "text*")]
    [NodeDescription("Finds the first match of a regular expression: whether it was found, the matched text and the capture groups 1..n. Case-sensitive unless ignoreCase is switched on.")]
    [NodeSearchTags("regex", "regexp", "pattern", "capture", "group", "extract", "parse", "find")]
    public static Dictionary<string, object> RegexMatch(string text, string pattern, bool ignoreCase = false)
    {
        RequireText("String.RegexMatch", text);
        var regex = CreateRegex("String.RegexMatch", pattern, ignoreCase);
        var match = RunRegex("String.RegexMatch", pattern, () => regex.Match(text));

        var groups = new List<string>();
        if (match.Success)
        {
            for (int i = 1; i < match.Groups.Count; i++)
            {
                groups.Add(match.Groups[i].Success ? match.Groups[i].Value : string.Empty);
            }
        }

        return new Dictionary<string, object>
        {
            ["found"] = match.Success,
            ["match"] = match.Success ? match.Value : string.Empty,
            ["groups"] = groups,
        };
    }

    /// <summary>Finds every non-overlapping match of a regular expression.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="pattern">The .NET regular expression, e.g. "\d+".</param>
    /// <param name="ignoreCase">True to ignore upper/lower case (off by default: regular expressions are case-sensitive).</param>
    /// <returns>The text of every match, in order (empty when nothing matches).</returns>
    [NodeName("String.RegexMatches")]
    [return: NodeName("matches")]
    [NodeDescription("Returns the text of every match of a regular expression as a list (empty when nothing matches). Case-sensitive unless ignoreCase is switched on.")]
    [NodeSearchTags("regex", "regexp", "pattern", "findall", "extract", "all", "numbers")]
    public static IList<string> RegexMatches(string text, string pattern, bool ignoreCase = false)
    {
        RequireText("String.RegexMatches", text);
        var regex = CreateRegex("String.RegexMatches", pattern, ignoreCase);
        return RunRegex("String.RegexMatches", pattern, () =>
        {
            var found = new List<string>();
            foreach (Match match in regex.Matches(text))
            {
                found.Add(match.Value);
            }

            return (IList<string>)found;
        });
    }

    /// <summary>
    /// Replaces every match of a regular expression. The replacement may refer to capture groups with $1, $2, ...
    /// (or ${name}); write "$$" for a literal dollar sign.
    /// </summary>
    /// <param name="text">The text to change.</param>
    /// <param name="pattern">The .NET regular expression to replace.</param>
    /// <param name="replacement">The replacement text; $1, $2, ... insert capture groups.</param>
    /// <param name="ignoreCase">True to ignore upper/lower case (off by default: regular expressions are case-sensitive).</param>
    /// <returns>The text with every match replaced.</returns>
    [NodeName("String.RegexReplace")]
    [return: NodeName("text")]
    [NodeDescription("Replaces every match of a regular expression; $1, $2, ... in the replacement insert the capture groups. Case-sensitive unless ignoreCase is switched on.")]
    [NodeSearchTags("regex", "regexp", "pattern", "substitute", "rename", "clean", "sub")]
    public static string RegexReplace(string text, string pattern, string replacement, bool ignoreCase = false)
    {
        RequireText("String.RegexReplace", text);
        var regex = CreateRegex("String.RegexReplace", pattern, ignoreCase);
        return RunRegex("String.RegexReplace", pattern, () => regex.Replace(text, replacement ?? string.Empty));
    }

    /// <summary>
    /// Splits a text wherever a regular expression matches. Capture groups in the pattern are kept in the result
    /// (standard .NET behaviour), so use a non-capturing group "(?:...)" to drop them.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <param name="pattern">The .NET regular expression that separates the parts, e.g. "[,;]\s*".</param>
    /// <returns>The parts between the matches.</returns>
    [NodeName("String.RegexSplit")]
    [return: NodeName("list")]
    [NodeDescription("Splits a text into a list of parts wherever a regular expression matches. Case-sensitive; start the pattern with (?i) to ignore case.")]
    [NodeSearchTags("regex", "regexp", "pattern", "tokenize", "divide", "delimiter", "separator")]
    public static IList<string> RegexSplit(string text, string pattern)
    {
        RequireText("String.RegexSplit", text);
        var regex = CreateRegex("String.RegexSplit", pattern, false);
        return RunRegex("String.RegexSplit", pattern, () => (IList<string>)new List<string>(regex.Split(text)));
    }

    // ── Padding, searching, trimming ────────────────────────────────────────

    /// <summary>Pads a text on the left up to a width, e.g. to number "7" as "007".</summary>
    /// <param name="text">The text to pad.</param>
    /// <param name="width">The wanted total length; a longer text is returned unchanged.</param>
    /// <param name="padChar">The padding character: exactly one character (default a space).</param>
    /// <returns>The padded text.</returns>
    [NodeName("String.PadLeft")]
    [return: NodeName("text")]
    [NodeDescription("Pads a text on the left with a character up to a total width (\"7\" becomes \"007\" with width 3 and padChar 0).")]
    [NodeSearchTags("pad", "zero", "leading", "align", "fill", "right-align")]
    public static string PadLeft(string text, int width, string padChar = " ")
    {
        return Pad("String.PadLeft", text, width, padChar, true);
    }

    /// <summary>Pads a text on the right up to a width.</summary>
    /// <param name="text">The text to pad.</param>
    /// <param name="width">The wanted total length; a longer text is returned unchanged.</param>
    /// <param name="padChar">The padding character: exactly one character (default a space).</param>
    /// <returns>The padded text.</returns>
    [NodeName("String.PadRight")]
    [return: NodeName("text")]
    [NodeDescription("Pads a text on the right with a character up to a total width.")]
    [NodeSearchTags("pad", "trailing", "align", "fill", "left-align", "column")]
    public static string PadRight(string text, int width, string padChar = " ")
    {
        return Pad("String.PadRight", text, width, padChar, false);
    }

    /// <summary>
    /// Finds the first occurrence of a text, starting at an index. Ignores upper/lower case (ordinal comparison) unless
    /// ignoreCase is switched off. An empty search text is found at the start index.
    /// </summary>
    /// <param name="text">The text to search in.</param>
    /// <param name="search">The text to look for.</param>
    /// <param name="ignoreCase">True (default) to ignore upper/lower case.</param>
    /// <param name="startIndex">Zero-based index to start searching at (0 to the text's length).</param>
    /// <returns>The zero-based index of the first occurrence, or -1 when it is absent.</returns>
    [NodeName("String.IndexOf")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("index")]
    [NodeDescription("Returns the zero-based index of the first occurrence of a text (-1 when absent). Ignores case unless ignoreCase is switched off.")]
    [NodeSearchTags("find", "position", "locate", "search", "first")]
    public static int IndexOf(string text, string search, bool ignoreCase = true, int startIndex = 0)
    {
        RequireText("String.IndexOf", text);
        RequireSearch("String.IndexOf", search);
        if (startIndex < 0 || startIndex > text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startIndex),
                "String.IndexOf: start index " + startIndex.ToString(CultureInfo.InvariantCulture) +
                " is out of range for a text of " + text.Length.ToString(CultureInfo.InvariantCulture) + " character(s).");
        }

        if (search.Length == 0)
        {
            return startIndex;
        }

        return text.IndexOf(search, startIndex, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds the last occurrence of a text. Ignores upper/lower case (ordinal comparison) unless ignoreCase is switched
    /// off. An empty search text is found at the end of the text.
    /// </summary>
    /// <param name="text">The text to search in.</param>
    /// <param name="search">The text to look for.</param>
    /// <param name="ignoreCase">True (default) to ignore upper/lower case.</param>
    /// <returns>The zero-based index of the last occurrence, or -1 when it is absent.</returns>
    [NodeName("String.LastIndexOf")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("index")]
    [NodeDescription("Returns the zero-based index of the last occurrence of a text (-1 when absent). Ignores case unless ignoreCase is switched off.")]
    [NodeSearchTags("find", "position", "locate", "search", "last", "extension")]
    public static int LastIndexOf(string text, string search, bool ignoreCase = true)
    {
        RequireText("String.LastIndexOf", text);
        RequireSearch("String.LastIndexOf", search);
        if (search.Length == 0)
        {
            return text.Length;
        }

        return text.LastIndexOf(search, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits a text into lines at "\r\n", "\n" and "\r". A line break at the very end of the text does not add an
    /// empty last line, and an empty text has no lines.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <param name="removeEmpty">True to drop lines that are empty (lines holding only spaces are kept).</param>
    /// <returns>The lines, without their line breaks.</returns>
    [NodeName("String.Lines")]
    [return: NodeName("lines")]
    [NodeDescription("Splits a text into a list of lines (handles \\r\\n, \\n and \\r), optionally dropping the empty ones.")]
    [NodeSearchTags("split", "newline", "linebreak", "rows", "paragraph", "multiline")]
    public static IList<string> Lines(string text, bool removeEmpty = false)
    {
        RequireText("String.Lines", text);

        var lines = new List<string>();
        var start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n')
            {
                continue;
            }

            AddLine(lines, text.Substring(start, i - start), removeEmpty);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            AddLine(lines, text.Substring(start), removeEmpty);
        }

        return lines;
    }

    /// <summary>Removes characters from the start of a text.</summary>
    /// <param name="text">The text to trim.</param>
    /// <param name="chars">The characters to remove (each character of this text counts); empty means whitespace.</param>
    /// <returns>The trimmed text.</returns>
    [NodeName("String.TrimStart")]
    [return: NodeName("text")]
    [NodeDescription("Removes whitespace (or the given characters) from the start of a text.")]
    [NodeSearchTags("trim", "strip", "leading", "left", "clean", "whitespace")]
    public static string TrimStart(string text, string chars = "")
    {
        RequireText("String.TrimStart", text);
        return string.IsNullOrEmpty(chars) ? text.TrimStart() : text.TrimStart(chars.ToCharArray());
    }

    /// <summary>Removes characters from the end of a text.</summary>
    /// <param name="text">The text to trim.</param>
    /// <param name="chars">The characters to remove (each character of this text counts); empty means whitespace.</param>
    /// <returns>The trimmed text.</returns>
    [NodeName("String.TrimEnd")]
    [return: NodeName("text")]
    [NodeDescription("Removes whitespace (or the given characters) from the end of a text.")]
    [NodeSearchTags("trim", "strip", "trailing", "right", "clean", "whitespace")]
    public static string TrimEnd(string text, string chars = "")
    {
        RequireText("String.TrimEnd", text);
        return string.IsNullOrEmpty(chars) ? text.TrimEnd() : text.TrimEnd(chars.ToCharArray());
    }

    // ── Case, shape and tests ───────────────────────────────────────────────

    /// <summary>
    /// Capitalises the first letter of every word and lowercases the rest (invariant culture), so "bim COORDINATION"
    /// becomes "Bim Coordination". Acronyms are therefore lowercased too ("HVAC unit" becomes "Hvac Unit").
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>The text in title case.</returns>
    [NodeName("String.ToTitleCase")]
    [return: NodeName("text")]
    [NodeDescription("Capitalises the first letter of every word and lowercases the rest (\"bim COORDINATION\" becomes \"Bim Coordination\").")]
    [NodeSearchTags("capitalize", "capitalise", "proper", "case", "words", "heading")]
    public static string ToTitleCase(string text)
    {
        RequireText("String.ToTitleCase", text);
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
    }

    /// <summary>Repeats a text several times, with an optional separator between the copies.</summary>
    /// <param name="text">The text to repeat.</param>
    /// <param name="count">How many copies, 0 to 10000 (0 gives an empty text).</param>
    /// <param name="separator">Text placed between two copies.</param>
    /// <returns>The repeated text.</returns>
    [NodeName("String.Repeat")]
    [return: NodeName("text")]
    [NodeDescription("Repeats a text a number of times (0 to 10000), optionally with a separator between the copies.")]
    [NodeSearchTags("duplicate", "multiply", "fill", "line", "dashes", "times")]
    public static string Repeat(string text, [NodeRange(0, 10000)] int count, string separator = "")
    {
        RequireText("String.Repeat", text);
        if (count < 0 || count > 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "String.Repeat: count must be between 0 and 10000 (got " + count.ToString(CultureInfo.InvariantCulture) + ").");
        }

        separator ??= string.Empty;
        var length = (long)count * text.Length + (count > 1 ? (long)(count - 1) * separator.Length : 0L);
        if (length > MaxRepeatLength)
        {
            throw new ArgumentException(
                "String.Repeat: the result would be " + length.ToString(CultureInfo.InvariantCulture) +
                " characters long, more than the limit of " + MaxRepeatLength.ToString(CultureInfo.InvariantCulture) +
                ". Use a smaller count or a shorter text.");
        }

        var builder = new StringBuilder((int)length);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(separator);
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    /// <summary>Reverses the characters of a text (a letter with its accent, or a surrogate pair, stays together).</summary>
    /// <param name="text">The text to reverse.</param>
    /// <returns>The reversed text.</returns>
    [NodeName("String.Reverse")]
    [return: NodeName("text")]
    [NodeDescription("Reverses the characters of a text.")]
    [NodeSearchTags("backwards", "mirror", "flip", "invert")]
    public static string Reverse(string text)
    {
        RequireText("String.Reverse", text);

        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current);
        }

        elements.Reverse();
        return string.Concat(elements);
    }

    /// <summary>Tests whether a text is empty or holds only whitespace. A missing (null) text counts as blank.</summary>
    /// <param name="text">The text to test. A list is tested item by item, and a gap in the list (a blank spreadsheet cell) counts as blank.</param>
    /// <returns>True when the text is null, empty or only whitespace.</returns>
    [NodeName("String.IsBlank")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isBlank")]
    [NodeDescription("Tests whether a text is null, empty or only whitespace. With a list it tests every item and a gap in the list counts as blank, so a column of cells gives one true or false per cell.")]
    [NodeSearchTags("empty", "null", "whitespace", "missing", "has value", "validate")]
    public static bool IsBlank([AcceptsNull] string text)
    {
        return string.IsNullOrWhiteSpace(text);
    }

    /// <summary>Takes the first characters of a text. A count larger than the text returns the whole text.</summary>
    /// <param name="text">The text to take from.</param>
    /// <param name="count">How many characters to take (0 or more).</param>
    /// <returns>The first <paramref name="count"/> characters.</returns>
    [NodeName("String.Left")]
    [return: NodeName("text")]
    [NodeDescription("Returns the first characters of a text (the whole text when the count is larger).")]
    [NodeSearchTags("first", "start", "prefix", "head", "take", "substring")]
    public static string Left(string text, int count)
    {
        RequireText("String.Left", text);
        RequireCount("String.Left", count);
        return count >= text.Length ? text : text.Substring(0, count);
    }

    /// <summary>Takes the last characters of a text. A count larger than the text returns the whole text.</summary>
    /// <param name="text">The text to take from.</param>
    /// <param name="count">How many characters to take (0 or more).</param>
    /// <returns>The last <paramref name="count"/> characters.</returns>
    [NodeName("String.Right")]
    [return: NodeName("text")]
    [NodeDescription("Returns the last characters of a text (the whole text when the count is larger).")]
    [NodeSearchTags("last", "end", "suffix", "tail", "take", "substring")]
    public static string Right(string text, int count)
    {
        RequireText("String.Right", text);
        RequireCount("String.Right", count);
        return count >= text.Length ? text : text.Substring(text.Length - count);
    }

    /// <summary>
    /// Removes accents and similar marks from letters ("é" becomes "e", "Zürich" becomes "Zurich"). Letters that do
    /// not decompose into a base letter plus a mark ("ø", "ł", "ß") are left as they are.
    /// </summary>
    /// <param name="text">The text to clean.</param>
    /// <returns>The text without diacritics.</returns>
    [NodeName("String.RemoveDiacritics")]
    [return: NodeName("text")]
    [NodeDescription("Removes accents from letters (\"é\" becomes \"e\") so names compare and sort without them.")]
    [NodeSearchTags("accents", "umlaut", "ascii", "normalize", "normalise", "latin", "strip")]
    public static string RemoveDiacritics(string text)
    {
        RequireText("String.RemoveDiacritics", text);

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // ── Helpers (private, so the loader does not import them) ───────────────

    private static void RequireText(string node, string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), node + " requires a text. Wire text into the 'text' input.");
        }
    }

    private static void RequireSearch(string node, string search)
    {
        if (search == null)
        {
            throw new ArgumentNullException(nameof(search), node + " requires the text to look for. Wire text into the 'search' input.");
        }
    }

    private static void RequireCount(string node, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                node + ": count must be 0 or more (got " + count.ToString(CultureInfo.InvariantCulture) + ").");
        }
    }

    private static string Pad(string node, string text, int width, string padChar, bool left)
    {
        RequireText(node, text);
        if (padChar == null || padChar.Length != 1)
        {
            throw new ArgumentException(
                node + ": padChar must be exactly one character (got '" + padChar + "').",
                nameof(padChar));
        }

        if (width < 0 || width > MaxPadWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                node + ": width must be between 0 and " + MaxPadWidth.ToString(CultureInfo.InvariantCulture) +
                " (got " + width.ToString(CultureInfo.InvariantCulture) + ").");
        }

        return left ? text.PadLeft(width, padChar[0]) : text.PadRight(width, padChar[0]);
    }

    private static void AddLine(List<string> lines, string line, bool removeEmpty)
    {
        if (!removeEmpty || line.Length > 0)
        {
            lines.Add(line);
        }
    }

    private static bool IsAllZeros(string formatted)
    {
        foreach (var c in formatted)
        {
            if (c >= '1' && c <= '9')
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeKeys(IEnumerable<string> keys)
    {
        var shown = new List<string>();
        var total = 0;
        foreach (var key in keys)
        {
            total++;
            if (shown.Count < 10)
            {
                shown.Add(key);
            }
        }

        if (total == 0)
        {
            return "The dictionary is empty.";
        }

        return "Available keys: " + string.Join(", ", shown) + (total > shown.Count ? ", ..." : ".");
    }

    // Checks every "{n" placeholder against the number of values so a missing one gets a message that names it
    // (the framework's own message does not say which placeholder was short).
    private static void CheckPlaceholders(string format, int valueCount)
    {
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '{')
            {
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '{')
            {
                i++;
                continue;
            }

            var j = i + 1;
            long index = 0;
            var digits = 0;
            while (j < format.Length && format[j] >= '0' && format[j] <= '9')
            {
                if (digits < 9)
                {
                    index = index * 10 + (format[j] - '0');
                }

                digits++;
                j++;
            }

            if (digits == 0)
            {
                continue; // not a numbered placeholder: let string.Format report it
            }

            if (digits > 9 || index >= valueCount)
            {
                var shown = digits > 9 ? format.Substring(i + 1, digits) : index.ToString(CultureInfo.InvariantCulture);
                var available = valueCount == 0
                    ? "no values are wired"
                    : "only " + valueCount.ToString(CultureInfo.InvariantCulture) + " value(s) are wired (placeholders {0}" +
                      (valueCount > 1 ? " to {" + (valueCount - 1).ToString(CultureInfo.InvariantCulture) + "}" : string.Empty) +
                      " are available)";
                throw new ArgumentException(
                    "String.Format: the format uses the placeholder {" + shown + "} but " + available +
                    ". Wire another value into 'values' or lower the placeholder number.",
                    "values");
            }

            i = j - 1;
        }
    }

    private static Regex CreateRegex(string node, string pattern, bool ignoreCase)
    {
        if (pattern == null)
        {
            throw new ArgumentNullException(nameof(pattern), node + " requires a regular expression pattern. Wire text into the 'pattern' input.");
        }

        try
        {
            var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            return RegexCache.Get(pattern, options, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(node + ": the pattern '" + pattern + "' is not valid: " + ex.Message, nameof(pattern), ex);
        }
    }

    private static T RunRegex<T>(string node, string pattern, Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException(
                node + ": matching the pattern '" + pattern + "' took longer than 2 seconds and was stopped. " +
                "Simplify the pattern (avoid nested repeats such as '(a+)+') or match a shorter text.",
                ex);
        }
    }
}
