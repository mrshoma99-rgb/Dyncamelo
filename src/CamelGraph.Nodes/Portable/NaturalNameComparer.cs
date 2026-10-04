using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// Compares names the way a person sorts them: letters ignoring case, and a run of digits as a NUMBER, so "Clash2" comes before
/// "Clash10" (a plain alphabetical sort puts "Clash10" first). Used by the sort option of the saved-viewpoint folder node. Pure
/// (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class NaturalNameComparer : IComparer<string?>
{
    /// <summary>The shared instance.</summary>
    public static readonly NaturalNameComparer Instance = new NaturalNameComparer();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x == null)
        {
            return -1;
        }

        if (y == null)
        {
            return 1;
        }

        int i = 0;
        int j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int startX = i;
                int startY = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }

                var digitsX = x.Substring(startX, i - startX).TrimStart('0');
                var digitsY = y.Substring(startY, j - startY).TrimStart('0');
                if (digitsX.Length != digitsY.Length)
                {
                    return digitsX.Length < digitsY.Length ? -1 : 1;
                }

                var byValue = string.CompareOrdinal(digitsX, digitsY);
                if (byValue != 0)
                {
                    return byValue;
                }

                // Same number written with different leading zeros: the shorter spelling first, so the order is stable.
                var spelled = (i - startX).CompareTo(j - startY);
                if (spelled != 0)
                {
                    return spelled;
                }

                continue;
            }

            var cx = char.ToUpperInvariant(x[i]);
            var cy = char.ToUpperInvariant(y[j]);
            if (cx != cy)
            {
                return cx < cy ? -1 : 1;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
