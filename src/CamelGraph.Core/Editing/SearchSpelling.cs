using System;

namespace CamelGraph.Core.Editing;

/// <summary>
/// Makes the British and American spellings of a word the same word for the library search, so "colour" finds <c>Color.ByHSV</c>,
/// "grey" finds "gray", "centre" finds "center" and "metre" finds "meter". Both the text of every node and the text typed in the
/// search box are folded the same way before they are compared, so it does not matter which spelling either side uses.
/// </summary>
public static class SearchSpelling
{
    // Ordered: a longer form comes before the shorter one it contains ("centred" before "centre", "storeys" before "storey").
    // The "-is-" stems cover the verb and its forms (normalise, normalised, normalising, normalisation).
    private static readonly string[][] Pairs =
    {
        new[] { "colour", "color" },
        new[] { "grey", "gray" },
        new[] { "centred", "centered" },
        new[] { "centre", "center" },
        new[] { "metre", "meter" },
        new[] { "litre", "liter" },
        new[] { "storeys", "stories" },
        new[] { "storey", "story" },
        new[] { "neighbour", "neighbor" },
        new[] { "behaviour", "behavior" },
        new[] { "favourite", "favorite" },
        new[] { "catalogue", "catalog" },
        new[] { "analysing", "analyzing" },
        new[] { "analysed", "analyzed" },
        new[] { "analyse", "analyze" },
        new[] { "normalis", "normaliz" },
        new[] { "capitalis", "capitaliz" },
        new[] { "summaris", "summariz" },
        new[] { "organis", "organiz" },
        new[] { "visualis", "visualiz" },
        new[] { "categoris", "categoriz" },
        new[] { "customis", "customiz" },
        new[] { "optimis", "optimiz" },
        new[] { "minimis", "minimiz" },
        new[] { "maximis", "maximiz" },
        new[] { "initialis", "initializ" },
        new[] { "synchronis", "synchroniz" },
    };

    /// <summary>
    /// The text with British spellings changed to American ones ("colours" becomes "colors", "centreline" becomes "centerline",
    /// "normalised" becomes "normalized"). Anything else is left alone.
    /// </summary>
    /// <param name="lowerText">Text already in lower case (the search folds case first).</param>
    /// <returns>The folded text; the input itself when there is nothing to fold.</returns>
    public static string Fold(string? lowerText)
    {
        if (string.IsNullOrEmpty(lowerText))
        {
            return string.Empty;
        }

        var text = lowerText!;
        foreach (var pair in Pairs)
        {
            if (text.IndexOf(pair[0], StringComparison.Ordinal) >= 0)
            {
                text = text.Replace(pair[0], pair[1]);
            }
        }

        return text;
    }
}
