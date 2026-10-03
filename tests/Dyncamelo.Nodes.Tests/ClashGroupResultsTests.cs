using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Dyncamelo.Nodes.Coordination;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The logic of Clash.GroupResults that used to scan the whole clash tree for every result it moved: the planner
/// (finds every wanted result through dictionaries after reading the tree once) and the runner (moves them and keeps
/// track of how each move renumbers the tree). Both are compared with a literal copy of the old algorithm on random
/// trees: same tree afterwards, same counts.
/// </summary>
public class ClashGroupResultsTests
{
    private const string Target = "Hotspot";

    // ------------------------------------------------------------ an in-memory clash test

    private sealed class FakeNode
    {
        public ClashTreeEntryKind Kind;
        public Guid Guid;
        public string? Name;
        public List<FakeNode> Members = new List<FakeNode>();

        public static FakeNode Result(Guid guid, string? name) => new FakeNode { Kind = ClashTreeEntryKind.Result, Guid = guid, Name = name };

        public static FakeNode Group(string name, params FakeNode[] members) => new FakeNode { Kind = ClashTreeEntryKind.Group, Guid = Guid.NewGuid(), Name = name, Members = members.ToList() };

        public static FakeNode Other() => new FakeNode { Kind = ClashTreeEntryKind.Other };
    }

    /// <summary>A clash test held in memory that answers like Navisworks' TestsMove: a removal renumbers its parent, a move goes to an index of the target.</summary>
    private sealed class FakeTest : IClashTreeEditor
    {
        public List<FakeNode> Children = new List<FakeNode>();

        /// <summary>Navisworks might drop a result group that became empty; the planner cannot know.</summary>
        public bool DropEmptyGroups;

        public int Reads;
        public int Moves;
        public int Refused;
        public long ChildrenSeen;

        public IReadOnlyList<ClashTreeEntry> ReadChildren()
        {
            Reads++;
            var entries = new List<ClashTreeEntry>();
            foreach (var child in Children)
            {
                ChildrenSeen++;
                switch (child.Kind)
                {
                    case ClashTreeEntryKind.Result:
                        entries.Add(ClashTreeEntry.Result(child.Guid, child.Name));
                        break;
                    case ClashTreeEntryKind.Group:
                        var members = new List<ClashTreeEntry>();
                        foreach (var member in child.Members)
                        {
                            ChildrenSeen++;
                            members.Add(member.Kind == ClashTreeEntryKind.Result ? ClashTreeEntry.Result(member.Guid, member.Name) : ClashTreeEntry.Other());
                        }

                        entries.Add(ClashTreeEntry.Group(child.Guid, child.Name, members));
                        break;
                    default:
                        entries.Add(ClashTreeEntry.Other());
                        break;
                }
            }

            return entries;
        }

        public bool TryMove(int sourceTop, int sourceMember, int targetTop, Guid targetGuid, ClashResultKey key)
        {
            if (targetTop < 0 || targetTop >= Children.Count || Children[targetTop].Kind != ClashTreeEntryKind.Group ||
                !string.Equals(Children[targetTop].Name, Target, StringComparison.Ordinal) ||
                (targetGuid != Guid.Empty && Children[targetTop].Guid != targetGuid))
            {
                Refused++;
                return false;
            }

            var target = Children[targetTop];
            List<FakeNode> parent;
            if (sourceMember < 0)
            {
                if (sourceTop < 0 || sourceTop >= Children.Count)
                {
                    Refused++;
                    return false;
                }

                parent = Children;
            }
            else
            {
                if (sourceTop < 0 || sourceTop >= Children.Count || Children[sourceTop].Kind != ClashTreeEntryKind.Group || sourceMember >= Children[sourceTop].Members.Count)
                {
                    Refused++;
                    return false;
                }

                parent = Children[sourceTop].Members;
            }

            var index = sourceMember < 0 ? sourceTop : sourceMember;
            var item = parent[index];
            if (item.Kind != ClashTreeEntryKind.Result || !key.Matches(item.Guid, item.Name))
            {
                Refused++;
                return false;
            }

            Moves++;
            parent.RemoveAt(index);
            target.Members.Add(item);
            if (DropEmptyGroups && sourceMember >= 0)
            {
                DropIfEmpty(Children, Children[sourceTop]);
            }

            return true;
        }

