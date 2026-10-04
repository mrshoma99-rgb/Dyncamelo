using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The pure rules of the saved-tree folder nodes (SelectionSets.InFolder, SortFolder, RenameFolder, DeleteFolder and the folder input of the
/// set creators): how a folder path such as "Walls/Level 1" is read, and in which order a sort puts the entries of a folder. Pure (no
/// Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class SavedTreePaths
{
    /// <summary>
    /// Splits a folder path at "/" (a backslash counts too), trimming each name and dropping empty ones.
    /// </summary>
    /// <param name="path">The path, such as "Walls/Level 1"; null or blank gives no names (the top level).</param>
    /// <returns>The folder names, outermost first.</returns>
    public static List<string> Split(string? path)
    {
        if (path == null)
        {
            return new List<string>();
        }

        return path.Split('/', '\\')
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>True when the text names a nested folder ("A/B") rather than one folder.</summary>
    /// <param name="path">The text.</param>
    public static bool IsPath(string? path) => Split(path).Count > 1;

    /// <summary>
    /// The order a sort puts the entries of a folder in: by name, ignoring case, entries with the same name keeping the order they have
    /// now (a stable sort). Returns the current positions in their new order.
    /// </summary>
    /// <param name="names">The entries' names in their current order (null counts as empty).</param>
    /// <returns>For each new position, the position the entry has now.</returns>
    public static List<int> SortedOrder(IReadOnlyList<string?> names)
    {
        return Enumerable.Range(0, names.Count)
            .OrderBy(index => names[index] ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(index => index)
            .ToList();
    }

    /// <summary>
    /// The moves that turn the current order into <paramref name="order"/>, when every move takes an entry to the FRONT of the folder
    /// (the one position the saved-tree Move call treats unambiguously). The longest tail of <paramref name="order"/> that is already in
    /// the right relative order stays where it is; the entries before it are moved to the front, last one first. A folder that is already
    /// in order needs no move at all.
    /// </summary>
    /// <param name="order">The new order, as returned by <see cref="SortedOrder"/> (positions in the current order).</param>
    /// <returns>The moves as (from, to) positions in the order the folder has at that moment: apply them one by one.</returns>
    public static List<KeyValuePair<int, int>> MovesToFront(IReadOnlyList<int> order)
    {
        var count = order.Count;
        var moves = new List<KeyValuePair<int, int>>();
        if (count < 2)
        {
            return moves;
        }

        var tail = count - 1;
        while (tail > 0 && order[tail - 1] < order[tail])
        {
            tail--;
        }

        var current = Enumerable.Range(0, count).ToList();
        for (var target = tail - 1; target >= 0; target--)
        {
            var from = current.IndexOf(order[target]);
            if (from > 0)
            {
                moves.Add(new KeyValuePair<int, int>(from, 0));
                var moved = current[from];
                current.RemoveAt(from);
                current.Insert(0, moved);
            }
        }

        return moves;
    }
}
