using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// Which comments of a thread SavedItem.SetCommentStatus changes: the one at a position, or all of them. Pure (no Navisworks
/// types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class CommentSelection
{
    /// <summary>The positions of the comments to change.</summary>
    /// <param name="count">How many comments the thread has.</param>
    /// <param name="index">The 0-based position of one comment, or -1 (any negative number) for every comment.</param>
    /// <returns>The positions in thread order; empty for an empty thread.</returns>
    /// <exception cref="ArgumentException">The position is past the end of the thread.</exception>
    public static List<int> Positions(int count, int index)
    {
        var positions = new List<int>();
        if (index < 0)
        {
            for (var i = 0; i < count; i++)
            {
                positions.Add(i);
            }

            return positions;
        }

        if (index >= count)
        {
            throw new ArgumentException(
                count == 0
                    ? "The item has no comments, so there is no comment " + index.ToString(CultureInfo.InvariantCulture) + " to change."
                    : "The item has " + count.ToString(CultureInfo.InvariantCulture) + " comment(s) (positions 0 to " +
                      (count - 1).ToString(CultureInfo.InvariantCulture) + "), so there is no comment " +
                      index.ToString(CultureInfo.InvariantCulture) + ". Use -1 to change all of them.");
        }

        positions.Add(index);
        return positions;
    }
}