        public string Describe()
        {
            var text = new StringBuilder();
            foreach (var child in Children)
            {
                if (child.Kind == ClashTreeEntryKind.Result)
                {
                    text.Append("R").Append(Short(child.Guid)).Append(';');
                }
                else if (child.Kind == ClashTreeEntryKind.Group)
                {
                    text.Append("G(").Append(child.Name).Append(':');
                    foreach (var member in child.Members)
                    {
                        text.Append(member.Kind == ClashTreeEntryKind.Result ? Short(member.Guid) : "-").Append(',');
                    }

                    text.Append(");");
                }
                else
                {
                    text.Append("-;");
                }
            }

            return text.ToString();
        }

        public static void DropIfEmpty(List<FakeNode> children, FakeNode group)
        {
            if (group.Members.Count == 0)
            {
                children.Remove(group);
            }
        }
    }

    // The number a test Guid was made from, as the six hex digits that differ.
    private static string Short(Guid guid) => guid.ToString("N").Substring(2, 6);

    private static Guid G(int n) => new Guid(n, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

    private static ClashResultKey K(int n) => new ClashResultKey(G(n), "Clash" + n);

    private static FakeNode R(int n) => FakeNode.Result(G(n), "Clash" + n);

    // The old algorithm, literally: for every wanted result, find the target group, scan the whole tree for the result, move it to the end.
    private static ClashGroupOutcome OldAlgorithm(FakeTest test, bool moveExisting, IReadOnlyList<ClashResultKey> wanted)
    {
        var outcome = new ClashGroupOutcome();
        foreach (var key in wanted)
        {
            var group = test.Children.FirstOrDefault(c => c.Kind == ClashTreeEntryKind.Group && c.Name == Target)
                ?? throw new InvalidOperationException("no target group");

            List<FakeNode>? parent = null;
            var index = -1;
            var inTarget = false;
            var parentIsTest = false;
            for (int i = 0; i < test.Children.Count && parent == null && !inTarget; i++)
            {
                var child = test.Children[i];
                if (child.Kind == ClashTreeEntryKind.Result && key.Matches(child.Guid, child.Name))
                {
                    parent = test.Children;
                    parentIsTest = true;
                    index = i;
                }
                else if (child.Kind == ClashTreeEntryKind.Group)
                {
                    for (int j = 0; j < child.Members.Count; j++)
                    {
                        var member = child.Members[j];
                        if (member.Kind == ClashTreeEntryKind.Result && key.Matches(member.Guid, member.Name))
                        {
                            if (child.Name == Target)
                            {
                                inTarget = true;
                            }
                            else
                            {
                                parent = child.Members;
                                index = j;
                            }

                            break;
                        }
                    }
                }
            }

            if (parent == null && !inTarget)
            {
                outcome.Missing++;
                continue;
            }

            if (inTarget)
            {
                continue;
            }

            if (!parentIsTest && !moveExisting)
            {
                outcome.Skipped++;
                continue;
            }

            var item = parent![index];
            parent.RemoveAt(index);
            group.Members.Add(item);
            if (!parentIsTest && test.DropEmptyGroups)
            {
                var source = test.Children.First(c => c.Members == parent);
                FakeTest.DropIfEmpty(test.Children, source);
            }

            if (parentIsTest)
            {
                outcome.Added++;
            }
            else
            {
                outcome.Moved++;
            }
        }

        return outcome;
    }

    private static ClashGroupOutcome Run(FakeTest test, bool moveExisting, params ClashResultKey[] wanted)
    {
        return ClashGroupRunner.Run(test, "Test 1", Target, moveExisting, wanted);
    }

    // ------------------------------------------------------------ planner

    [Fact]
    public void ALooseResultIsAddedAndOneInAnotherGroupIsMovedOnlyWhenAsked()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Group("Other group", R(2), R(3)));
        test.Children.Add(FakeNode.Group(Target));

        var kept = Run(test, false, K(1), K(2));
        Assert.Equal((1, 0, 1, 0), (kept.Added, kept.Moved, kept.Skipped, kept.Missing));
        Assert.Equal("G(Other group:000002,000003,);G(Hotspot:000001,);", test.Describe());

