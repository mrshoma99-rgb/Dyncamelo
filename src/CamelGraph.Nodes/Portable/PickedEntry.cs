using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>What looking at the item found at a picked entry's tree path says about it.</summary>
public enum PickCheck
{
    /// <summary>The entry carries nothing to compare (a graph saved before identities were stored): the item is taken as it is.</summary>
    Unchecked,

    /// <summary>The item is the one that was picked.</summary>
    Verified,

    /// <summary>The item at that place is a different one: the model changed since the pick.</summary>
    Mismatch,
}

/// <summary>
/// One picked model element as it is stored in a graph file: its position in the model tree ("modelIndex:childIdx/childIdx", which
/// stops pointing at the same element when the model is rebuilt, another model is appended or the script runs on another project) and,
/// since the audit, its identity: the instance GUID, or its name when the element has no GUID. The identity is what the node checks the
/// element at the path against, so a pick that no longer points at the same element is found by its GUID or reported, never silently
/// replaced by whatever now sits at the old position. Written as <c>path</c>, <c>path|guid</c> or <c>path||name</c>; a file written before
/// identities were stored holds the path only and still loads. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class PickedEntry
{
    /// <summary>Creates an entry.</summary>
    /// <param name="path">The tree path, "modelIndex:childIdx/childIdx".</param>
    /// <param name="guid">The element's instance GUID, or <see cref="Guid.Empty"/> when it has none (or none was stored).</param>
    /// <param name="name">The element's name, kept only when there is no GUID.</param>
    public PickedEntry(string path, Guid guid, string? name)
    {
        Path = path ?? string.Empty;
        Guid = guid;
        Name = guid == Guid.Empty && !string.IsNullOrEmpty(name) ? name : null;
    }

    /// <summary>The tree path.</summary>
    public string Path { get; }

    /// <summary>The instance GUID, or <see cref="Guid.Empty"/>.</summary>
    public Guid Guid { get; }

    /// <summary>The name, when the element has no GUID; otherwise null.</summary>
    public string? Name { get; }

    /// <summary>True when the entry carries an identity to check the element against.</summary>
    public bool HasIdentity => Guid != Guid.Empty || Name != null;

    /// <summary>The index of the model the path starts in (the number before the colon), or -1.</summary>
    public int ModelIndex
    {
        get
        {
            var colon = Path.IndexOf(':');
            return colon > 0 && int.TryParse(Path.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : -1;
        }
    }

    /// <summary>Reads an entry; text with only a path (an old file) gives an entry without identity.</summary>
    /// <param name="text">The stored text.</param>
    public static PickedEntry Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split(new[] { '|' }, 3);
        var guid = Guid.Empty;
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            Guid.TryParse(parts[1], out guid);
        }

        var name = parts.Length > 2 ? Unescape(parts[2]) : null;
        return new PickedEntry(parts[0].Trim(), guid, name);
    }

    /// <summary>Writes the entry as stored in the graph file.</summary>
    public string Format()
    {
        if (Guid != Guid.Empty)
        {
            return Path + "|" + Guid.ToString("N");
        }

        return Name != null ? Path + "||" + Escape(Name) : Path;
    }

    /// <summary>The stored text of a picked element: path plus identity.</summary>
    /// <param name="path">The tree path.</param>
    /// <param name="guid">The element's instance GUID.</param>
    /// <param name="displayName">The element's name.</param>
    public static string Encode(string path, Guid guid, string? displayName) => new PickedEntry(path, guid, displayName).Format();

    /// <summary>Compares the element found at the entry's path with what was picked.</summary>
    /// <param name="entry">The stored entry.</param>
    /// <param name="guidAtPath">The instance GUID of the element at the path.</param>
    /// <param name="nameAtPath">The name of the element at the path.</param>
    public static PickCheck Check(PickedEntry entry, Guid guidAtPath, string? nameAtPath)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (entry.Guid != Guid.Empty)
        {
            return guidAtPath == entry.Guid ? PickCheck.Verified : PickCheck.Mismatch;
        }

        if (entry.Name != null)
        {
            return string.Equals(nameAtPath ?? string.Empty, entry.Name, StringComparison.Ordinal) ? PickCheck.Verified : PickCheck.Mismatch;
        }

        return PickCheck.Unchecked;
    }

    /// <summary>
    /// Chooses the element that has a picked GUID when the path no longer fits: the only one, or, when several carry the same GUID (a
    /// file appended twice), the only one in the model the pick came from. Never guesses between several.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="candidates">Every element found with that GUID (may be null or empty).</param>
    /// <param name="inPickedModel">Whether a candidate belongs to the model the pick came from.</param>
    /// <param name="chosen">The element, or the default.</param>
    /// <returns>True when exactly one element qualifies.</returns>
    public static bool TryChoose<T>(IReadOnlyList<T>? candidates, Func<T, bool> inPickedModel, out T chosen)
    {
        chosen = default!;
        if (candidates == null || candidates.Count == 0)
        {
            return false;
        }

        if (candidates.Count == 1)
        {
            chosen = candidates[0];
            return true;
        }

        var found = 0;
        foreach (var candidate in candidates)
        {
            if (inPickedModel(candidate))
            {
                chosen = candidate;
                found++;
            }
        }

        if (found == 1)
        {
            return true;
        }

        chosen = default!;
        return false;
    }

    /// <summary>The warning for picked elements that could not be resolved; null when all were.</summary>
    /// <param name="total">How many elements the pick holds.</param>
    /// <param name="missing">How many could not be found.</param>
    public static string? MissingMessage(int total, int missing)
    {
        if (missing <= 0)
        {
            return null;
        }

        const string cause = "the model is not the one they were picked in, or it has changed since";
        const string remedy = "Pick them again.";
        if (missing >= total)
        {
            return total == 1
                ? "The picked element was not found (" + cause + ") and was left out. " + remedy
                : "None of the " + total.ToString(CultureInfo.InvariantCulture) + " picked elements was found (" + cause + "). " + remedy;
        }

        return missing.ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture) +
               " picked elements were not found (" + cause + ") and were left out. " + remedy;
    }

    private static string Escape(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            switch (ch)
            {
                case '%': builder.Append("%25"); break;
                case ';': builder.Append("%3B"); break;
                case '|': builder.Append("%7C"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    private static string Unescape(string text) =>
        text.Replace("%7C", "|").Replace("%3B", ";").Replace("%25", "%");
}
