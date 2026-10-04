using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The list-subtraction core behind Selection.Remove. The reference is what the node used to do: call Remove for every request in
/// turn, each taking out the first entry equal to it. Two wrappers for one model item are different objects, so the tests use
/// wrappers compared by a key, the way the node compares model items.
/// </summary>
public class ListRemovalTests
{
    private sealed class Wrapper
    {
        public Wrapper(int key, int serial)
        {
            Key = key;
            Serial = serial;
        }

        public int Key { get; }

        public int Serial { get; }
    }

    private sealed class ByKey : IEqualityComparer<Wrapper>
    {
        public int Calls { get; private set; }

        public bool Equals(Wrapper? x, Wrapper? y)
        {
            Calls++;
            return x != null && y != null && x.Key == y.Key;
        }

        public int GetHashCode(Wrapper item)
        {
            Calls++;
            return item.Key;
        }
    }

    private static List<Wrapper> Wrappers(params int[] keys) => keys.Select((key, i) => new Wrapper(key, i)).ToList();

    private static string Show(IEnumerable<Wrapper> items) => string.Join(",", items.Select(w => w.Key + "#" + w.Serial));

    // The old behaviour: Remove for every request, the first matching entry each time.
    private static List<Wrapper> OneAtATime(List<Wrapper> source, IEnumerable<Wrapper> requests, out int removed)
    {
        var copy = new List<Wrapper>(source);
        removed = 0;
        foreach (var request in requests)
        {
            var index = copy.FindIndex(w => w.Key == request.Key);
            if (index >= 0)
            {
                copy.RemoveAt(index);
                removed++;
            }
        }

        return copy;
    }

    [Fact]
    public void TakesOutTheRequestedItemsAndKeepsTheOrderOfTheRest()
    {
        var source = Wrappers(1, 2, 3, 4, 5);
        var kept = ListRemoval.RemoveFirstOfEach(source, Wrappers(4, 2), new ByKey(), out var removed);

        Assert.Equal("1#0,3#2,5#4", Show(kept));
        Assert.Equal(2, removed);
        Assert.Equal(5, source.Count); // the list is not changed
    }

    [Fact]
    public void ARequestForAnItemThatIsNotThereIsIgnored_AndNothingRemovedKeepsEverything()
    {
        var source = Wrappers(1, 2, 3);
        var kept = ListRemoval.RemoveFirstOfEach(source, Wrappers(9, 8), new ByKey(), out var removed);
        Assert.Equal("1#0,2#1,3#2", Show(kept));
        Assert.Equal(0, removed);

        var none = ListRemoval.RemoveFirstOfEach(source, new List<Wrapper>(), new ByKey(), out removed);
        Assert.Equal("1#0,2#1,3#2", Show(none));
        Assert.Equal(0, removed);
        Assert.NotSame(source, none);

        Assert.Empty(ListRemoval.RemoveFirstOfEach(new List<Wrapper>(), Wrappers(1), new ByKey(), out removed));
        Assert.Equal(0, removed);
    }

    [Fact]
    public void EachRequestTakesOutTheFirstMatchingEntryOnly()
    {
        // Entries 2#1 and 2#3 are the same item; one request removes the first, a second request the next, a third finds nothing.
        var source = Wrappers(1, 2, 3, 2, 4);

        Assert.Equal("1#0,3#2,2#3,4#4", Show(ListRemoval.RemoveFirstOfEach(source, Wrappers(2), new ByKey(), out var removed)));
        Assert.Equal(1, removed);

        Assert.Equal("1#0,3#2,4#4", Show(ListRemoval.RemoveFirstOfEach(source, Wrappers(2, 2), new ByKey(), out removed)));
        Assert.Equal(2, removed);

        Assert.Equal("1#0,3#2,4#4", Show(ListRemoval.RemoveFirstOfEach(source, Wrappers(2, 2, 2, 2), new ByKey(), out removed)));
        Assert.Equal(2, removed);
    }

    [Fact]
    public void DifferentWrappersOfOneItemAreTheSameItem()
    {
        var inList = new Wrapper(7, 100);
        var other = new Wrapper(7, 200);
        var kept = ListRemoval.RemoveFirstOfEach(new[] { new Wrapper(1, 0), inList }, new[] { other }, new ByKey(), out var removed);
        Assert.Equal(1, removed);
        Assert.Equal(new[] { 1 }, kept.Select(w => w.Key));
    }

    [Fact]
    public void NullRequestsAreIgnored_AndNullArgumentsThrow()
    {
        var kept = ListRemoval.RemoveFirstOfEach(Wrappers(1, 2), new Wrapper?[] { null, new Wrapper(2, 9) }!, new ByKey(), out var removed);
        Assert.Equal("1#0", Show(kept));
        Assert.Equal(1, removed);

        Assert.Throws<ArgumentNullException>(() => ListRemoval.RemoveFirstOfEach(null!, Wrappers(1), new ByKey(), out _));
        Assert.Throws<ArgumentNullException>(() => ListRemoval.RemoveFirstOfEach(Wrappers(1), null!, new ByKey(), out _));
        Assert.Throws<ArgumentNullException>(() => ListRemoval.RemoveFirstOfEach(Wrappers(1), Wrappers(1), null!, out _));
    }

    [Fact]
    public void RandomListsGiveTheSameResultAsRemovingOneAtATime()
    {
        var random = new Random(11);
        for (int round = 0; round < 300; round++)
        {
            var distinct = random.Next(1, 40);
            var source = Wrappers(Enumerable.Range(0, random.Next(0, 120)).Select(_ => random.Next(distinct)).ToArray());
            var requests = Wrappers(Enumerable.Range(0, random.Next(0, 80)).Select(_ => random.Next(distinct + 5)).ToArray());

            var expected = OneAtATime(source, requests, out var expectedRemoved);
            var actual = ListRemoval.RemoveFirstOfEach(source, requests, new ByKey(), out var actualRemoved);

            Assert.Equal(Show(expected), Show(actual));
            Assert.Equal(expectedRemoved, actualRemoved);
        }
    }

    [Fact]
    public void TakingTwentyThousandOutOfAHundredThousandAsksTheComparerLinearlyOften()
    {
        var source = Wrappers(Enumerable.Range(0, 100000).ToArray());
        var requests = Wrappers(Enumerable.Range(0, 100000).Where(i => i % 5 == 0).ToArray());
        var comparer = new ByKey();

        var kept = ListRemoval.RemoveFirstOfEach(source, requests, comparer, out var removed);

        Assert.Equal(20000, removed);
        Assert.Equal(80000, kept.Count);
        Assert.DoesNotContain(kept, w => w.Key % 5 == 0);
        // The one-at-a-time version made up to 20 000 x 100 000 comparisons; a hash makes a few per entry.
        Assert.True(comparer.Calls < 10 * (source.Count + requests.Count), "comparer calls: " + comparer.Calls);
        // And the order is the list's order.
        Assert.True(kept.Zip(kept.Skip(1), (a, b) => a.Serial < b.Serial).All(ordered => ordered));
    }
}