        var pulled = Run(test, true, K(2));
        Assert.Equal((0, 1, 0, 0), (pulled.Added, pulled.Moved, pulled.Skipped, pulled.Missing));
        Assert.Equal("G(Other group:000003,);G(Hotspot:000001,000002,);", test.Describe());
    }

    [Fact]
    public void ResultsAlreadyInTheGroupAreLeftAloneSoRerunsAreClean()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Group(Target, R(2)));

        var outcome = Run(test, true, K(1), K(2));

        Assert.Equal((1, 0, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
        Assert.Equal("G(Hotspot:000002,000001,);", test.Describe());
        var again = Run(test, true, K(1), K(2));
        Assert.Equal((0, 0, 0, 0), (again.Added, again.Moved, again.Skipped, again.Missing));
    }

    [Fact]
    public void AResultThatIsNotInTheTestIsCountedAsMissing()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Group(Target));

        var outcome = Run(test, false, K(1), K(99), new ClashResultKey(Guid.Empty, "no such result"), new ClashResultKey(Guid.Empty, null));

        Assert.Equal((1, 0, 0, 3), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
    }

    [Fact]
    public void TheSameResultWiredTwiceCountsOnce()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Group("Other", R(2)));
        test.Children.Add(FakeNode.Group(Target));

        var outcome = Run(test, true, K(1), K(2), K(1), K(2));

        Assert.Equal((1, 1, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
    }

    [Fact]
    public void ASkippedResultWiredTwiceIsSkippedTwiceAsBefore()
    {
        var test = new FakeTest();
        test.Children.Add(FakeNode.Group("Other", R(2)));
        test.Children.Add(FakeNode.Group(Target));

        var outcome = Run(test, false, K(2), K(2));

        Assert.Equal(2, outcome.Skipped);
    }

    [Fact]
    public void TheOrderOfTheGroupFollowsTheOrderTheResultsWereAskedFor()
    {
        var test = new FakeTest();
        for (int n = 1; n <= 6; n++)
        {
            test.Children.Add(R(n));
        }

        test.Children.Add(FakeNode.Group(Target, R(10)));

        Run(test, false, K(5), K(2), K(6), K(1));

        Assert.Equal("R000003;R000004;G(Hotspot:00000a,000005,000002,000006,000001,);", test.Describe());
    }

    [Fact]
    public void AResultWithoutAGuidIsFoundByItsName()
    {
        var test = new FakeTest();
        test.Children.Add(FakeNode.Result(Guid.Empty, "Clash7"));
        test.Children.Add(R(8));
        test.Children.Add(FakeNode.Group(Target));

        // by name alone, and a wanted result with a Guid matches a stored one that has none by its name
        var outcome = Run(test, false, new ClashResultKey(Guid.Empty, "Clash8"), new ClashResultKey(G(7), "Clash7"));

        Assert.Equal(2, outcome.Added);
        Assert.Equal("G(Hotspot:000008,000000,);", test.Describe());
    }

    [Fact]
    public void WithAGuidOnBothSidesTheNameIsIgnored()
    {
        var test = new FakeTest();
        test.Children.Add(FakeNode.Result(G(1), "Same name"));
        test.Children.Add(FakeNode.Result(G(2), "Same name"));
        test.Children.Add(FakeNode.Group(Target));

        var outcome = Run(test, false, new ClashResultKey(G(2), "Same name"));

        Assert.Equal(1, outcome.Added);
        Assert.Equal("R000001;G(Hotspot:000002,);", test.Describe());
    }

    [Fact]
    public void WithoutTheTargetGroupThePlanSaysSo()
    {
        var children = new List<ClashTreeEntry> { ClashTreeEntry.Result(G(1), "Clash1") };

        var error = Assert.Throws<InvalidOperationException>(
            () => ClashGroupPlanner.Plan(children, Target, false, new[] { K(1) }));

        Assert.Contains("'" + Target + "'", error.Message);
    }

    [Fact]
    public void TheFirstGroupWithTheNameIsTheTargetAndAResultInASecondOneIsAlreadyThere()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Group(Target));
        test.Children.Add(FakeNode.Group(Target, R(2)));

        var outcome = Run(test, true, K(1), K(2));

        Assert.Equal((1, 0, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
        Assert.Equal("G(Hotspot:000001,);G(Hotspot:000002,);", test.Describe());
    }

    // ------------------------------------------------------------ runner

    [Fact]
    public void ResultsSittingBeforeAndAfterTheGroupAreAllFoundAfterEveryRenumbering()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Other());
        test.Children.Add(FakeNode.Group("Left", R(2), FakeNode.Other(), R(3), R(4)));
        test.Children.Add(R(5));
        test.Children.Add(FakeNode.Group(Target));
        test.Children.Add(R(6));
        test.Children.Add(FakeNode.Group("Right", R(7), R(8)));

        var outcome = Run(test, true, K(8), K(6), K(1), K(4), K(2), K(7), K(5), K(3));

        Assert.Equal((3, 5, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
        Assert.Equal("-;G(Left:-,);G(Hotspot:000008,000006,000001,000004,000002,000007,000005,000003,);G(Right:);", test.Describe());
        Assert.Equal(1, test.Reads);
        Assert.Equal(0, test.Refused);
    }

    [Fact]
    public void WhenNavisworksDropsAnEmptiedGroupTheRestIsPlannedAgainFromAFreshRead()
    {
        var test = new FakeTest { DropEmptyGroups = true };
        test.Children.Add(FakeNode.Group("A", R(1)));
        test.Children.Add(FakeNode.Group("B", R(2), R(3)));
        test.Children.Add(FakeNode.Group(Target));
        test.Children.Add(R(4));
        test.Children.Add(FakeNode.Group("C", R(5)));

        var outcome = Run(test, true, K(1), K(2), K(3), K(4), K(5));

        Assert.Equal((1, 4, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
        Assert.Equal("G(Hotspot:000001,000002,000003,000004,000005,);", test.Describe());
        Assert.True(test.Reads > 1, "the tree should have been read again");
    }

    [Fact]
    public void AGroupDroppedBeforeTheTargetDoesNotSendTheRestToASecondGroupOfTheSameName()
    {
        var test = new FakeTest { DropEmptyGroups = true };
        test.Children.Add(FakeNode.Group("A", R(1)));
        test.Children.Add(FakeNode.Group(Target));
        test.Children.Add(FakeNode.Group(Target));
        test.Children.Add(R(2));

        var outcome = Run(test, true, K(1), K(2));

        Assert.Equal((1, 1, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
        Assert.Equal("G(Hotspot:000001,000002,);G(Hotspot:);", test.Describe());
    }

    [Fact]
    public void ATreeThatDoesNotMatchEvenRightAfterTheReadIsReportedNotLoopedOn()
    {
        var test = new RefusingEditor(new FakeTest());
        test.Inner.Children.Add(R(1));
        test.Inner.Children.Add(FakeNode.Group(Target));

        var error = Assert.Throws<InvalidOperationException>(
            () => ClashGroupRunner.Run(test, "Test 1", Target, false, new[] { K(1) }));

        Assert.Contains("Test 1", error.Message);
    }

    private sealed class RefusingEditor : IClashTreeEditor
    {
        public RefusingEditor(FakeTest inner)
        {
            Inner = inner;
        }

        public FakeTest Inner { get; }

        public IReadOnlyList<ClashTreeEntry> ReadChildren() => Inner.ReadChildren();

        public bool TryMove(int sourceTop, int sourceMember, int targetTop, Guid targetGuid, ClashResultKey key) => false;
    }

    [Fact]
    public void NothingWantedChangesNothing()
    {
        var test = new FakeTest();
        test.Children.Add(R(1));
        test.Children.Add(FakeNode.Group(Target));

        var outcome = Run(test, true);

        Assert.Equal((0, 0, 0, 0), (outcome.Added, outcome.Moved, outcome.Skipped, outcome.Missing));
        Assert.Equal(0, test.Moves);
    }

    // ------------------------------------------------------------ same answer as the old algorithm

    private static FakeTest RandomTest(Random random, bool withNameFallbacks, bool dropEmptyGroups, out List<ClashResultKey> wanted)
    {
        var test = new FakeTest { DropEmptyGroups = dropEmptyGroups };
        var next = 1;

        FakeNode NewResult()
        {
            var n = next++;
            var guid = withNameFallbacks && random.Next(5) == 0 ? Guid.Empty : G(n);

            // A result with a Guid may share its name with others (the Guid decides); one without a Guid needs a name of its own.
            var name = guid != Guid.Empty && random.Next(4) == 0 ? "Shared" : "Clash" + n;
            return FakeNode.Result(guid, name);
        }

        var topCount = random.Next(0, 25);
        var targetAt = random.Next(0, topCount + 1);
        for (int i = 0; i <= topCount; i++)
        {
            if (i == targetAt)
            {
                var existing = FakeNode.Group(Target);
                for (int m = random.Next(0, 4); m > 0; m--)
                {
                    existing.Members.Add(NewResult());
                }

                test.Children.Add(existing);
            }

            switch (random.Next(6))
            {
                case 0:
                    var group = FakeNode.Group(random.Next(8) == 0 ? Target : "Group " + random.Next(4));
                    for (int m = random.Next(0, 6); m > 0; m--)
                    {
                        group.Members.Add(random.Next(8) == 0 ? FakeNode.Other() : NewResult());
                    }

                    test.Children.Add(group);
                    break;
                case 1:
                    test.Children.Add(FakeNode.Other());
                    break;
                default:
                    test.Children.Add(NewResult());
                    break;
            }
        }

        // what is asked for: a random sample of the results in a random order, some twice, some unknown
        wanted = new List<ClashResultKey>();
        var flat = new List<FakeNode>();
        foreach (var child in test.Children)
        {
            if (child.Kind == ClashTreeEntryKind.Result)
            {
                flat.Add(child);
            }
            else if (child.Kind == ClashTreeEntryKind.Group)
            {
                flat.AddRange(child.Members.Where(c => c.Kind == ClashTreeEntryKind.Result));
            }
        }

        var count = flat.Count == 0 ? 0 : random.Next(0, flat.Count * 2);
        for (int i = 0; i < count; i++)
        {
            var pick = flat[random.Next(flat.Count)];
            switch (random.Next(10))
            {
                case 0:
                    wanted.Add(new ClashResultKey(G(1000 + random.Next(50)), "Unknown " + random.Next(50)));
                    break;
                case 1 when withNameFallbacks && pick.Name != "Shared":
                    wanted.Add(new ClashResultKey(Guid.Empty, pick.Name));
                    break;
                default:
                    wanted.Add(new ClashResultKey(pick.Guid, pick.Name));
                    break;
            }
        }

        return test;
    }

    private static FakeTest Copy(FakeTest test)
    {
        var copy = new FakeTest { DropEmptyGroups = test.DropEmptyGroups };
        FakeNode Clone(FakeNode n) => new FakeNode { Kind = n.Kind, Guid = n.Guid, Name = n.Name, Members = n.Members.Select(Clone).ToList() };
        copy.Children = test.Children.Select(Clone).ToList();
        return copy;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnRandomTreesTheResultIsTheSameAsTheOldScanPerResult(bool withNameFallbacks, bool dropEmptyGroups)
    {
        var checkedCases = 0;
        for (int seed = 0; seed < 600; seed++)
        {
            var random = new Random(seed);
            var original = RandomTest(random, withNameFallbacks, dropEmptyGroups, out var wanted);
            if (!original.Children.Any(c => c.Kind == ClashTreeEntryKind.Group && c.Name == Target))
            {
                continue; // the node creates the group before it moves anything
            }

            foreach (var moveExisting in new[] { false, true })
            {
                var oldTree = Copy(original);
                var newTree = Copy(original);

                var oldOutcome = OldAlgorithm(oldTree, moveExisting, wanted);
                var newOutcome = ClashGroupRunner.Run(newTree, "Test 1", Target, moveExisting, wanted);

                var label = "seed " + seed + (moveExisting ? " moveExisting" : string.Empty);
                Assert.True(oldTree.Describe() == newTree.Describe(), label + ": " + oldTree.Describe() + " != " + newTree.Describe());
                Assert.True(
                    (oldOutcome.Added, oldOutcome.Moved, oldOutcome.Skipped, oldOutcome.Missing) == (newOutcome.Added, newOutcome.Moved, newOutcome.Skipped, newOutcome.Missing),
                    label + ": the counts differ");

                // Without a surprise from Navisworks the plan holds: one read of the tree, no move refused.
                if (!dropEmptyGroups)
                {
                    Assert.True(newTree.Reads == 1 && newTree.Refused == 0, label + ": " + newTree.Reads + " reads, " + newTree.Refused + " refused moves");
                }

                checkedCases++;
            }
        }

        Assert.True(checkedCases > 400, "only " + checkedCases + " cases were compared");
    }

    // ------------------------------------------------------------ it is no longer a scan per result

    [Fact]
    public void ThousandsOfResultsAreMovedWithOneReadOfTheTreeAndOneMovePerResult()
    {
        const int count = 20000;
        var test = new FakeTest();
        for (int n = 1; n <= count; n++)
        {
            test.Children.Add(n % 5 == 0 ? FakeNode.Group("Group " + (n % 7), R(n), R(n + count)) : R(n));
        }

        test.Children.Add(FakeNode.Group(Target));
        var wanted = new List<ClashResultKey>();
        for (int n = 1; n <= count; n++)
        {
            wanted.Add(K(n));
        }

        var clock = Stopwatch.StartNew();
        var outcome = ClashGroupRunner.Run(test, "Test 1", Target, true, wanted);
        clock.Stop();

        Assert.Equal(count, outcome.Added + outcome.Moved);
        Assert.Equal(count, test.Moves);
        Assert.Equal(1, test.Reads);
        Assert.Equal(0, test.Refused);
        // The tree was looked at once (about 1.5 entries per result), not once per result.
        Assert.True(test.ChildrenSeen < 10L * count, "children seen: " + test.ChildrenSeen);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "took " + clock.Elapsed);
    }
}
