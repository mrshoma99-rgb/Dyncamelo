using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Coordination;

/// <summary>
/// The identity of one clash result the user wants grouped: its Guid and display name, read before any edit
/// can invalidate the wrapper they came from.
/// </summary>
public readonly struct ClashResultKey
{
    /// <summary>Creates a key.</summary>
    /// <param name="guid">The result's Guid (<see cref="Guid.Empty"/> when it has none).</param>
    /// <param name="name">The result's display name (null reads as empty).</param>
    public ClashResultKey(Guid guid, string? name)
    {
        Guid = guid;
        Name = name ?? string.Empty;
    }

    /// <summary>The result's Guid, or <see cref="Guid.Empty"/>.</summary>
    public Guid Guid { get; }

    /// <summary>The result's display name; never null.</summary>
    public string Name { get; }

    /// <summary>
    /// Whether a result of the tree is the one this key stands for: by Guid when both sides have one, otherwise
    /// by (non-empty) display name. The Navisworks API hands out a new wrapper object per access, so identity
    /// is never a reference comparison.
    /// </summary>
    /// <param name="candidateGuid">The tree result's Guid.</param>
    /// <param name="candidateName">The tree result's display name.</param>
    /// <returns>True when the candidate is the wanted result.</returns>
    public bool Matches(Guid candidateGuid, string? candidateName)
    {
        if (Guid != Guid.Empty && candidateGuid != Guid.Empty)
        {
            return candidateGuid == Guid;
        }

        return Name.Length > 0 && string.Equals(candidateName, Name, StringComparison.Ordinal);
    }
}

/// <summary>What a child of a clash test (or of one of its result groups) is.</summary>
public enum ClashTreeEntryKind
{
    /// <summary>Something that is neither a clash result nor a result group (never moved, but it takes a position).</summary>
    Other,

    /// <summary>An individual clash result.</summary>
    Result,

    /// <summary>A result group; its members are listed in <see cref="ClashTreeEntry.Members"/>.</summary>
    Group,
}

/// <summary>
/// One child of a clash test as read from the tree: a loose result, a result group with its members, or
/// something else that only takes a position. A plain description of the tree (no Navisworks types), so the
/// grouping logic can be unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class ClashTreeEntry
{
    private static readonly IReadOnlyList<ClashTreeEntry> NoMembers = new ClashTreeEntry[0];

    private ClashTreeEntry(ClashTreeEntryKind kind, Guid guid, string? name, IReadOnlyList<ClashTreeEntry> members)
    {
        Kind = kind;
        Guid = guid;
        Name = name;
        Members = members;
    }

    /// <summary>What the child is.</summary>
    public ClashTreeEntryKind Kind { get; }

    /// <summary>The Guid of a result or group (<see cref="Guid.Empty"/> when it has none).</summary>
    public Guid Guid { get; }

    /// <summary>The display name of a result or group (null when it has none).</summary>
    public string? Name { get; }

    /// <summary>The children of a group, in order (empty for anything else).</summary>
    public IReadOnlyList<ClashTreeEntry> Members { get; }

    /// <summary>A child that is not a result or a group.</summary>
    /// <returns>The entry.</returns>
    public static ClashTreeEntry Other() => new ClashTreeEntry(ClashTreeEntryKind.Other, Guid.Empty, null, NoMembers);

    /// <summary>An individual clash result.</summary>
    /// <param name="guid">The result's Guid.</param>
    /// <param name="name">The result's display name.</param>
    /// <returns>The entry.</returns>
    public static ClashTreeEntry Result(Guid guid, string? name) => new ClashTreeEntry(ClashTreeEntryKind.Result, guid, name, NoMembers);

    /// <summary>A result group.</summary>
    /// <param name="guid">The group's Guid (<see cref="Guid.Empty"/> when it has none).</param>
    /// <param name="name">The group's display name.</param>
    /// <param name="members">The group's children, in order.</param>
    /// <returns>The entry.</returns>
    public static ClashTreeEntry Group(Guid guid, string? name, IReadOnlyList<ClashTreeEntry> members)
    {
        return new ClashTreeEntry(ClashTreeEntryKind.Group, guid, name, members ?? NoMembers);
    }
}

