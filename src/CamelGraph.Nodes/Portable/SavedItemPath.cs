using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The text form of where a saved item sits in a Navisworks tree (Saved Viewpoints, Sets): folder names and the item's own name
/// separated by "/", as in <c>Reviews/Week 12/Clash 5</c>. The nodes that take a folder or a name accept this form, so two
/// folders (or two viewpoints in different folders) with the same name can be told apart. Pure (no Navisworks types), so it is
/// unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class SavedItemPath
{
    /// <summary>The separator between the folder names and the item name.</summary>
    public const char Separator = '/';

    /// <summary>Whether the text is a path ("A/B") rather than one plain name.</summary>
    /// <param name="text">The folder or item text.</param>
    public static bool IsPath(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) && text!.IndexOf(Separator) >= 0 && Split(text).Count > 1;
    }

    /// <summary>Splits a path into its names: trimmed, empty parts left out.</summary>
    /// <param name="text">The path, for example <c>Reviews/Week 12</c>.</param>
    /// <returns>The names in order; empty for an empty text.</returns>
    public static List<string> Split(string? text)
    {
        var segments = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return segments;
        }

        foreach (var part in text!.Split(Separator))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                segments.Add(trimmed);
            }
        }

        return segments;
    }

    /// <summary>The path as text: the names joined with "/".</summary>
    /// <param name="segments">The names, outermost folder first.</param>
    public static string Join(IEnumerable<string> segments)
    {
        if (segments == null)
        {
            throw new ArgumentNullException(nameof(segments));
        }

        return string.Join(Separator.ToString(), segments);
    }
}
