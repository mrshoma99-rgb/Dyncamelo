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
}
