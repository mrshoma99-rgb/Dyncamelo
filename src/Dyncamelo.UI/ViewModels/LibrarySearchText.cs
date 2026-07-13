using System;
using System.Globalization;
using System.Text;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Normalization shared by the library's search index and the typed query, so
/// both sides are folded identically no matter how the text was produced.
/// Keyboards — RTL layouts and IMEs especially, and more so with Caps Lock
/// engaged — can slip invisible characters into typed text (directional marks
/// like RLM/LRM/ALM, zero-width joiners) or exotic whitespace (NBSP); one such
/// character glued to a token silently defeats an ordinal Contains. Everything
/// is therefore lower-cased invariantly, invisible control/format characters
/// are dropped, and every whitespace variant becomes a plain space.
/// </summary>
internal static class LibrarySearchText
{
    /// <summary>Folds text for matching: invariant lower-case, no control/format chars, uniform spaces.</summary>
    /// <param name="text">Raw entry metadata or search-box text.</param>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text!.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.Control || category == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>Normalizes a query and splits it into non-empty tokens.</summary>
    /// <param name="text">Search-box text as typed.</param>
    internal static string[] Tokenize(string? text)
    {
        return Normalize(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
