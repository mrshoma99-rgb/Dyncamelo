using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Nodes.Coordination;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// The live tree of one stored clash test, edited in place (<c>TestsMove</c>), for
/// <see cref="ClashGroupRunner"/>. The stored test is looked up again on every call — a wrapper kept from
/// before an edit may no longer show the tree as it is — and every result is addressed by its index; a result is
/// only moved after the child at that index has been checked to be the one that was planned.
/// Internal — never surfaced as a node.
/// </summary>
internal sealed class StoredClashTreeEditor : IClashTreeEditor
{
    private readonly DocumentClash _clash;
    private readonly Guid _testGuid;
    private readonly string _testName;
    private readonly string _groupName;

    internal StoredClashTreeEditor(DocumentClash clash, Guid testGuid, string testName, string groupName)
    {
        _clash = clash;
        _testGuid = testGuid;
        _testName = testName;
        _groupName = groupName;
    }

    /// <inheritdoc />
    public IReadOnlyList<ClashTreeEntry> ReadChildren()
    {
        var children = Relocate().Children;
        var entries = new List<ClashTreeEntry>(children.Count);
        foreach (var child in children)
        {
            if (child is ClashResult result)
            {
                entries.Add(ClashTreeEntry.Result(result.Guid, result.DisplayName));
            }
            else if (child is ClashResultGroup group)
            {
                var members = new List<ClashTreeEntry>(group.Children.Count);
                foreach (var member in group.Children)
                {
                    members.Add(member is ClashResult memberResult
                        ? ClashTreeEntry.Result(memberResult.Guid, memberResult.DisplayName)
                        : ClashTreeEntry.Other());
                }

                entries.Add(ClashTreeEntry.Group(group.Guid, group.DisplayName, members));
            }
            else
            {
                entries.Add(ClashTreeEntry.Other());
            }
        }

        return entries;
    }

    /// <inheritdoc />
    public bool TryMove(int sourceTop, int sourceMember, int targetTop, Guid targetGuid, ClashResultKey key)
    {
        var stored = Relocate();
        var children = stored.Children;
        var count = children.Count;

        if (targetTop < 0 || targetTop >= count ||
            !(children[targetTop] is ClashResultGroup target) ||
            !string.Equals(target.DisplayName, _groupName, StringComparison.Ordinal) ||
            (targetGuid != Guid.Empty && target.Guid != targetGuid))
        {
            return false;
        }

        GroupItem parent;
        SavedItem item;
        int index;
        if (sourceMember < 0)
        {
            if (sourceTop < 0 || sourceTop >= count)
            {
                return false;
            }

            parent = stored;
            index = sourceTop;
            item = children[sourceTop];
        }
        else
        {
            if (sourceTop < 0 || sourceTop >= count || !(children[sourceTop] is ClashResultGroup source))
            {
                return false;
            }

            if (sourceMember >= source.Children.Count)
            {
                return false;
            }

            parent = source;
            index = sourceMember;
            item = source.Children[sourceMember];
        }

        if (!(item is ClashResult result) || !key.Matches(result.Guid, result.DisplayName))
        {
            return false;
        }

        _clash.TestsData.TestsMove(parent, index, target, target.Children.Count);
        return true;
    }

    private ClashTest Relocate()
    {
        return ClashHelpers.FindStoredTest(_clash, _testGuid, _testName)
            ?? throw new InvalidOperationException(
                "The clash test '" + _testName + "' disappeared while its results were being grouped.");
    }
}
