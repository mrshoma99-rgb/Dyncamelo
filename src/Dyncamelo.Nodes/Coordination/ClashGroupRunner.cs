using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Coordination;

/// <summary>
/// The part of Navisworks' clash tree edit that Clash.GroupResults needs, so the loop around it can be unit-tested
/// against an in-memory tree. The Navisworks implementation re-locates the stored test on every call (a wrapper
/// kept from before an edit may be stale) and reads indexes only.
/// </summary>
public interface IClashTreeEditor
{
    /// <summary>Reads the children of the test as they are now (one pass over the tree).</summary>
    /// <returns>The children, in order.</returns>
    IReadOnlyList<ClashTreeEntry> ReadChildren();

    /// <summary>
    /// Moves one result to the end of the target group, after checking that it really is where the plan says.
    /// </summary>
    /// <param name="sourceTop">The index of the result (loose) or of its group among the test's children, now.</param>
    /// <param name="sourceMember">The index of the result inside its group now, or -1 for a loose result.</param>
    /// <param name="targetTop">The index of the target group among the test's children, now.</param>
    /// <param name="targetGuid">The Guid of the target group (<see cref="Guid.Empty"/> when it has none): the group at <paramref name="targetTop"/> must be that one.</param>
    /// <param name="key">The result that must be at the source place.</param>
    /// <returns>False, with nothing moved, when the tree does not hold what the plan expects at those places.</returns>
    bool TryMove(int sourceTop, int sourceMember, int targetTop, Guid targetGuid, ClashResultKey key);
}

/// <summary>What <see cref="ClashGroupRunner.Run"/> did.</summary>
public sealed class ClashGroupOutcome
{
    /// <summary>Loose results put into the group.</summary>
    public int Added { get; internal set; }

    /// <summary>Results pulled out of other groups into it.</summary>
    public int Moved { get; internal set; }

    /// <summary>Results left in other groups because moving them was not asked for.</summary>
    public int Skipped { get; internal set; }

    /// <summary>Wanted results that are not in the test.</summary>
    public int Missing { get; internal set; }
}

/// <summary>
/// The loop of Clash.GroupResults: read the tree once, plan, then move the results one by one in the order they
/// were asked for, keeping track of how each move renumbers the tree (a removal shifts every later index of its
/// parent by one) so no result has to be searched for again. When the tree turns out not to hold what the plan
/// expected (Navisworks changed something the plan did not foresee, e.g. it drops a group that became empty) the
/// rest is planned again from a fresh read. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashGroupRunner
{
    /// <summary>Moves the wanted results into the named group of the test.</summary>
    /// <param name="editor">The tree to edit.</param>
    /// <param name="testName">The test's name, for the error messages.</param>
    /// <param name="groupName">The target group's name; it must exist already.</param>
    /// <param name="moveExisting">True pulls results out of other groups; false counts them as skipped.</param>
    /// <param name="wanted">The wanted results, in the order they are to end up in the group.</param>
    /// <returns>How many were added, moved, skipped and not found.</returns>
    /// <exception cref="InvalidOperationException">The tree does not hold what it was read to hold, even straight after reading it.</exception>
    public static ClashGroupOutcome Run(
        IClashTreeEditor editor,
        string testName,
        string groupName,
        bool moveExisting,
        IReadOnlyList<ClashResultKey> wanted)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        if (wanted == null)
        {
            throw new ArgumentNullException(nameof(wanted));
        }

        var outcome = new ClashGroupOutcome();
        IReadOnlyList<ClashResultKey> pending = wanted;
        while (true)
        {
            var children = editor.ReadChildren();
            var plan = ClashGroupPlanner.Plan(children, groupName, moveExisting, pending);
            outcome.Skipped += plan.Skipped;
            outcome.Missing += plan.Missing;

            var topRemovals = new RemovalCounter(children.Count);
            var memberRemovals = new Dictionary<int, RemovalCounter>();
            var done = 0;
            for (; done < plan.Moves.Count; done++)
            {
                var move = plan.Moves[done];
                var targetTop = plan.TargetTop - topRemovals.RemovedBefore(plan.TargetTop);
                var sourceTop = move.SourceTop - topRemovals.RemovedBefore(move.SourceTop);
                var sourceMember = -1;
                RemovalCounter? groupRemovals = null;
                if (move.SourceMember >= 0)
                {
                    if (!memberRemovals.TryGetValue(move.SourceTop, out groupRemovals))
                    {
                        groupRemovals = new RemovalCounter(children[move.SourceTop].Members.Count);
                        memberRemovals[move.SourceTop] = groupRemovals;
                    }

                    sourceMember = move.SourceMember - groupRemovals.RemovedBefore(move.SourceMember);
                }

                if (!editor.TryMove(sourceTop, sourceMember, targetTop, plan.TargetGuid, move.Key))
                {
                    break;
                }

                if (groupRemovals != null)
                {
                    groupRemovals.Remove(move.SourceMember);
                }
                else
                {
                    topRemovals.Remove(move.SourceTop);
                }

                if (move.FromAnotherGroup)
                {
                    outcome.Moved++;
                }
                else
                {
                    outcome.Added++;
                }
            }

            if (done == plan.Moves.Count)
            {
                return outcome;
            }

            if (done == 0)
            {
                throw new InvalidOperationException(
                    "The results tree of the clash test '" + testName + "' does not hold what was read from it a moment " +
                    "ago (a result was not at its place) — it was probably changed while the node ran. Run the node again.");
            }

            // Something moved the tree in a way the plan did not foresee: plan what is left from a fresh read.
            var rest = new List<ClashResultKey>(plan.Moves.Count - done);
            for (int index = done; index < plan.Moves.Count; index++)
            {
                rest.Add(plan.Moves[index].Key);
            }

            pending = rest;
        }
    }

    /// <summary>Counts removed positions of a list of known length (a Fenwick tree): how many removed ones lie before a position.</summary>
    private sealed class RemovalCounter
    {
        private readonly int[] _tree;

        internal RemovalCounter(int length)
        {
            _tree = new int[length + 1];
        }

        internal void Remove(int position)
        {
            for (int i = position + 1; i < _tree.Length; i += i & -i)
            {
                _tree[i]++;
            }
        }

        internal int RemovedBefore(int position)
        {
            var count = 0;
            for (int i = Math.Min(position, _tree.Length - 1); i > 0; i -= i & -i)
            {
                count += _tree[i];
            }

            return count;
        }
    }
}
