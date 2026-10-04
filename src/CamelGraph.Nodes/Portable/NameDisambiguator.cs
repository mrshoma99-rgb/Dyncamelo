using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>The result of <see cref="NameDisambiguator.Make"/>.</summary>
public sealed class DisambiguatedNames
{
    /// <summary>Creates a result.</summary>
    /// <param name="names">One name per item, in the order of the input.</param>
    /// <param name="groupsPrefixed">True when the group was put in front of the names.</param>
    /// <param name="renumbered">How many names got a number because they still repeated.</param>
    public DisambiguatedNames(IReadOnlyList<string> names, bool groupsPrefixed, int renumbered)
    {
        Names = names;
        GroupsPrefixed = groupsPrefixed;
        Renumbered = renumbered;
    }

    /// <summary>One name per item, in the order of the input; no two are the same.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>True when the items came from more than one group and the group was put in front of every name.</summary>
    public bool GroupsPrefixed { get; }

    /// <summary>How many names got " (2)", " (3)" ... because they still repeated after the group was added.</summary>
    public int Renumbered { get; }
}

/// <summary>
/// Gives a batch of items names that are unique within the batch, for nodes that name one saved item (a viewpoint, a BCF topic)
/// after each clash result. Clash Detective numbers its results per test (Clash1, Clash2 ... in every test), so a batch that
/// spans two tests holds the same name twice and the second item would replace the first. When the items belong to more than one
/// group (the test) every name gets its group in front of it; whatever still repeats after that is numbered. Pure (no Navisworks
/// types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NameDisambiguator
{
    /// <summary>The separator between the group and the name when a group is put in front of the names.</summary>
    public const string GroupSeparator = " - ";

    /// <summary>Makes the names of a batch unique.</summary>
    /// <param name="names">The name each item would have on its own (the clash result's name).</param>
    /// <param name="groups">The group of each item (the clash test's name); null or empty for none. Same length as <paramref name="names"/>.</param>
    /// <returns>The unique names, and whether the group was added and how many names were numbered.</returns>
    public static DisambiguatedNames Make(IReadOnlyList<string> names, IReadOnlyList<string?> groups)
    {
        if (names == null)
        {
            throw new ArgumentNullException(nameof(names));
        }

        if (groups == null)
        {
            throw new ArgumentNullException(nameof(groups));
        }

        if (groups.Count != names.Count)
        {
            throw new ArgumentException("names and groups must have the same length.", nameof(groups));
        }

        var distinctGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (!string.IsNullOrEmpty(group))
            {
                distinctGroups.Add(group!);
            }
        }

        var prefixed = distinctGroups.Count > 1;
        var bases = new List<string>(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i] ?? string.Empty;
            bases.Add(prefixed && !string.IsNullOrEmpty(groups[i]) ? groups[i] + GroupSeparator + name : name);
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>(bases.Count);
        var renumbered = 0;
        foreach (var name in bases)
        {
            if (used.Add(name))
            {
                result.Add(name);
                continue;
            }

            counts.TryGetValue(name, out var seen);
            string candidate;
            do
            {
                seen++;
                candidate = name + " (" + (seen + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            }
            while (!used.Add(candidate));

            counts[name] = seen;
            result.Add(candidate);
            renumbered++;
        }

        return new DisambiguatedNames(result, prefixed, renumbered);
    }
}