/// <summary>One result to move into the target group.</summary>
public sealed class ClashPlannedMove
{
    internal ClashPlannedMove(ClashResultKey key, int sourceTop, int sourceMember, bool fromAnotherGroup)
    {
        Key = key;
        SourceTop = sourceTop;
        SourceMember = sourceMember;
        FromAnotherGroup = fromAnotherGroup;
    }

    /// <summary>The wanted result.</summary>
    public ClashResultKey Key { get; }

    /// <summary>
    /// Where the result sits among the test's children when the tree was read: its own index for a loose
    /// result, the index of its group otherwise.
    /// </summary>
    public int SourceTop { get; }

    /// <summary>The result's index inside its group when the tree was read, or -1 for a loose result.</summary>
    public int SourceMember { get; }

    /// <summary>True when the result comes out of another group (a "moved" result), false for a loose one (an "added" one).</summary>
    public bool FromAnotherGroup { get; }
}

/// <summary>What <see cref="ClashGroupPlanner.Plan"/> decided.</summary>
public sealed class ClashGroupPlan
{
    internal ClashGroupPlan(List<ClashPlannedMove> moves, int targetTop, Guid targetGuid, int skipped, int missing)
    {
        Moves = moves;
        TargetTop = targetTop;
        TargetGuid = targetGuid;
        Skipped = skipped;
        Missing = missing;
    }

    /// <summary>The results to move, in the order they were asked for (that is the order they end up in the group).</summary>
    public IReadOnlyList<ClashPlannedMove> Moves { get; }

    /// <summary>Where the target group sits among the test's children when the tree was read.</summary>
    public int TargetTop { get; }

    /// <summary>The Guid of the target group (<see cref="Guid.Empty"/> when it has none): with several groups of the same name only this one is the target.</summary>
    public Guid TargetGuid { get; }

    /// <summary>Wanted results left where they are because they sit in another group and moving was not asked for.</summary>
    public int Skipped { get; }

    /// <summary>Wanted results that are not in the tree at all.</summary>
    public int Missing { get; }
}

