using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// Text nodes. String inputs replicate over lists of strings; the engine's
/// coercion converts numbers to strings on the way in where needed.
/// </summary>
[NodeCategory("String")]
public static class StringNodes
{
    /// <summary>Concatenates two strings.</summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <returns>The two strings joined together.</returns>
    [NodeName("String.Concat")]
    [NodeDescription("Joins two strings into one.")]
    [NodeSearchTags("concatenate", "join", "append", "+")]
    public static string Concat(string a, string b)
    {
        return (a ?? string.Empty) + (b ?? string.Empty);
    }

    /// <summary>Joins a list of values into one string with a separator between elements.</summary>
    /// <param name="separator">Text placed between elements.</param>
    /// <param name="list">The values to join; non-strings are formatted invariantly.</param>
    /// <returns>The joined string.</returns>
    [NodeName("String.Join")]
    [NodeDescription("Joins the elements of a list into a single string with a separator.")]
    [NodeSearchTags("concatenate", "combine", "delimiter")]
    public static string Join(string separator, IList<object?> list)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list), "String.Join requires a list.");
        }

        var parts = new List<string>(list.Count);
        foreach (var item in list)
        {
            parts.Add(TypeCoercion.FormatValue(item));
        }

        return string.Join(separator ?? string.Empty, parts);
    }

    /// <summary>Tests whether a string contains a substring.</summary>
    /// <param name="str">The string to search in.</param>
    /// <param name="searchFor">The substring to look for.</param>
    /// <param name="ignoreCase">True to compare case-insensitively.</param>
    /// <returns>True when the substring occurs in the string.</returns>
    [NodeName("String.Contains")]
    [NodeDescription("Tests whether a string contains the given substring.")]
    [NodeSearchTags("substring", "search", "find")]
    public static bool Contains(string str, string searchFor, bool ignoreCase = false)
    {
        if (str == null)
        {
            throw new ArgumentNullException(nameof(str), "String.Contains requires a string.");
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return str.IndexOf(searchFor ?? string.Empty, comparison) >= 0;
    }

    /// <summary>
    /// Splits a string on a separator. An empty separator returns the whole
    /// string as a single-element list; empty segments between adjacent
    /// separators are preserved.
    /// </summary>
    /// <param name="str">The string to split.</param>
    /// <param name="separator">Separator text.</param>
    /// <returns>The list of segments.</returns>
    [NodeName("String.Split")]
    [return: NodeName("list")]
    [NodeDescription("Splits a string into a list of substrings around a separator.")]
    [NodeSearchTags("tokenize", "divide", "delimiter")]
    public static IList<string> Split(string str, string separator)
    {
        if (str == null)
        {
            throw new ArgumentNullException(nameof(str), "String.Split requires a string.");
        }

        if (string.IsNullOrEmpty(separator))
        {
            return new List<string> { str };
        }

        return new List<string>(str.Split(new[] { separator }, StringSplitOptions.None));
    }

    /// <summary>Replaces every occurrence of a substring with another string.</summary>
    /// <param name="str">The string to modify.</param>
    /// <param name="searchFor">Substring to replace (must not be empty).</param>
    /// <param name="replaceWith">Replacement text.</param>
    /// <returns>The string with all occurrences replaced.</returns>
    [NodeName("String.Replace")]
    [NodeDescription("Replaces all occurrences of a substring with another string.")]
    [NodeSearchTags("substitute", "swap")]
    public static string Replace(string str, string searchFor, string replaceWith)
    {
        if (str == null)
        {
            throw new ArgumentNullException(nameof(str), "String.Replace requires a string.");
        }

        if (string.IsNullOrEmpty(searchFor))
        {
            throw new ArgumentException("String.Replace requires a non-empty search string.", nameof(searchFor));
        }

        return str.Replace(searchFor, replaceWith ?? string.Empty);
    }

    /// <summary>Length of a string in characters.</summary>
    /// <param name="str">The string to measure.</param>
    /// <returns>The number of characters.</returns>
    [NodeName("String.Length")]
    [NodeDescription("Returns the number of characters in a string.")]
    [NodeSearchTags("count", "size")]
    public static int Length(string str)
    {
        if (str == null)
        {
            throw new ArgumentNullException(nameof(str), "String.Length requires a string.");
        }

        return str.Length;
    }

    /// <summary>
    /// Parses a string as a number using the invariant culture (decimal point,
    /// no thousands separators). Whitespace around the number is ignored.
    /// </summary>
    /// <param name="str">The text to parse, e.g. "3.14".</param>
    /// <returns>The parsed number.</returns>
    [NodeName("String.ToNumber")]
    [NodeDescription("Converts a numeric string (invariant culture, e.g. \"3.14\") to a number.")]
    [NodeSearchTags("parse", "convert", "double")]
    public static double ToNumber(string str)
    {
        if (str == null)
        {
            throw new ArgumentNullException(nameof(str), "String.ToNumber requires a string.");
        }

        if (double.TryParse(str.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new FormatException("Cannot convert '" + str + "' to a number. Expected an invariant-culture numeric string such as \"3.14\".");
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
    [NodeDescription("Tests whether a string starts with the given prefix.")]
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
    [NodeDescription("Tests whether a string ends with the given suffix.")]
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
    /// the start index to the end of the string.
    /// </summary>
    /// <param name="text">The string to slice.</param>
    /// <param name="startIndex">Zero-based index of the first character to take.</param>
    /// <param name="length">Number of characters to take; -1 = to the end.</param>
    /// <returns>The extracted substring.</returns>
    [NodeName("String.Substring")]
    [NodeDescription("Extracts part of a string from a start index (-1 length = to the end).")]
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

        if (length < 0)
        {
            return text.Substring(startIndex);
        }

        if (startIndex + length > text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "String.Substring: start index " + startIndex.ToString(CultureInfo.InvariantCulture) +
                " plus length " + length.ToString(CultureInfo.InvariantCulture) +
                " exceeds the string's " + text.Length.ToString(CultureInfo.InvariantCulture) + " character(s).");
        }

        return text.Substring(startIndex, length);
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

    /// <summary>Removes leading and trailing whitespace from a string.</summary>
    /// <param name="text">The string to trim.</param>
    /// <returns>The trimmed string.</returns>
    [NodeName("String.Trim")]
    [NodeDescription("Removes leading and trailing whitespace from a string.")]
    [NodeSearchTags("whitespace", "strip", "clean")]
    public static string Trim(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "String.Trim requires a string.");
        }

        return text.Trim();
    }
}
