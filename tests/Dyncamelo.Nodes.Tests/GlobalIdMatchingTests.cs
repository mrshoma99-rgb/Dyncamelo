using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Nodes.Portable;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The pure half of BCF.ImportIssues' GlobalId fallback: batching the unmatched GUIDs for one search each, and giving the items of a
/// combined (OR groups) search back to the GUIDs they matched, with the same result as one search per GUID.
/// </summary>
public class GlobalIdMatchingTests
{
    private sealed class Item
    {
        public Item(int serial, string? globalId)
        {
            Serial = serial;
            GlobalId = globalId;
        }

        public int Serial { get; }

        public string? GlobalId { get; }

        public override string ToString() => Serial + ":" + GlobalId;
    }

    private static string? Read(Item item) => item.GlobalId;

    private static string Show(IEnumerable<Item> items) => string.Join(",", items.Select(i => i.Serial));

    // ── Batch ────────────────────────────────────────────────────────────────

    [Fact]
    public void Batch_SplitsInOrderIntoBatchesOfAtMostTheSize()
    {
        var guids = Enumerable.Range(0, 7).Select(i => "g" + i).ToList();

        var batches = GlobalIdMatching.Batch(guids, 3);

        Assert.Equal(3, batches.Count);
        Assert.Equal(new[] { "g0", "g1", "g2" }, batches[0]);
        Assert.Equal(new[] { "g3", "g4", "g5" }, batches[1]);
        Assert.Equal(new[] { "g6" }, batches[2]);
    }

    [Fact]
    public void Batch_AnExactMultipleHasNoEmptyBatch_AndNoGuidsHasNoBatch()
    {
        Assert.Equal(2, GlobalIdMatching.Batch(new[] { "a", "b", "c", "d" }, 2).Count);
        Assert.Empty(GlobalIdMatching.Batch(new string[0], 2));
        Assert.Single(GlobalIdMatching.Batch(new[] { "a" }));
    }

    [Fact]
    public void Batch_TheDefaultKeepsAFewHundredGuidsInOneSearch()
    {
        var guids = Enumerable.Range(0, GlobalIdMatching.MaxGuidsPerSearch).Select(i => "g" + i).ToList();
        Assert.Single(GlobalIdMatching.Batch(guids));
        guids.Add("one more");
        Assert.Equal(2, GlobalIdMatching.Batch(guids).Count);
    }

    [Fact]
    public void Batch_RejectsABadArgument()
    {
        Assert.Throws<ArgumentNullException>(() => GlobalIdMatching.Batch(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlobalIdMatching.Batch(new[] { "a" }, 0));
    }

    // ── TryMapToGuids ────────────────────────────────────────────────────────

    [Fact]
    public void ItemsGoToTheGuidTheyCarry_InTheOrderTheyWereFound()
    {
        var found = new[] { new Item(0, "B"), new Item(1, "A"), new Item(2, "B"), new Item(3, "C") };

        Assert.True(GlobalIdMatching.TryMapToGuids(found, Read, new[] { "A", "B", "C", "D" }, out var byGuid));

        Assert.Equal("1", Show(byGuid["A"]));
        Assert.Equal("0,2", Show(byGuid["B"]));
        Assert.Equal("3", Show(byGuid["C"]));
        Assert.False(byGuid.ContainsKey("D")); // a GUID with no item is not listed
    }

    [Fact]
    public void NothingFoundIsAnEmptyResult()
    {
        Assert.True(GlobalIdMatching.TryMapToGuids(new Item[0], Read, new[] { "A" }, out var byGuid));
        Assert.Empty(byGuid);
    }

    [Fact]
    public void AnItemThatCannotBeGivenToAGuidMakesTheWholeResultUnusable()
    {
        var guids = new[] { "A", "B" };

        // The text is the GUID in another case, has a space, is missing, or is another GUID altogether: the search matched by a rule
        // this mapping does not know, so the caller has to look the GUIDs up one at a time.
        foreach (var text in new string?[] { "a", "A ", null, "Z" })
        {
            var found = new[] { new Item(0, "A"), new Item(1, text), new Item(2, "B") };
            Assert.False(GlobalIdMatching.TryMapToGuids(found, Read, guids, out var byGuid), "text: " + (text ?? "(none)"));
            Assert.Empty(byGuid);
        }
    }

    [Fact]
    public void ThePartsOfOneCombinedSearchEqualTheSeparateSearchesPerGuid()
    {
        var random = new Random(31);
        for (int round = 0; round < 200; round++)
        {
            // A model of items with GlobalIds (some shared by several items, some without) and some GUIDs to look up.
            var model = Enumerable.Range(0, random.Next(0, 200))
                .Select(i => new Item(i, random.Next(6) == 0 ? null : "id" + random.Next(30)))
                .ToList();
            var wanted = Enumerable.Range(0, random.Next(0, 25)).Select(_ => "id" + random.Next(40)).Distinct().ToList();

            // One search per GUID, as before: the items whose GlobalId equals it, in model order.
            var separately = new Dictionary<string, string>();
            foreach (var guid in wanted)
            {
                var matches = model.Where(i => i.GlobalId == guid).ToList();
                if (matches.Count > 0)
                {
                    separately[guid] = Show(matches);
                }
            }

            // One combined search: every item that matches any of the GUIDs, in model order, then mapped back.
            var set = new HashSet<string>(wanted);
            var combined = model.Where(i => i.GlobalId != null && set.Contains(i.GlobalId)).ToList();
            Assert.True(GlobalIdMatching.TryMapToGuids(combined, Read, wanted, out var byGuid));

            Assert.Equal(
                separately.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value),
                byGuid.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + Show(p.Value)));
        }
    }

    [Fact]
    public void BadArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => GlobalIdMatching.TryMapToGuids<Item>(null!, Read, new[] { "A" }, out _));
        Assert.Throws<ArgumentNullException>(() => GlobalIdMatching.TryMapToGuids(new Item[0], null!, new[] { "A" }, out _));
        Assert.Throws<ArgumentNullException>(() => GlobalIdMatching.TryMapToGuids(new Item[0], Read, null!, out _));
    }
}