/// <summary>
/// The decision half of Clash.GroupResults: given the tree as it was read ONCE and the list of wanted results,
/// it finds where each of them sits (by Guid or by name, through dictionaries instead of a scan of the tree per
/// result) and says which to move. Pure (no Navisworks types), so it is unit-tested; the same decisions the
/// node made by re-scanning the whole tree for every result, in a single pass.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashGroupPlanner
{
    /// <summary>Plans the moves of the wanted results into the first group called <paramref name="groupName"/>.</summary>
    /// <param name="children">The children of the test, in order, as they are now.</param>
    /// <param name="groupName">The target group's name; the group must be among the children.</param>
    /// <param name="moveExisting">True pulls results out of other groups; false counts them as skipped.</param>
    /// <param name="wanted">The wanted results, in the order they are to end up in the group.</param>
    /// <returns>The moves and the counts of skipped and missing results.</returns>
    /// <exception cref="InvalidOperationException">The target group is not among the children.</exception>
    public static ClashGroupPlan Plan(
        IReadOnlyList<ClashTreeEntry> children,
        string groupName,
        bool moveExisting,
        IReadOnlyList<ClashResultKey> wanted)
    {
        if (children == null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        if (wanted == null)
        {
            throw new ArgumentNullException(nameof(wanted));
        }

        // One pass over the tree: every result gets a slot (in tree order), and dictionaries find slots by Guid or name.
        var slots = new List<Slot>();
        var byGuid = new Dictionary<Guid, List<int>>();
        var byName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byNameWithoutGuid = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        int targetTop = -1;
        var targetGuid = Guid.Empty;
        for (int top = 0; top < children.Count; top++)
        {
            var child = children[top];
            if (child.Kind == ClashTreeEntryKind.Result)
            {
                AddSlot(slots, byGuid, byName, byNameWithoutGuid, new Slot(top, -1, child.Guid, child.Name, false));
            }
            else if (child.Kind == ClashTreeEntryKind.Group)
            {
                var isTarget = string.Equals(child.Name, groupName, StringComparison.Ordinal);
                if (isTarget && targetTop < 0)
                {
                    targetTop = top;
                    targetGuid = child.Guid;
                }

                for (int member = 0; member < child.Members.Count; member++)
                {
                    var entry = child.Members[member];
                    if (entry.Kind == ClashTreeEntryKind.Result)
                    {
                        AddSlot(slots, byGuid, byName, byNameWithoutGuid, new Slot(top, member, entry.Guid, entry.Name, isTarget));
                    }
                }
            }
        }

        if (targetTop < 0)
        {
            throw new InvalidOperationException(
                "The group '" + groupName + "' disappeared while results were being moved into it.");
        }

        var moves = new List<ClashPlannedMove>();
        var claimed = new HashSet<int>();
        int skipped = 0, missing = 0;
        foreach (var key in wanted)
        {
            var found = false;
            var slotIndex = -1;
            foreach (var candidate in Candidates(key, byGuid, byName, byNameWithoutGuid))
            {
                found = true;
                if (!claimed.Contains(candidate))
                {
                    slotIndex = candidate;
                    break;
                }
            }

            if (!found)
            {
                missing++;
                continue;
            }

            if (slotIndex < 0)
            {
                continue; // every match was already handled (the same result wired twice): nothing more to do
            }

            var slot = slots[slotIndex];
            if (slot.InTarget)
            {
                claimed.Add(slotIndex);
                continue; // already where it belongs — re-runs stay clean
            }

            var fromAnotherGroup = slot.Member >= 0;
            if (fromAnotherGroup && !moveExisting)
            {
                skipped++;
                continue;
            }

            claimed.Add(slotIndex);
            moves.Add(new ClashPlannedMove(key, slot.Top, slot.Member, fromAnotherGroup));
        }

        return new ClashGroupPlan(moves, targetTop, targetGuid, skipped, missing);
    }

    private readonly struct Slot
    {
        internal Slot(int top, int member, Guid guid, string? name, bool inTarget)
        {
            Top = top;
            Member = member;
            Guid = guid;
            Name = name;
            InTarget = inTarget;
        }

        internal int Top { get; }

        internal int Member { get; }

        internal Guid Guid { get; }

        internal string? Name { get; }

        internal bool InTarget { get; }
    }

    private static void AddSlot(
        List<Slot> slots,
        Dictionary<Guid, List<int>> byGuid,
        Dictionary<string, List<int>> byName,
        Dictionary<string, List<int>> byNameWithoutGuid,
        Slot slot)
    {
        var index = slots.Count;
        slots.Add(slot);
        if (slot.Guid != Guid.Empty)
        {
            Append(byGuid, slot.Guid, index);
        }

        if (!string.IsNullOrEmpty(slot.Name))
        {
            Append(byName, slot.Name!, index);
            if (slot.Guid == Guid.Empty)
            {
                Append(byNameWithoutGuid, slot.Name!, index);
            }
        }
    }

    private static void Append<TKey>(Dictionary<TKey, List<int>> map, TKey key, int index)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<int>();
            map[key] = list;
        }

        list.Add(index);
    }

    // The slots a key matches, in tree order — the same answer as testing every result of the tree with
    // ClashResultKey.Matches, without the scan: a key with a Guid matches the results with that Guid and, by name,
    // those that have no Guid; a key without one matches by name alone.
    private static IEnumerable<int> Candidates(
        ClashResultKey key,
        Dictionary<Guid, List<int>> byGuid,
        Dictionary<string, List<int>> byName,
        Dictionary<string, List<int>> byNameWithoutGuid)
    {
        List<int>? first = null;
        List<int>? second = null;
        if (key.Guid != Guid.Empty)
        {
            byGuid.TryGetValue(key.Guid, out first);
            if (key.Name.Length > 0)
            {
                byNameWithoutGuid.TryGetValue(key.Name, out second);
            }
        }
        else if (key.Name.Length > 0)
        {
            byName.TryGetValue(key.Name, out first);
        }

        if (first == null && second == null)
        {
            yield break;
        }

        if (second == null || first == null)
        {
            foreach (var index in first ?? second!)
            {
                yield return index;
            }

            yield break;
        }

        // Merge the two ascending lists (no slot is in both: one needs a Guid, the other has none).
        int a = 0, b = 0;
        while (a < first.Count || b < second.Count)
        {
            if (b >= second.Count || (a < first.Count && first[a] < second[b]))
            {
                yield return first[a++];
            }
            else
            {
                yield return second[b++];
            }
        }
    }
}
