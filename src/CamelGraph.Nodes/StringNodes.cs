using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;

namespace CamelGraph.Nodes;

/// <summary>
/// Text nodes. String inputs replicate over lists of strings; the engine's
/// coercion converts numbers to strings on the way in where needed.
/// </summary>
[NodeCategory("String")]
public static class StringNodes
{
    /// <summary>Concatenates two strings.</summary>
    /// <param name="a">First string. A missing (null) value counts as empty text, also for the gaps of a list.</param>
    /// <param name="b">Second string. A missing (null) value counts as empty text, also for the gaps of a list.</param>
    /// <returns>The two strings joined together.</returns>
    [NodeName("String.Concat")]
    [NodeDescription("Joins two strings into one. A missing value counts as empty text, also a gap in a list (a column with blank cells keeps every row).")]
    [NodeSearchTags("concatenate", "join", "append", "+")]
    public static string Concat([AcceptsNull] string a, [AcceptsNull] string b)
    {
        return (a ?? string.Empty) + (b ?? string.Empty);
    }

    /// <summary>Joins a list of values into one string with a separator between elements.</summary>
    /// <param name="separator">Text placed between elements.</param>
    /// <param name="list">The values to join; non-strings are formatted invariantly and a missing (null) value becomes empty text, as in String.Concat.</param>
    /// <returns>The joined string.</returns>
    [NodeName("String.Join")]
    [NodeDescription("Joins the elements of a list into a single string with a separator. A missing value becomes empty text (\"a,,c\"), as in String.Concat.")]
    [NodeSearchTags("concatenate", "combine", "delimiter")]
    public static string Join(string separator, [MultiInput] IList<object?> list)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list), "String.Join requires a list.");
        }

        var parts = new List<string>(list.Count);
        foreach (var item in list)
        {
            parts.Add(item == null ? string.Empty : TypeCoercion.FormatValue(item));
        }

        return string.Join(separator ?? string.Empty, parts);
    }

    /// <summary>Tests whether a string contains a substring.</summary>
    /// <param name="text">The string to search in.</param>
    /// <param name="searchFor">The substring to look for.</param>
    /// <param name="ignoreCase">True (default) to compare case-insensitively.</param>
    /// <returns>True when the substring occurs in the string.</returns>
    [NodeName("String.Contains")]
    [PortAlias("str", "text")]
    [NodeDescription("Tests whether a string contains the given substring. Ignores case unless ignoreCase is switched off.")]
    [NodeSearchTags("substring", "search", "find")]
    public static bool Contains(string text, string searchFor, bool ignoreCase = true)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Contains requires a string.");
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return text.IndexOf(searchFor ?? string.Empty, comparison) >= 0;
    }

    /// <summary>
    /// Splits a string on a separator. An empty separator returns the whole
    /// string as a single-element list; empty segments between adjacent
    /// separators are preserved unless <paramref name="removeEmpty"/> is on.
    /// </summary>
    /// <param name="text">The string to split.</param>
    /// <param name="separator">Separator text.</param>
    /// <param name="removeEmpty">True to drop the empty parts, so "a,b,,c" gives three parts (off by default).</param>
    /// <param name="trim">True to remove the spaces around every part, so "a, b ,c" gives a, b and c (off by default). Trimming happens before the empty parts are dropped, so a part of only spaces counts as empty.</param>
    /// <returns>The list of segments.</returns>
    [NodeName("String.Split")]
    [PortAlias("str", "text")]
    [NodeAliases("CamelGraph.Nodes.StringNodes.Split@string,string")]
    [return: NodeName("list")]
    [NodeDescription("Splits a string into a list of substrings around a separator. Empty parts are kept unless removeEmpty is on; trim removes the spaces around every part (\"a, b ,c\" with separator \",\" and trim gives a, b, c).")]
    [NodeSearchTags("tokenize", "divide", "delimiter", "csv", "comma")]
    public static IList<string> Split(string text, string separator, bool removeEmpty = false, bool trim = false)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Split requires a string.");
        }

        var parts = string.IsNullOrEmpty(separator)
            ? new List<string> { text }
            : new List<string>(text.Split(new[] { separator }, StringSplitOptions.None));

        if (trim)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                parts[i] = parts[i].Trim();
            }
        }

        if (removeEmpty)
        {
            parts.RemoveAll(part => part.Length == 0);
        }

        return parts;
    }

    /// <summary>Replaces every occurrence of a substring with another string.</summary>
    /// <param name="text">The string to modify.</param>
    /// <param name="searchFor">Substring to replace (must not be empty).</param>
    /// <param name="replaceWith">Replacement text.</param>
    /// <param name="ignoreCase">True to find the substring whatever its upper/lower case (off by default: the search is case-sensitive).</param>
    /// <returns>The string with all occurrences replaced.</returns>
    [NodeName("String.Replace")]
    [PortAlias("str", "text")]
    [NodeAliases("CamelGraph.Nodes.StringNodes.Replace@string,string,string")]
    [NodeDescription("Replaces all occurrences of a substring with another string. Case-sensitive unless ignoreCase is switched on.")]
    [NodeSearchTags("substitute", "swap")]
    public static string Replace(string text, string searchFor, string replaceWith, bool ignoreCase = false)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Replace requires a string.");
        }

        if (string.IsNullOrEmpty(searchFor))
        {
            throw new ArgumentException("String.Replace requires a non-empty search string.", nameof(searchFor));
        }

        if (!ignoreCase)
        {
            return text.Replace(searchFor, replaceWith ?? string.Empty);
        }

        var replacement = replaceWith ?? string.Empty;
        var result = new System.Text.StringBuilder(text.Length);
        var position = 0;
        while (true)
        {
            var found = text.IndexOf(searchFor, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            result.Append(text, position, found - position).Append(replacement);
            position = found + searchFor.Length;
        }

        return result.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>Length of a string in characters.</summary>
    /// <param name="text">The string to measure.</param>
    /// <returns>The number of characters.</returns>
    [NodeName("String.Length")]
    [PortAlias("str", "text")]
    [NodeDescription("Returns the number of characters in a string.")]
    [NodeSearchTags("count", "size")]
    public static int Length(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Length requires a string.");
        }

        return text.Length;
    }

    /// <summary>
    /// Parses a string as a number. By default the text must be a plain invariant number (decimal point, no thousands
    /// separators; whitespace around it is ignored). A decimal comma and the first number of a text with a unit are
    /// opt-in.
    /// </summary>
    /// <param name="text">The text to parse, e.g. "3.14".</param>
    /// <param name="decimalSeparator">The decimal separator of the text: "." (default, "3.14") or "," ("1,5").</param>
    /// <param name="ignoreUnits">True to read the first number in the text and ignore the rest, so "12.5 mm" gives 12.5 (off by default: the whole text must be the number). Thousands separators ("1,234.5 m") are read only in this mode.</param>
    /// <returns>The parsed number.</returns>
    [NodeName("String.ToNumber")]
    [PortAlias("str", "text")]
    [NodeAliases("CamelGraph.Nodes.StringNodes.ToNumber@string")]
    [NodeDescription("Converts a numeric string to a number. By default the whole text must be a plain number with a decimal point (\"3.14\", \"-2\", \"1.5e3\"). " +
        "Set decimalSeparator to \",\" for a decimal comma (\"1,5\"). Switch ignoreUnits on to take the first number out of a text with a unit or a label (\"12.5 mm\" gives 12.5, \"approx. 1,234.5 m\" gives 1234.5; " +
        "thousands separators are read only in this mode). A text without a number still fails; the row shows a warning.")]
    [NodeSearchTags("parse", "convert", "double", "extract number", "decimal comma", "units")]
    public static double ToNumber(string text, [NodeChoices(".", ",")] string decimalSeparator = ".", bool ignoreUnits = false)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.ToNumber requires a string.");
        }

        var comma = IsDecimalComma(decimalSeparator);
        if (!comma && !ignoreUnits)
        {
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            // This text is pinned by the Wiki scene graphs (tests/Shared/Wiki/SceneGraphs.cs) and shown in the wiki pictures.
            throw new FormatException("Cannot convert '" + text + "' to a number. Expected an invariant-culture numeric string such as \"3.14\".");
        }

        if (!ignoreUnits)
        {
            var trimmed = text.Trim();
            if (StrictCommaNumber.IsMatch(trimmed) &&
                double.TryParse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var commaValue))
            {
                return commaValue;
            }

            throw new FormatException(
                "Cannot convert '" + text + "' to a number. With decimalSeparator \",\" the text must be a number with a decimal comma such as \"1,5\" " +
                "(no decimal point, no thousands separators). To read the number out of a text with a unit (\"12,5 mm\") switch ignoreUnits on.");
        }

        var match = (comma ? FirstNumberComma : FirstNumberDot).Match(text);
        if (!match.Success)
        {
            throw new FormatException("Cannot find a number in '" + text + "'. Text without digits cannot be converted; check the text or test it first (String.RegexIsMatch).");
        }

        var other = comma ? '.' : ',';
        var after = match.Index + match.Length;
        if (after + 1 < text.Length && text[after] == other && char.IsDigit(text[after + 1]))
        {
            throw new FormatException(
                "Cannot tell what the number in '" + text + "' means: after '" + match.Value + "' the text goes on with '" + other + "' and a digit. " +
                "Set decimalSeparator to \"" + other + "\" if that is the decimal separator of the text.");
        }

        var digits = comma ? match.Value.Replace(".", string.Empty).Replace(',', '.') : match.Value.Replace(",", string.Empty);
        if (double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        throw new FormatException("Cannot convert '" + match.Value + "' (found in '" + text + "') to a number.");
    }

    // The first number in a text: an optional sign (only when nothing letter-like sits in front of it, so "A-12" is 12), digits with
    // correct thousands grouping or plain digits, an optional fraction and an optional exponent. ".5" counts when no letter or digit
    // is in front of the point ("Rev.2" is 2).
    private static readonly Regex FirstNumberDot = new Regex(
        @"(?:(?<![\p{L}\p{N}])[+-])?(?:\d{1,3}(?:,\d{3})+(?!\d)(?:\.\d+)?|\d+(?:\.\d+)?|(?<![\p{L}\p{N}])\.\d+)(?:[eE][+-]?\d+)?",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex FirstNumberComma = new Regex(
        @"(?:(?<![\p{L}\p{N}])[+-])?(?:\d{1,3}(?:\.\d{3})+(?!\d)(?:,\d+)?|\d+(?:,\d+)?|(?<![\p{L}\p{N}]),\d+)(?:[eE][+-]?\d+)?",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    // A whole number written with a decimal comma: "1,5", "-2", ",5", "1,5e3".
    private static readonly Regex StrictCommaNumber = new Regex(
        @"^[+-]?(?:\d+(?:,\d*)?|,\d+)(?:[eE][+-]?\d+)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static bool IsDecimalComma(string? decimalSeparator)
    {
        var separator = decimalSeparator == null ? string.Empty : decimalSeparator.Trim();
        if (separator.Length == 0 || separator == ".")
        {
            return false;
        }

        if (separator == ",")
        {
            return true;
        }

        throw new ArgumentException(
            "String.ToNumber: decimalSeparator must be \".\" or \",\" (got '" + decimalSeparator + "'). Pick one from the list.",
            nameof(decimalSeparator));
    }

    /// <summary>
    /// Formats any value as a display string (invariant culture). The input is
    /// untyped, so lists and dictionaries are formatted whole, e.g. "[1, 2, 3]".
    /// </summary>
    /// <param name="obj">The value to format.</param>
    /// <returns>The formatted text ("null" for null).</returns>
    [NodeName("String.FromObject")]
    [NodeDescription("Converts any value (including whole lists) to its display string.")]
    [NodeSearchTags("tostring", "format", "text")]
    public static string FromObject(object? obj)
    {
        return TypeCoercion.FormatValue(obj);
    }

    /// <summary>Tests whether a string starts with a prefix.</summary>
    /// <param name="text">The string to test.</param>
    /// <param name="searchFor">The prefix to look for.</param>
    /// <param name="ignoreCase">True (default) to compare case-insensitively.</param>
    /// <returns>True when the string starts with the prefix.</returns>
    [NodeName("String.StartsWith")]
    [NodeDescription("Tests whether a string starts with the given prefix. Ignores case unless ignoreCase is switched off.")]
    [NodeSearchTags("prefix", "begins", "leading")]
    public static bool StartsWith(string text, string searchFor, bool ignoreCase = true)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.StartsWith requires a string.");
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return text.StartsWith(searchFor ?? string.Empty, comparison);
    }

    /// <summary>Tests whether a string ends with a suffix.</summary>
    /// <param name="text">The string to test.</param>
    /// <param name="searchFor">The suffix to look for.</param>
    /// <param name="ignoreCase">True (default) to compare case-insensitively.</param>
    /// <returns>True when the string ends with the suffix.</returns>
    [NodeName("String.EndsWith")]
    [NodeDescription("Tests whether a string ends with the given suffix. Ignores case unless ignoreCase is switched off.")]
    [NodeSearchTags("suffix", "trailing", "extension")]
    public static bool EndsWith(string text, string searchFor, bool ignoreCase = true)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.EndsWith requires a string.");
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return text.EndsWith(searchFor ?? string.Empty, comparison);
    }

    /// <summary>
    /// Extracts part of a string. The default length (-1) takes everything from
    /// the start index to the end of the string. A length that runs past the end of the text
    /// gives what is left, like String.Left and String.Right.
    /// </summary>
    /// <param name="text">The string to slice.</param>
    /// <param name="startIndex">Zero-based index of the first character to take (0 to the text's length).</param>
    /// <param name="length">Number of characters to take; -1 = to the end. More than the text has left gives what is left.</param>
    /// <returns>The extracted substring.</returns>
    [NodeName("String.Substring")]
    [NodeDescription("Extracts part of a string from a start index (-1 length = to the end). A length that runs past the end gives what is left, like String.Left and String.Right; a start index beyond the end of the text is an error.")]
    [NodeSearchTags("slice", "extract", "mid", "part")]
    public static string Substring(string text, int startIndex, int length = -1)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Substring requires a string.");
        }

        if (startIndex < 0 || startIndex > text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startIndex),
                "String.Substring: start index " + startIndex.ToString(CultureInfo.InvariantCulture) +
                " is out of range for a string of " + text.Length.ToString(CultureInfo.InvariantCulture) + " character(s).");
        }

        var left = text.Length - startIndex;
        return length < 0 || length >= left ? text.Substring(startIndex) : text.Substring(startIndex, length);
    }

    /// <summary>Converts a string to uppercase (invariant culture).</summary>
    /// <param name="text">The string to convert.</param>
    /// <returns>The uppercase string.</returns>
    [NodeName("String.ToUpper")]
    [NodeDescription("Converts a string to uppercase.")]
    [NodeSearchTags("uppercase", "capital", "case")]
    public static string ToUpper(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.ToUpper requires a string.");
        }

        return text.ToUpperInvariant();
    }

    /// <summary>Converts a string to lowercase (invariant culture).</summary>
    /// <param name="text">The string to convert.</param>
    /// <returns>The lowercase string.</returns>
    [NodeName("String.ToLower")]
    [NodeDescription("Converts a string to lowercase.")]
    [NodeSearchTags("lowercase", "case")]
    public static string ToLower(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.ToLower requires a string.");
        }

        return text.ToLowerInvariant();
    }

    /// <summary>Removes leading and trailing whitespace (or the given characters) from a string.</summary>
    /// <param name="text">The string to trim.</param>
    /// <param name="chars">The characters to remove (each character of this text counts); empty means whitespace.</param>
    /// <returns>The trimmed string.</returns>
    [NodeName("String.Trim")]
    [NodeAliases("CamelGraph.Nodes.StringNodes.Trim@string")]
    [NodeDescription("Removes whitespace (or the given characters) from both ends of a string. See String.TrimStart and String.TrimEnd for one end only.")]
    [NodeSearchTags("whitespace", "strip", "clean")]
    public static string Trim(string text, string chars = "")
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Trim requires a string.");
        }

        return string.IsNullOrEmpty(chars) ? text.Trim() : text.Trim(chars.ToCharArray());
    }
}
