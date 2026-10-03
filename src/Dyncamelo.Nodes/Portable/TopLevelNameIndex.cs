using System;
using System.Collections.Generic;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// The names of the items of one list (the saved viewpoints of a folder, the selection sets of a folder), kept so that
/// "is there an item with this name, and where" is a dictionary lookup instead of a scan of the list. A node that makes many
/// items in one folder builds it once, then calls <see cref="Append"/> for every item it adds, where it used to search the
/// whole, growing folder twice per item (so the work grew with the square of the number of items). Pure (no Navisworks
/// types), so it is unit-tested.
/// </summary>
public sealed class TopLevelNameIndex
{
    private readonly Dictionary<string, int> _firstByName = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Creates an empty index (an empty list).</summary>
    public TopLevelNameIndex()
    {
    }

    /// <summary>Creates the index of a list that already holds items.</summary>
    /// <param name="namesByPosition">
    /// One entry per item of the list, in order: its name, or null for an item that must never be found by name (an item of
    /// another kind, like a folder among viewpoints) — it still takes up its position.
    /// </param>
    public TopLevelNameIndex(IEnumerable<string?> namesByPosition)
    {
        Reset(namesByPosition);
    }

    /// <summary>The number of items in the list (positions used so far).</summary>
    public int Count { get; private set; }

    /// <summary>The position of the FIRST item with this name (compared exactly, case included).</summary>
    /// <param name="name">The name to find.</param>
    /// <param name="index">The position, or -1.</param>
    /// <returns>True when an item has the name.</returns>
    public bool TryGetIndex(string name, out int index)
    {
        if (name != null && _firstByName.TryGetValue(name, out index))
        {
            return true;
        }

        index = -1;
        return false;
    }

    /// <summary>Forgets everything and indexes the list again (the list was not what the index thought it was).</summary>
    /// <param name="namesByPosition">One entry per item of the list, in order, as for the constructor.</param>
    public void Reset(IEnumerable<string?> namesByPosition)
    {
        if (namesByPosition == null)
        {
            throw new ArgumentNullException(nameof(namesByPosition));
        }

        _firstByName.Clear();
        Count = 0;
        foreach (var name in namesByPosition)
        {
            Append(name);
        }
    }

    /// <summary>Records an item added at the end of the list.</summary>
    /// <param name="name">Its name, or null when it must never be found by name.</param>
    /// <returns>Its position (the list's length before it was added).</returns>
    public int Append(string? name)
    {
        var position = Count++;
        if (name != null && !_firstByName.ContainsKey(name))
        {
            _firstByName[name] = position;
        }

        return position;
    }
}
