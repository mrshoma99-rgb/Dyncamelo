using System;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Text;

/// <summary>
/// Unordered pair matching against two text filters — the core of
/// Clash.FilterByItemProperty ("Pipe vs Wall" reads the same whichever side
/// the pipe landed on). An empty filter matches anything; a null text (the
/// property was missing) matches nothing. Pure — fully unit-testable.
/// </summary>
[IsVisibleInLibrary(false)]
public static class TextPairFilter
{
    /// <summary>
    /// Whether the unordered pair (textA, textB) satisfies the filter pair
    /// (filter1, filter2) in either assignment.
    /// </summary>
    public static bool PairMatches(
        string? textA, string? textB, string? filter1, string? filter2, string mode, bool caseSensitive)
    {
        return (SideMatches(textA, filter1, mode, caseSensitive) && SideMatches(textB, filter2, mode, caseSensitive)) ||
               (SideMatches(textA, filter2, mode, caseSensitive) && SideMatches(textB, filter1, mode, caseSensitive));
    }

    /// <summary>
    /// Whether one text satisfies one filter under the mode ("contains",
    /// "equals", "starts with" or "ends with"). Empty filter = matches
    /// anything; null text (missing value) = matches nothing.
    /// </summary>
    public static bool SideMatches(string? text, string? filter, string mode, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        if (text == null)
        {
            return false;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "equals":
                return string.Equals(text, filter, comparison);
            case "starts with":
                return text.StartsWith(filter!, comparison);
            case "ends with":
                return text.EndsWith(filter!, comparison);
            case "contains":
            case "":
                return text.IndexOf(filter!, comparison) >= 0;
            default:
                throw new ArgumentException(
                    "Unknown mode '" + mode + "'. Use contains, equals, starts with or ends with.", nameof(mode));
        }
    }
}
